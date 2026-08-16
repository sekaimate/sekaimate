using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);
builder.Services.Configure<BrokerOptions>(builder.Configuration.GetSection("Broker"));
builder.Services.AddSingleton<TokenValidator>();
builder.Services.AddSingleton<EnrollmentStore>();
builder.Services.AddSingleton<MeetingStore>();
builder.Services.AddHttpClient<WebOidcTokenExchange>();
var app = builder.Build();
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedHost | ForwardedHeaders.XForwardedProto,
    KnownNetworks = { },
    KnownProxies = { },
});

// The current Compose deployment intentionally runs one Basis Server. Register it as the
// initial meeting so the admin can immediately issue a participant invite; future Kubernetes
// provisioning will add further records instead of replacing this bootstrap path.
{
    BrokerOptions broker = app.Services.GetRequiredService<IOptions<BrokerOptions>>().Value;
    MeetingStore meetings = app.Services.GetRequiredService<MeetingStore>();
    BrokerServerOptions? local = broker.FindServer("local");
    if (local != null)
    {
        // This must be a deployment setting. The broker never guesses localhost, an interface,
        // or a public DNS name because the correct target depends on the participant network.
        string host = Environment.GetEnvironmentVariable("BASIS_MEETING_PUBLIC_HOST")?.Trim() ?? string.Empty;
        ushort port = ushort.TryParse(Environment.GetEnvironmentVariable("SetPort"), out ushort configuredPort) ? configuredPort : (ushort)4296;
        string password = Environment.GetEnvironmentVariable("Password") ?? string.Empty;
        meetings.EnsureSingleComposeMeeting("local", "Local meeting", host, port, password, local.EffectiveTransportPublicKey);
    }
}

app.MapGet("/client-config/{serverId}", (string serverId, IOptions<BrokerOptions> options) =>
{
    BrokerOptions broker = options.Value;
    if (broker.FindServer(serverId) == null) return Results.NotFound();
    string? path = broker.ClientConfigPath(serverId);
    if (path == null || !File.Exists(path)) return Results.NotFound();
    return Results.Content(ClientConfig.RemoveSecrets(File.ReadAllText(path)), "application/json; charset=utf-8");
});

app.MapGet("/web-client-config/{serverId}", (string serverId, HttpContext http, IOptions<BrokerOptions> options) =>
{
    BrokerOptions broker = options.Value;
    BrokerServerOptions? server = broker.FindServer(serverId);
    if (server == null) return Results.NotFound();
    IReadOnlyList<ProviderOptions> providers = broker.GetOrganization().Providers ?? server.Providers ?? [];
    ProviderOptions[] webProviders = providers.Where(provider => provider.IsWebConfigured).ToArray();
    if (webProviders.Length == 0) return Results.Problem("Web SSO is not configured.", statusCode: 503);
    return Results.Json(CreateWebClientConfiguration(http, broker, server, webProviders));
});

app.MapGet("/admin/servers", (HttpContext http, IOptions<BrokerOptions> options) =>
{
    if (!AdminAuthorized(http, options.Value)) return Results.Unauthorized();
    return Results.Ok(options.Value.GetServers().Select(server => new
    {
        id = server.Id,
        ticketSigningKeyEnvironmentVariable = server.TicketSigningKeyEnvironmentVariable,
        transportPublicKeyEnvironmentVariable = server.TransportPublicKeyEnvironmentVariable,
        providers = server.Providers,
        ready = server.IsReady,
        hasTicketSigningKey = server.HasTicketSigningKey,
        hasTransportPublicKey = server.HasTransportPublicKey,
    }));
});

// Organization-wide identity settings.  Meetings inherit a snapshot when they are created;
// changing this does not silently change access control for an already running meeting.
app.MapGet("/admin/organization", (HttpContext http, IOptions<BrokerOptions> options) =>
{
    BrokerOptions broker = options.Value;
    if (!AdminAuthorized(http, broker)) return Results.Unauthorized();
    return Results.Ok(broker.GetOrganization());
});
app.MapPut("/admin/organization", async (OrganizationOptions organization, HttpContext http, IOptions<BrokerOptions> options, CancellationToken ct) =>
{
    BrokerOptions broker = options.Value;
    if (!AdminAuthorized(http, broker)) return Results.Unauthorized();
    if (!organization.IsStructurallyValid(out string error)) return Results.BadRequest(new { error });
    broker.Organization = organization;
    // The Docker development deployment has one bootstrap room named local. Keep that room in
    // sync so an organization can be configured before any Kubernetes meeting controller exists.
    BrokerServerOptions? local = broker.FindServer("local");
    if (local != null) local.Providers = organization.Providers!.Select(provider => provider.Copy()).ToList();
    await SaveBrokerConfigurationAsync(broker, ct);
    return Results.NoContent();
});

app.MapGet("/admin/meetings", (HttpContext http, IOptions<BrokerOptions> options, MeetingStore meetings) =>
{
    if (!AdminAuthorized(http, options.Value)) return Results.Unauthorized();
    return Results.Ok(meetings.List().Select(meeting => MeetingView(meeting, http, options.Value)));
});
app.MapPost("/admin/meetings", async (CreateMeetingRequest request, HttpContext http, IOptions<BrokerOptions> options, MeetingStore meetings, CancellationToken ct) =>
{
    BrokerOptions broker = options.Value;
    if (!AdminAuthorized(http, broker)) return Results.Unauthorized();
    if (!string.Equals(Environment.GetEnvironmentVariable("BASIS_CONTROL_PLANE_ALLOW_MANUAL_MEETINGS"), "true", StringComparison.OrdinalIgnoreCase))
        return Results.Problem("This deployment is configured for one Compose meeting. Kubernetes meeting provisioning is not enabled.", statusCode: StatusCodes.Status501NotImplemented);
    OrganizationOptions organization = broker.GetOrganization();
    if (!organization.IsStructurallyValid(out string organizationError))
        return Results.Problem("Organization SSO settings are incomplete: " + organizationError, statusCode: StatusCodes.Status409Conflict);
    if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Trim().Length > 120)
        return Results.BadRequest(new { error = "Meeting title is required and must be 120 characters or fewer." });
    if (!string.IsNullOrWhiteSpace(request.Id) && !MeetingIdentity.IsValid(request.Id))
        return Results.BadRequest(new { error = "Meeting ID may use up to 48 letters, numbers, '-' and '_'." });
    if (!string.IsNullOrWhiteSpace(request.Host) && !IsSafeHost(request.Host))
        return Results.BadRequest(new { error = "Connection host is invalid." });

    string id = MeetingIdentity.NewId(request.Id, request.Title, candidate => meetings.Exists(candidate) || broker.FindServer(candidate) != null);
    (string privateKey, string publicKey, string signingKey) = MeetingKeys.Generate();
    MeetingRecord meeting = new()
    {
        Id = id,
        Title = request.Title.Trim(),
        Host = request.Host?.Trim() ?? string.Empty,
        Port = request.Port is > 0 ? request.Port.Value : (ushort)4296,
        Password = string.IsNullOrWhiteSpace(request.Password) ? MeetingIdentity.RandomPassword() : request.Password,
        InviteToken = MeetingIdentity.RandomToken(),
        TicketSigningKey = signingKey,
        TransportPrivateKey = privateKey,
        TransportPublicKey = publicKey,
        Status = string.IsNullOrWhiteSpace(request.Host) ? "provisioning" : "ready",
        StatusDetail = string.IsNullOrWhiteSpace(request.Host) ? "Waiting for Kubernetes provisioning." : "External connection target configured.",
    };
    BrokerServerOptions admissionServer = new()
    {
        Id = id,
        Providers = organization.Providers!.Select(provider => provider.Copy()).ToList(),
        TicketSigningKey = signingKey,
        TransportPublicKey = publicKey,
    };
    broker.Servers ??= [];
    broker.Servers.Add(admissionServer);
    try
    {
        meetings.Add(meeting);
        await SaveBrokerConfigurationAsync(broker, ct);
    }
    catch
    {
        broker.Servers.Remove(admissionServer);
        throw;
    }
    return Results.Created($"/admin/meetings/{Uri.EscapeDataString(id)}", MeetingView(meeting, http, broker));
});
app.MapDelete("/admin/meetings/{meetingId}", async (string meetingId, HttpContext http, IOptions<BrokerOptions> options, MeetingStore meetings, CancellationToken ct) =>
{
    BrokerOptions broker = options.Value;
    if (!AdminAuthorized(http, broker)) return Results.Unauthorized();
    if (!meetings.Delete(meetingId, out _)) return Results.NotFound();
    broker.Servers?.RemoveAll(server => string.Equals(server.Id, meetingId, StringComparison.Ordinal));
    string? configPath = broker.ClientConfigPath(meetingId);
    if (configPath != null && File.Exists(configPath)) File.Delete(configPath);
    await SaveBrokerConfigurationAsync(broker, ct);
    return Results.NoContent();
});
app.MapPost("/admin/meetings/{meetingId}/invitations", (string meetingId, HttpContext http, IOptions<BrokerOptions> options, MeetingStore meetings) =>
{
    BrokerOptions broker = options.Value;
    if (!AdminAuthorized(http, broker)) return Results.Unauthorized();
    MeetingRecord? meeting = meetings.Find(meetingId);
    if (meeting == null) return Results.NotFound();
    return Results.Ok(new { url = $"{RequestOrigin(http, broker)}/join/{meeting.InviteToken}", meetingId = meeting.Id });
});
app.MapPut("/admin/servers/{serverId}", async (string serverId, BrokerServerOptions server, HttpContext http, IOptions<BrokerOptions> options, CancellationToken ct) =>
{
    BrokerOptions broker = options.Value;
    if (!AdminAuthorized(http, broker)) return Results.Unauthorized();
    server.Id = serverId;
    if (!server.IsStructurallyValid(out string error)) return Results.BadRequest(new { error });
    List<BrokerServerOptions> servers = broker.Servers ??= [];
    int index = servers.FindIndex(item => string.Equals(item.Id, serverId, StringComparison.Ordinal));
    if (index >= 0) servers[index] = server; else servers.Add(server);
    await SaveBrokerConfigurationAsync(broker, ct);
    return Results.NoContent();
});
app.MapDelete("/admin/servers/{serverId}", async (string serverId, HttpContext http, IOptions<BrokerOptions> options, CancellationToken ct) =>
{
    BrokerOptions broker = options.Value;
    if (!AdminAuthorized(http, broker)) return Results.Unauthorized();
    if (broker.Servers == null) return Results.NotFound();
    int removed = broker.Servers.RemoveAll(item => string.Equals(item.Id, serverId, StringComparison.Ordinal));
    if (removed == 0) return Results.NotFound();
    string? configPath = broker.ClientConfigPath(serverId);
    if (configPath != null && File.Exists(configPath)) File.Delete(configPath);
    await SaveBrokerConfigurationAsync(broker, ct);
    return Results.NoContent();
});
app.MapGet("/admin/client-config-template/{serverId}", (string serverId, HttpContext http, IOptions<BrokerOptions> options) =>
{
    BrokerOptions broker = options.Value;
    if (!AdminAuthorized(http, broker)) return Results.Unauthorized();
    BrokerServerOptions? server = broker.FindServer(serverId);
    if (server == null) return Results.NotFound();
    string publicKey = server.EffectiveTransportPublicKey;
    if (publicKey.Length == 0) return Results.Problem("The server transport public key is not available to this broker.", statusCode: 503);
    OrganizationOptions organization = broker.GetOrganization();
    IReadOnlyList<ProviderOptions> providers = organization.Providers is { Count: > 0 }
        ? organization.Providers : server.Providers!;
    return Results.Json(CreateClientConfiguration(http, broker, server.Id, publicKey, providers,
        organization.DefaultProviderId), new JsonSerializerOptions { WriteIndented = true });
});
app.MapGet("/admin/client-config/{serverId}", (string serverId, HttpContext http, IOptions<BrokerOptions> options) =>
{
    BrokerOptions broker = options.Value;
    if (!AdminAuthorized(http, broker)) return Results.Unauthorized();
    if (broker.FindServer(serverId) == null) return Results.NotFound();
    string? path = broker.ClientConfigPath(serverId);
    if (path == null || !File.Exists(path)) return Results.NotFound();
    return Results.File(path, "application/json; charset=utf-8", enableRangeProcessing: false);
});
app.MapPut("/admin/client-config/{serverId}", async (string serverId, HttpContext http, IOptions<BrokerOptions> options, CancellationToken ct) =>
{
    BrokerOptions broker = options.Value;
    if (!AdminAuthorized(http, broker)) return Results.Unauthorized();
    if (broker.FindServer(serverId) == null) return Results.NotFound();
    if (http.Request.ContentLength is > 262144) return Results.BadRequest(new { error = "Configuration is too large." });

    string json;
    using (var reader = new StreamReader(http.Request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true))
        json = await reader.ReadToEndAsync(ct);
    if (!ClientConfig.TryValidate(json, out string? error)) return Results.BadRequest(new { error });

    string? path = broker.ClientConfigPath(serverId);
    if (path == null) return Results.Problem("Client configuration storage is not configured.", statusCode: 503);
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    string temporaryPath = path + ".tmp";
    await File.WriteAllTextAsync(temporaryPath, json, new UTF8Encoding(false), ct);
    File.Move(temporaryPath, path, overwrite: true);
    return Results.NoContent();
});
app.MapPost("/admin/enrollment/{serverId}", (string serverId, HttpContext http, IOptions<BrokerOptions> options, EnrollmentStore enrollments) =>
{
    BrokerOptions broker = options.Value;
    if (!AdminAuthorized(http, broker)) return Results.Unauthorized();
    if (broker.FindServer(serverId) == null) return Results.NotFound();
    string token = enrollments.Issue(serverId);
    string baseUrl = $"{RequestOrigin(http, broker)}/enroll/{token}";
    return Results.Ok(new { url = baseUrl, webUrl = baseUrl + "/web", expiresInSeconds = 600 });
});
app.MapGet("/enroll/{token}", (string token, HttpContext http, IOptions<BrokerOptions> options, EnrollmentStore enrollments) =>
{
    if (!enrollments.Exists(token)) return Results.Content("This Basis SSO setup link has expired.", "text/plain; charset=utf-8", statusCode: 410);
    string configUrl = $"{RequestOrigin(http, options.Value)}/enroll/{Uri.EscapeDataString(token)}/config";
    string callback = "http://127.0.0.1:56831/basis-sso-config?url=" + Uri.EscapeDataString(configUrl);
    string callbackHtml = System.Net.WebUtility.HtmlEncode(callback);
    string page = $$"""
<!doctype html><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>SekaiMate — Basis 設定</title>
<style>body{margin:0;background:#f5f7fa;color:#202332;font:16px system-ui,-apple-system,sans-serif}.page{max-width:44rem;margin:0 auto;padding:4rem 1.25rem}.card{background:#fff;border:1px solid #d5dbdb;border-radius:16px;padding:2rem;box-shadow:0 4px 16px #172b4d12}.eyebrow{color:#553bc0;font-size:.8rem;font-weight:800;letter-spacing:.12em}h1{margin:.5rem 0 1rem;font-size:2rem}p{line-height:1.65}.button{display:inline-block;margin:1rem 0;padding:.8rem 1.2rem;border-radius:8px;background:#553bc0;color:#fff;text-decoration:none;font-weight:700}.hint{color:#5f6b7a;font-size:.9rem}</style>
<main class="page"><section class="card"><div class="eyebrow">SEKAIMATE</div><h1>組織設定を Basis に適用</h1><p>Basis を起動した状態で、下のボタンを押してください。ログインに必要な組織設定をこの端末の Basis に送ります。</p>
<p><a class="button" href="{{callbackHtml}}">Basis に設定を送る</a></p>
<p class="hint">設定 URL は 10 分間・一回限りです。送信後は Basis に戻ってログインしてください。</p></section></main>
""";
    return Results.Content(page, "text/html; charset=utf-8");
});
app.MapGet("/enroll/{token}/web", (string token, HttpContext http, IOptions<BrokerOptions> options, EnrollmentStore enrollments) =>
{
    if (!enrollments.Exists(token)) return Results.Content("This Basis SSO setup link has expired.", "text/plain; charset=utf-8", statusCode: 410);
    string? webOrigin = options.Value.AllowedWebOrigins?.Select(origin => origin?.Trim().TrimEnd('/'))
        .FirstOrDefault(origin => Uri.TryCreate(origin, UriKind.Absolute, out Uri? uri)
            && (uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback))
            && !string.IsNullOrWhiteSpace(uri.Host));
    if (string.IsNullOrWhiteSpace(webOrigin)) return Results.Problem("No WebGL origin is configured.", statusCode: 503);
    string configUrl = $"{RequestOrigin(http, options.Value)}/enroll/{Uri.EscapeDataString(token)}/web-config";
    return Results.Redirect($"{webOrigin}/?basisEnrollment=1&configUrl={Uri.EscapeDataString(configUrl)}");
});
app.MapGet("/enroll/{token}/web-config", (string token, HttpContext http, IOptions<BrokerOptions> options, EnrollmentStore enrollments, MeetingStore meetings) =>
{
    BrokerOptions broker = options.Value;
    if (!TryApplyWebCors(http, broker)) return Results.StatusCode(StatusCodes.Status403Forbidden);
    string? serverId = enrollments.Take(token);
    if (serverId == null) return Results.Content("This Basis SSO setup link has expired or was already used.", "text/plain; charset=utf-8", statusCode: 410);
    BrokerServerOptions? server = broker.FindServer(serverId);
    if (server == null) return Results.Problem("Broker server is not configured.", statusCode: 503);
    IReadOnlyList<ProviderOptions> providers = broker.GetOrganization().Providers is { Count: > 0 }
        ? broker.GetOrganization().Providers : server.Providers ?? [];
    return Results.Json(CreateWebClientConfiguration(http, broker, server, providers, enrollmentOnly: true));
});
app.MapGet("/enroll/{token}/config", (string token, HttpContext http, IOptions<BrokerOptions> options, EnrollmentStore enrollments, MeetingStore meetings) =>
{
    string? serverId = enrollments.Take(token);
    if (serverId == null) return Results.Content("This Basis SSO setup link has expired or was already used.", "text/plain; charset=utf-8", statusCode: 410);
    BrokerOptions broker = options.Value;
    BrokerServerOptions? server = broker.FindServer(serverId);
    MeetingRecord? meeting = meetings.Find(serverId);
    OrganizationOptions organization = broker.GetOrganization();
    IReadOnlyList<ProviderOptions> providers = organization.Providers is { Count: > 0 }
        ? organization.Providers : server?.Providers ?? [];
    string publicKey = meeting?.TransportPublicKey ?? server?.EffectiveTransportPublicKey ?? string.Empty;
    if (server == null || providers.Count == 0 || string.IsNullOrWhiteSpace(publicKey)) return Results.NotFound();
    return Results.Json(CreateClientConfiguration(http, broker, serverId, publicKey, providers, organization.DefaultProviderId));
});

// A join invitation carries the organization configuration for this one meeting. This means a
// fresh client can start from the participant URL; no organization-specific OIDC values need to
// be embedded in a Unity build. The endpoint intentionally exposes only native-client values,
// never ticket-signing or transport private keys.
app.MapGet("/join/{token}/config", (string token, HttpContext http, IOptions<BrokerOptions> options, MeetingStore meetings) =>
{
    MeetingRecord? meeting = meetings.FindInvite(token);
    if (meeting == null) return Results.NotFound();
    BrokerOptions broker = options.Value;
    if (!TryApplyWebCors(http, broker)) return Results.StatusCode(StatusCodes.Status403Forbidden);
    http.Response.Headers.CacheControl = "no-store";
    BrokerServerOptions? server = broker.FindServer(meeting.Id);
    OrganizationOptions organization = broker.GetOrganization();
    IReadOnlyList<ProviderOptions> providers = organization.Providers is { Count: > 0 }
        ? organization.Providers
        : server?.Providers ?? [];
    if (server == null || providers.Count == 0 || string.IsNullOrWhiteSpace(meeting.TransportPublicKey))
        return Results.Problem("Meeting organization configuration is incomplete.", statusCode: 503);
    return Results.Json(CreateClientConfiguration(http, broker, meeting.Id, meeting.TransportPublicKey, providers,
        organization.DefaultProviderId));
});

app.MapGet("/join/{token}/web-config", (string token, HttpContext http, IOptions<BrokerOptions> options, MeetingStore meetings) =>
{
    MeetingRecord? meeting = meetings.FindInvite(token);
    if (meeting == null) return Results.NotFound();
    BrokerOptions broker = options.Value;
    if (!TryApplyWebCors(http, broker)) return Results.StatusCode(StatusCodes.Status403Forbidden);
    BrokerServerOptions? server = broker.FindServer(meeting.Id);
    OrganizationOptions organization = broker.GetOrganization();
    IReadOnlyList<ProviderOptions> providers = organization.Providers is { Count: > 0 }
        ? organization.Providers
        : server?.Providers ?? [];
    if (server == null || providers.Count == 0 || string.IsNullOrWhiteSpace(meeting.TransportPublicKey))
        return Results.Problem("Meeting Web SSO configuration is incomplete.", statusCode: 503);
    http.Response.Headers.CacheControl = "no-store";
    return Results.Json(CreateWebClientConfiguration(http, broker, server, providers,
        publicKeyOverride: meeting.TransportPublicKey));
});

// Web clients receive meeting details over HTTPS instead of carrying the server password
// and generated username in the participant URL. The invitation token remains the only
// participant-facing capability, and the response is deliberately not cacheable.
app.MapGet("/join/{token}/web-manifest", (string token, HttpContext http, IOptions<BrokerOptions> options, MeetingStore meetings) =>
{
    MeetingRecord? meeting = meetings.FindInvite(token);
    if (meeting == null) return Results.NotFound();
    if (!TryApplyWebCors(http, options.Value)) return Results.StatusCode(StatusCodes.Status403Forbidden);
    if (!string.Equals(meeting.Status, "ready", StringComparison.OrdinalIgnoreCase)
        || string.IsNullOrWhiteSpace(meeting.Host))
        return Results.Content("This meeting is not ready yet.", "text/plain; charset=utf-8", statusCode: 409);

    if (!Uri.TryCreate(RequestOrigin(http, options.Value), UriKind.Absolute, out Uri? brokerOrigin))
        return Results.Problem("Broker origin is invalid.", statusCode: 503);
    http.Response.Headers.CacheControl = "no-store";
    string? configuredWebSocketUri = Environment.GetEnvironmentVariable("BASIS_WEB_SOCKET_URI");
    string websocketUri = !string.IsNullOrWhiteSpace(configuredWebSocketUri)
        ? configuredWebSocketUri.Trim()
        : $"{(brokerOrigin.Scheme == Uri.UriSchemeHttps ? "wss" : "ws")}://{brokerOrigin.Authority}/basis";
    string configUrl = $"{RequestOrigin(http, options.Value)}/join/{Uri.EscapeDataString(token)}/web-config";
    return Results.Json(new
    {
        configUrl,
        websocketUri,
        userName = "web-guest-" + token[..Math.Min(token.Length, 8)],
        password = meeting.Password,
    }, new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = null });
});

// A participant-facing URL. It never contains OAuth secrets, the admission signing key, or the
// meeting password. Native clients receive the opaque manifest through the loopback listener;
// Web clients follow the separate WebGL manifest URL below.
app.MapGet("/join/{token}", (string token, HttpContext http, IOptions<BrokerOptions> options, MeetingStore meetings) =>
{
    MeetingRecord? meeting = meetings.FindInvite(token);
    if (meeting == null) return Results.Content("This meeting invitation is invalid or has been revoked.", "text/plain; charset=utf-8", statusCode: 404);
    if (!string.Equals(meeting.Status, "ready", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(meeting.Host))
        return Results.Content("This meeting is not ready yet.", "text/plain; charset=utf-8", statusCode: 409);

    string manifestUrl = $"{RequestOrigin(http, options.Value)}/join/{Uri.EscapeDataString(token)}/manifest";
    string loopbackUrl = "http://127.0.0.1:56831/basis-join?url=" + Uri.EscapeDataString(manifestUrl);
    string webJoinUrl = BuildWebJoinUrl(meeting, token, options.Value, http);
    string encodedWebJoinUrl = System.Net.WebUtility.HtmlEncode(webJoinUrl);
    string loopbackScriptUrl = System.Text.Json.JsonSerializer.Serialize(loopbackUrl);
    string title = System.Net.WebUtility.HtmlEncode(meeting.Title);
    string page = $$"""
<!doctype html><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>{{title}} — SekaiMate</title><style>body{margin:0;background:#f5f7fa;color:#202332;font:16px system-ui,-apple-system,sans-serif}.page{max-width:44rem;margin:0 auto;padding:4rem 1.25rem}.card{background:#fff;border:1px solid #d5dbdb;border-radius:16px;padding:2rem;box-shadow:0 4px 16px #172b4d12}.eyebrow{color:#553bc0;font-size:.8rem;font-weight:800;letter-spacing:.12em}h1{margin:.5rem 0 1rem;font-size:2rem}p{line-height:1.65}.actions{display:flex;flex-wrap:wrap;gap:.75rem;margin:1.25rem 0}.button{display:inline-block;padding:.8rem 1.2rem;border:0;border-radius:8px;background:#553bc0;color:#fff;text-decoration:none;font:inherit;font-weight:700;cursor:pointer}.button.secondary{background:#364152}.hint{color:#5f6b7a;font-size:.9rem}iframe{display:none}</style>
<main class="page"><section class="card"><div class="eyebrow">SEKAIMATE</div><h1>{{title}}</h1><p id="status">参加方法を選択してください。</p><p class="actions"><a class="button" href="{{encodedWebJoinUrl}}">Webで参加</a><button class="button secondary" id="native-join" type="button">ネイティブで参加</button></p><p class="hint">Web版はブラウザで開きます。ネイティブ版はBasisアプリが起動している端末で選択してください。</p><iframe id="basis-join" title="Basis join bridge"></iframe></section></main>
<script>const bridgeUrl={{loopbackScriptUrl}},status=document.querySelector('#status'),frame=document.querySelector('#basis-join');addEventListener('message',e=>e.data==='basis-join-received'&&(status.textContent='Basisに会議への参加を渡しました。Basisに戻ってください。'));document.querySelector('#native-join').addEventListener('click',()=>{status.textContent='Basisに会議情報を渡しています…';frame.src=bridgeUrl;setTimeout(()=>status.textContent='Basisが起動していることを確認してください。',2500)});</script>
""";
    return Results.Content(page, "text/html; charset=utf-8");
});
app.MapGet("/join/{token}/open", (string token, HttpContext http, MeetingStore meetings) =>
{
    MeetingRecord? meeting = meetings.FindInvite(token);
    if (meeting == null) return Results.Content("This meeting invitation is invalid or has been revoked.", "text/plain; charset=utf-8", statusCode: 404);
    if (!string.Equals(meeting.Status, "ready", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(meeting.Host))
        return Results.Content("This meeting is not ready yet.", "text/plain; charset=utf-8", statusCode: 409);
    return Results.Redirect($"/join/{Uri.EscapeDataString(token)}");
});
app.MapGet("/join/{token}/manifest", (string token, HttpContext http, IOptions<BrokerOptions> options, MeetingStore meetings) =>
{
    MeetingRecord? meeting = meetings.FindInvite(token);
    if (meeting == null) return Results.NotFound();
    http.Response.Headers.CacheControl = "no-store";
    return Results.Json(new
    {
        meeting = new { id = meeting.Id, title = meeting.Title },
        configUrl = $"{RequestOrigin(http, options.Value)}/join/{Uri.EscapeDataString(token)}/config",
        connection = new { host = meeting.Host, port = meeting.Port, password = meeting.Password },
        serverTransport = TransportConfig(http, options.Value, meeting.TransportPublicKey, meeting.Id),
    });
});

app.MapGet("/health", (IOptions<BrokerOptions> options) =>
{
    BrokerOptions broker = options.Value;
    var servers = broker.GetServers().Select(server => new
    {
        id = server.Id,
        ready = server.IsReady,
        providers = server.Providers!.Select(provider => provider.Id).Where(id => !string.IsNullOrWhiteSpace(id)),
    }).ToList();
    bool ready = servers.Count > 0 && servers.All(server => server.ready);
    return ready
        ? Results.Ok(new { status = "ready", servers })
        : Results.Json(new { status = "not_ready", error = "Configure providers and ticket signing keys for every broker server.", servers }, statusCode: StatusCodes.Status503ServiceUnavailable);
});

app.MapMethods("/admission/{serverId}", new[] { "OPTIONS" }, (string serverId, HttpContext http, IOptions<BrokerOptions> options) =>
{
    if (!TryApplyAdmissionCors(http, options.Value)) return Results.StatusCode(StatusCodes.Status403Forbidden);
    return Results.NoContent();
});
app.MapPost("/admission/{serverId}", (AdmissionRequest request, string serverId, HttpContext http, TokenValidator validator, IOptions<BrokerOptions> options, CancellationToken ct) =>
    CreateAdmissionAsync(request, serverId, http, validator, options.Value, ct));

app.MapMethods("/web-oidc/{serverId}/{providerId}/token", new[] { "OPTIONS" },
    (HttpContext http, IOptions<BrokerOptions> options) =>
{
    if (!TryApplyAdmissionCors(http, options.Value)) return Results.StatusCode(StatusCodes.Status403Forbidden);
    return Results.NoContent();
});
app.MapPost("/web-oidc/{serverId}/{providerId}/token", async (
    string serverId,
    string providerId,
    HttpContext http,
    IOptions<BrokerOptions> options,
    WebOidcTokenExchange exchange,
    CancellationToken ct) =>
{
    BrokerOptions broker = options.Value;
    if (!TryApplyAdmissionCors(http, broker)) return Results.StatusCode(StatusCodes.Status403Forbidden);
    if (!http.Request.HasFormContentType) return Results.BadRequest(new { error = "form_encoded_request_required" });
    BrokerServerOptions? server = broker.FindServer(serverId);
    ProviderOptions? provider = (broker.GetOrganization().Providers ?? server?.Providers ?? [])
        .FirstOrDefault(candidate => string.Equals(candidate.Id, providerId, StringComparison.Ordinal));
    if (server == null || provider == null) return Results.NotFound();
    if (!provider.TryGetWebCredential(out Uri? tokenEndpoint, out string clientId, out string clientSecret))
        return Results.Problem("Web SSO provider credentials are incomplete.", statusCode: 503);

    IFormCollection form = await http.Request.ReadFormAsync(ct);
    string grantType = form["grant_type"].ToString();
    var forwarded = new Dictionary<string, string>(StringComparer.Ordinal);
    if (grantType == "authorization_code")
    {
        string redirectUri = form["redirect_uri"].ToString();
        if (!AllowedWebRedirect(broker, redirectUri)) return Results.BadRequest(new { error = "invalid_redirect_uri" });
        foreach (string key in new[] { "grant_type", "code", "redirect_uri", "code_verifier" })
            if (!string.IsNullOrWhiteSpace(form[key])) forwarded[key] = form[key].ToString();
        if (!forwarded.ContainsKey("code") || !forwarded.ContainsKey("code_verifier"))
            return Results.BadRequest(new { error = "invalid_request" });
    }
    else if (grantType == "refresh_token")
    {
        forwarded["grant_type"] = grantType;
        forwarded["refresh_token"] = form["refresh_token"].ToString();
        if (string.IsNullOrWhiteSpace(forwarded["refresh_token"])) return Results.BadRequest(new { error = "invalid_request" });
    }
    else return Results.BadRequest(new { error = "unsupported_grant_type" });

    using HttpResponseMessage response = await exchange.SendAsync(tokenEndpoint!, clientId, clientSecret, forwarded, ct);
    string body = await response.Content.ReadAsStringAsync(ct);
    http.Response.Headers.CacheControl = "no-store";
    return Results.Content(body, response.Content.Headers.ContentType?.ToString() ?? "application/json", statusCode: (int)response.StatusCode);
});

static async Task<IResult> CreateAdmissionAsync(AdmissionRequest request, string serverId, HttpContext http,
    TokenValidator validator, BrokerOptions broker, CancellationToken ct)
{
    if (!TryApplyAdmissionCors(http, broker)) return Results.StatusCode(StatusCodes.Status403Forbidden);
    http.Response.Headers.CacheControl = "no-store";
    if (string.IsNullOrWhiteSpace(request.IdToken) || string.IsNullOrWhiteSpace(request.Did)
        || request.IdToken.Length > 16384 || request.Did.Length > 256)
        return Results.BadRequest(new { error = "idToken and did are required" });
    if (request.Did.Contains('\n') || request.Did.Contains('\r')) return Results.BadRequest(new { error = "invalid did" });

    IReadOnlyList<BrokerServerOptions> servers = broker.GetServers();
    BrokerServerOptions? server = servers.FirstOrDefault(candidate => string.Equals(candidate.Id, serverId, StringComparison.Ordinal));
    if (server == null) return Results.NotFound();
    if (!server.IsReady) return Results.Problem("Broker server is not configured.", statusCode: 503);

    ValidatedIdentity? identity = await validator.ValidateAsync(request.IdToken, server.Providers!, ct);
    if (identity is null) return Results.Unauthorized();
    string key = server.EffectiveTicketSigningKey;
    return Results.Ok(new { ticket = Ticket.Create(key, identity.Issuer, identity.Subject, request.Did) });
}

static bool TryApplyAdmissionCors(HttpContext http, BrokerOptions broker)
{
    string? origin = http.Request.Headers.Origin.FirstOrDefault();
    if (string.IsNullOrWhiteSpace(origin)) return true;
    string normalizedOrigin = origin.TrimEnd('/');
    bool allowed = broker.AllowedWebOrigins?.Any(value => string.Equals(value?.Trim().TrimEnd('/'), normalizedOrigin, StringComparison.OrdinalIgnoreCase)) == true;
    if (!allowed) return false;
    http.Response.Headers.AccessControlAllowOrigin = normalizedOrigin;
    http.Response.Headers.AccessControlAllowMethods = "POST, OPTIONS";
    http.Response.Headers.AccessControlAllowHeaders = "Content-Type";
    http.Response.Headers.AccessControlMaxAge = "600";
    http.Response.Headers.Vary = "Origin";
    return true;
}

static bool TryApplyWebCors(HttpContext http, BrokerOptions broker)
{
    string? origin = http.Request.Headers.Origin.FirstOrDefault();
    if (string.IsNullOrWhiteSpace(origin)) return true;
    string normalizedOrigin = origin.TrimEnd('/');
    bool allowed = broker.AllowedWebOrigins?.Any(value => string.Equals(value?.Trim().TrimEnd('/'), normalizedOrigin, StringComparison.OrdinalIgnoreCase)) == true;
    if (!allowed) return false;
    http.Response.Headers.AccessControlAllowOrigin = normalizedOrigin;
    http.Response.Headers.AccessControlAllowMethods = "GET, OPTIONS";
    http.Response.Headers.AccessControlAllowHeaders = "Content-Type";
    http.Response.Headers.Vary = "Origin";
    return true;
}

static bool AllowedWebRedirect(BrokerOptions broker, string value)
{
    if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? redirect) || redirect.AbsolutePath != "/sso-callback") return false;
    string origin = redirect.GetLeftPart(UriPartial.Authority).TrimEnd('/');
    return broker.AllowedWebOrigins?.Any(allowed => string.Equals(allowed?.Trim().TrimEnd('/'), origin, StringComparison.OrdinalIgnoreCase)) == true;
}

app.Run();

static bool AdminAuthorized(HttpContext http, BrokerOptions broker)
{
    // The development Compose gateway binds only to loopback and explicitly enables this mode,
    // so its admin UI can be used without copying a token into every browser session. A public
    // deployment must leave this false and protect the gateway with real operator authentication.
    if (broker.AllowUnauthenticatedAdmin) return true;
    string configured = Environment.GetEnvironmentVariable(broker.AdminTokenEnvironmentVariable ?? string.Empty) ?? string.Empty;
    if (configured.Length < 32) return false;
    string? authorization = http.Request.Headers.Authorization;
    const string prefix = "Bearer ";
    if (authorization == null || !authorization.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
    byte[] expected = Encoding.UTF8.GetBytes(configured);
    byte[] actual = Encoding.UTF8.GetBytes(authorization[prefix.Length..]);
    return expected.Length == actual.Length && CryptographicOperations.FixedTimeEquals(expected, actual);
}

static string RequestOrigin(HttpContext http, BrokerOptions broker)
{
    if (Uri.TryCreate(broker.PublicBaseUrl, UriKind.Absolute, out Uri? configured)
        && (configured.Scheme == Uri.UriSchemeHttps || configured.Scheme == Uri.UriSchemeHttp))
        return configured.GetLeftPart(UriPartial.Authority);
    return $"{http.Request.Scheme}://{http.Request.Host}";
}

static string BuildWebJoinUrl(MeetingRecord meeting, string token, BrokerOptions broker, HttpContext http)
{
    string? webOrigin = broker.AllowedWebOrigins?.Select(origin => origin?.Trim().TrimEnd('/'))
        .FirstOrDefault(origin => Uri.TryCreate(origin, UriKind.Absolute, out Uri? uri)
            && (uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback))
            && !string.IsNullOrWhiteSpace(uri.Host));
    if (string.IsNullOrWhiteSpace(webOrigin)) return string.Empty;
    string manifestUrl = $"{RequestOrigin(http, broker)}/join/{Uri.EscapeDataString(token)}/web-manifest";
    return $"{webOrigin}/?basisMeeting=1&meetingUrl={Uri.EscapeDataString(manifestUrl)}";
}

static object TransportConfig(HttpContext http, BrokerOptions broker, string publicKey, string serverId)
{
    string endpoint = $"{RequestOrigin(http, broker)}/admission/{serverId}";
    // Unity's TLS implementation does not inherit a browser's one-time exception for a
    // locally generated development certificate. Convey this opt-in only when the generated
    // endpoint itself resolves to loopback; the client validates the same restriction again.
    bool allowUntrustedLoopbackCertificate = Uri.TryCreate(endpoint, UriKind.Absolute, out Uri? uri) && uri.IsLoopback;
    return new
    {
        serverPublicKey = publicKey,
        admissionEndpoint = endpoint,
        allowUntrustedLoopbackCertificate,
    };
}

static object CreateClientConfiguration(HttpContext http, BrokerOptions broker, string serverId, string publicKey,
    IReadOnlyList<ProviderOptions> providers, string? defaultProviderId)
{
    return new
    {
        defaultProviderId = string.IsNullOrWhiteSpace(defaultProviderId) ? providers.FirstOrDefault()?.Id : defaultProviderId,
        serverTransport = TransportConfig(http, broker, publicKey, serverId),
        providers = providers.Select(provider => new
        {
            id = provider.Id,
            label = string.IsNullOrWhiteSpace(provider.Label) ? provider.Id : provider.Label,
            issuer = provider.Issuer,
            clientId = provider.Audience,
            clientSecret = provider.ClientSecret,
            scopes = new[] { "openid", "email", "profile" },
            displayNameClaims = new[] { "name", "preferred_username", "email" },
            access = new
            {
                allowedGroups = provider.AllowedGroups ?? [],
                allowedClaims = (provider.AllowedHostedDomains ?? []).Select(domain => new
                {
                    claim = "hd",
                    values = new[] { domain },
                }),
            },
        }),
        redirect = new { mode = "loopback", host = "127.0.0.1", port = 0, path = "/callback" },
        enforcement = new { allowOfflineWithinTokenValidity = true },
    };
}

static object CreateWebClientConfiguration(HttpContext http, BrokerOptions broker, BrokerServerOptions server,
    IReadOnlyList<ProviderOptions> providers, string? publicKeyOverride = null, bool enrollmentOnly = false)
{
    OrganizationOptions organization = broker.GetOrganization();
    return new
    {
        defaultProviderId = providers.Any(provider => string.Equals(provider.Id, organization.DefaultProviderId, StringComparison.Ordinal))
            ? organization.DefaultProviderId : providers[0].Id,
        organizationEnrollment = enrollmentOnly,
        serverTransport = enrollmentOnly
            ? new { serverPublicKey = "", admissionEndpoint = "" }
            : TransportConfig(http, broker, publicKeyOverride ?? server.EffectiveTransportPublicKey, server.Id!),
        providers = providers.Select(provider => new
        {
            id = provider.Id,
            label = string.IsNullOrWhiteSpace(provider.Label) ? provider.Id : provider.Label,
            issuer = provider.Issuer,
            clientId = provider.WebClientId,
            tokenEndpoint = $"{RequestOrigin(http, broker)}/web-oidc/{Uri.EscapeDataString(server.Id!)}/{Uri.EscapeDataString(provider.Id!)}" + "/token",
            scopes = new[] { "openid", "email", "profile" },
            extraAuthParams = HostedDomainPolicy.AuthorizationParameters(provider.AllowedHostedDomains),
            displayNameClaims = new[] { "name", "preferred_username", "email" },
            access = new
            {
                allowedGroups = provider.AllowedGroups ?? [],
                allowedClaims = (provider.AllowedHostedDomains ?? []).Select(domain => new { claim = "hd", values = new[] { domain } }),
            },
        }),
        redirect = new { mode = "browser", path = "/sso-callback" },
        enforcement = new { allowOfflineWithinTokenValidity = true },
    };
}

static async Task SaveBrokerConfigurationAsync(BrokerOptions broker, CancellationToken ct)
{
    string path = Environment.GetEnvironmentVariable("BASIS_SSO_BROKER_CONFIG_PATH") ?? Path.Combine(AppContext.BaseDirectory, "appsettings.json");
    string temporary = path + ".tmp";
    string json = JsonSerializer.Serialize(new { Broker = broker }, new JsonSerializerOptions { WriteIndented = true });
    await File.WriteAllTextAsync(temporary, json, new UTF8Encoding(false), ct);
    File.Move(temporary, path, overwrite: true);
}

static object MeetingView(MeetingRecord meeting, HttpContext http, BrokerOptions broker) => new
{
    id = meeting.Id,
    title = meeting.Title,
    status = meeting.Status,
    statusDetail = meeting.StatusDetail,
    host = meeting.Host,
    port = meeting.Port,
    createdAt = meeting.CreatedAt,
    updatedAt = meeting.UpdatedAt,
    joinUrl = $"{RequestOrigin(http, broker)}/join/{meeting.InviteToken}",
    invitationReady = string.Equals(meeting.Status, "ready", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(meeting.Host),
};

static bool IsSafeHost(string value)
{
    string host = value.Trim();
    if (host.Length is < 1 or > 253 || host.Contains('/') || host.Contains('?') || host.Contains('#') || host.Any(char.IsWhiteSpace)) return false;
    return Uri.CheckHostName(host.Trim('[', ']')) is UriHostNameType.Dns or UriHostNameType.IPv4 or UriHostNameType.IPv6;
}

sealed record AdmissionRequest(string IdToken, string Did);
sealed record ValidatedIdentity(string Issuer, string Subject);
sealed class BrokerOptions
{
    public List<BrokerServerOptions>? Servers { get; set; }
    public string? ClientConfigDirectory { get; set; }
    public string? AdminTokenEnvironmentVariable { get; set; }
    public bool AllowUnauthenticatedAdmin { get; set; }
    public string? PublicBaseUrl { get; set; }
    public List<string>? AllowedWebOrigins { get; set; }
    public OrganizationOptions? Organization { get; set; }

    public IReadOnlyList<BrokerServerOptions> GetServers() => Servers ?? [];
    public BrokerServerOptions? FindServer(string id) => GetServers().FirstOrDefault(server => string.Equals(server.Id, id, StringComparison.Ordinal));
    public OrganizationOptions GetOrganization()
    {
        if (Organization?.Providers is { Count: > 0 }) return Organization;
        // The initial Docker install predates organization settings. Treat its first existing
        // SSO server policy as the organization default until it is saved through the new UI.
        BrokerServerOptions? legacy = GetServers().FirstOrDefault(server => server.Providers is { Count: > 0 });
        return new OrganizationOptions
        {
            DefaultProviderId = legacy?.Providers?.FirstOrDefault()?.Id ?? string.Empty,
            Providers = legacy?.Providers?.Select(provider => provider.Copy()).ToList() ?? [],
        };
    }
    public string? ClientConfigPath(string serverId)
    {
        if (string.IsNullOrWhiteSpace(ClientConfigDirectory) || FindServer(serverId) == null) return null;
        return Path.Combine(ClientConfigDirectory, serverId + ".json");
    }
}
sealed class BrokerServerOptions
{
    public string? Id { get; set; }
    public string? TicketSigningKeyEnvironmentVariable { get; set; }
    public string? TransportPublicKeyEnvironmentVariable { get; set; }
    // Per-meeting keys are stored in the protected broker control-plane volume. Existing
    // single-server Docker deployments continue to use the environment-variable form.
    public string? TicketSigningKey { get; set; }
    public string? TransportPublicKey { get; set; }
    public List<ProviderOptions>? Providers { get; set; }
    public string EffectiveTicketSigningKey => !string.IsNullOrWhiteSpace(TicketSigningKey)
        ? TicketSigningKey : Environment.GetEnvironmentVariable(TicketSigningKeyEnvironmentVariable ?? string.Empty) ?? string.Empty;
    public string EffectiveTransportPublicKey => !string.IsNullOrWhiteSpace(TransportPublicKey)
        ? TransportPublicKey : Environment.GetEnvironmentVariable(TransportPublicKeyEnvironmentVariable ?? string.Empty) ?? string.Empty;
    public bool HasTicketSigningKey =>
        EffectiveTicketSigningKey.Length >= 32;
    public bool HasTransportPublicKey =>
        !string.IsNullOrWhiteSpace(EffectiveTransportPublicKey);
    public bool IsReady => !string.IsNullOrWhiteSpace(Id)
        && Providers is { Count: > 0 }
        && HasTicketSigningKey
        && HasTransportPublicKey;
    public bool IsStructurallyValid(out string error)
    {
        if (string.IsNullOrWhiteSpace(Id) || Id.Any(c => !(char.IsLetterOrDigit(c) || c is '-' or '_'))) { error = "Server ID must use letters, numbers, '-' or '_'."; return false; }
        if (string.IsNullOrWhiteSpace(TicketSigningKey) && string.IsNullOrWhiteSpace(TicketSigningKeyEnvironmentVariable)) { error = "A ticket-signing key or key environment variable is required."; return false; }
        if (string.IsNullOrWhiteSpace(TransportPublicKey) && string.IsNullOrWhiteSpace(TransportPublicKeyEnvironmentVariable)) { error = "A transport public key or key environment variable is required."; return false; }
        if (Providers is not { Count: > 0 } || Providers.Any(provider => !provider.IsStructurallyValid())) { error = "Every provider needs an ID, issuer, client ID, and HTTPS JWKS URL."; return false; }
        error = string.Empty;
        return true;
    }
}
sealed class OrganizationOptions
{
    public string? DisplayName { get; set; }
    public string? DefaultProviderId { get; set; }
    public List<ProviderOptions>? Providers { get; set; }
    public bool IsStructurallyValid(out string error)
    {
        if (Providers is not { Count: > 0 } || Providers.Any(provider => !provider.IsStructurallyValid())) { error = "Configure at least one valid identity provider."; return false; }
        if (!string.IsNullOrWhiteSpace(DefaultProviderId) && !Providers.Any(provider => string.Equals(provider.Id, DefaultProviderId, StringComparison.OrdinalIgnoreCase))) { error = "The default provider must be enabled."; return false; }
        error = string.Empty;
        return true;
    }
}
sealed class ProviderOptions
{
    public string? Id { get; set; }
    public string? Label { get; set; }
    public string? Issuer { get; set; }
    public string? Audience { get; set; }
    public string? ClientSecret { get; set; }
    public string? WebClientId { get; set; }
    public string? WebClientSecret { get; set; }
    public string? TokenEndpoint { get; set; }
    public string? JwksUri { get; set; }
    public List<string>? AllowedHostedDomains { get; set; }
    public List<string>? AllowedGroups { get; set; }
    public bool IsWebConfigured => !string.IsNullOrWhiteSpace(WebClientId)
        && !string.IsNullOrWhiteSpace(WebClientSecret)
        && Uri.TryCreate(TokenEndpoint, UriKind.Absolute, out Uri? endpoint)
        && endpoint.Scheme == Uri.UriSchemeHttps;
    public ProviderOptions Copy() => new()
    {
        Id = Id,
        Label = Label,
        Issuer = Issuer,
        Audience = Audience,
        ClientSecret = ClientSecret,
        WebClientId = WebClientId,
        WebClientSecret = WebClientSecret,
        TokenEndpoint = TokenEndpoint,
        JwksUri = JwksUri,
        AllowedHostedDomains = AllowedHostedDomains?.ToList() ?? [],
        AllowedGroups = AllowedGroups?.ToList() ?? [],
    };
    public bool IsStructurallyValid() => !string.IsNullOrWhiteSpace(Id)
        && Uri.TryCreate(Issuer, UriKind.Absolute, out Uri? issuer)
        && issuer.Scheme == Uri.UriSchemeHttps
        && (!string.IsNullOrWhiteSpace(Audience) || IsWebConfigured)
        && Uri.TryCreate(JwksUri, UriKind.Absolute, out Uri? jwks)
        && jwks.Scheme == Uri.UriSchemeHttps;
    public bool TryGetWebCredential(out Uri? tokenEndpoint, out string clientId, out string clientSecret)
    {
        clientId = WebClientId ?? string.Empty;
        clientSecret = WebClientSecret ?? string.Empty;
        bool valid = Uri.TryCreate(TokenEndpoint, UriKind.Absolute, out tokenEndpoint)
            && tokenEndpoint?.Scheme == Uri.UriSchemeHttps
            && !string.IsNullOrWhiteSpace(clientId)
            && !string.IsNullOrWhiteSpace(clientSecret);
        return valid;
    }
}

sealed class EnrollmentStore
{
    private readonly Dictionary<string, Enrollment> _entries = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    public string Issue(string serverId)
    {
        byte[] bytes = RandomNumberGenerator.GetBytes(32);
        string token = Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        lock (_gate)
        {
            RemoveExpired();
            _entries[token] = new Enrollment(serverId, DateTimeOffset.UtcNow.AddMinutes(10));
        }
        return token;
    }

    public bool Exists(string token)
    {
        lock (_gate)
        {
            RemoveExpired();
            return _entries.ContainsKey(token);
        }
    }

    public string? Take(string token)
    {
        lock (_gate)
        {
            RemoveExpired();
            if (!_entries.Remove(token, out Enrollment? entry)) return null;
            return entry.ServerId;
        }
    }

    private void RemoveExpired()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        foreach (string token in _entries.Where(pair => pair.Value.ExpiresAt <= now).Select(pair => pair.Key).ToArray()) _entries.Remove(token);
    }

    private sealed record Enrollment(string ServerId, DateTimeOffset ExpiresAt);
}

static class ClientConfig
{
    public static bool TryValidate(string json, out string? error)
    {
        if (string.IsNullOrWhiteSpace(json)) { error = "Configuration must not be empty."; return false; }
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object) { error = "Configuration root must be a JSON object."; return false; }
            error = null;
            return true;
        }
        catch (JsonException exception) { error = "Invalid JSON: " + exception.Message; return false; }
    }

    public static string RemoveSecrets(string json)
    {
        JsonNode? root = JsonNode.Parse(json);
        RemoveSecrets(root);
        return root?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) ?? "{}";
    }

    private static void RemoveSecrets(JsonNode? node)
    {
        if (node is JsonObject objectNode)
        {
            foreach (string key in objectNode.Select(pair => pair.Key).ToArray())
            {
                if (string.Equals(key, "clientSecret", StringComparison.OrdinalIgnoreCase)) objectNode.Remove(key);
                else RemoveSecrets(objectNode[key]);
            }
        }
        else if (node is JsonArray array)
            foreach (JsonNode? child in array) RemoveSecrets(child);
    }
}

sealed class TokenValidator
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };
    public async Task<ValidatedIdentity?> ValidateAsync(string token, IEnumerable<ProviderOptions> providers, CancellationToken ct)
    {
        try { return await ValidateCoreAsync(token, providers, ct); }
        catch { return null; }
    }

    private async Task<ValidatedIdentity?> ValidateCoreAsync(string token, IEnumerable<ProviderOptions> providers, CancellationToken ct)
    {
        string[] parts = token.Split('.'); if (parts.Length != 3) return null;
        using JsonDocument header = JsonDocument.Parse(Decode(parts[0]));
        using JsonDocument payload = JsonDocument.Parse(Decode(parts[1]));
        if (!header.RootElement.TryGetProperty("alg", out var alg) || alg.GetString() != "RS256") return null;
        string? issuer = payload.RootElement.TryGetProperty("iss", out var iss) ? iss.GetString() : null;
        ProviderOptions? provider = providers.FirstOrDefault(p => p.Issuer == issuer);
        if (provider == null) return null;
        string[] audiences = new[] { provider.Audience, provider.WebClientId }
            .Where(value => !string.IsNullOrWhiteSpace(value)).Cast<string>().ToArray();
        string jwksUriText = provider.JwksUri ?? string.Empty;
        if (audiences.Length == 0 || !Uri.TryCreate(jwksUriText, UriKind.Absolute, out Uri? jwksUri)
            || jwksUri == null || jwksUri.Scheme != Uri.UriSchemeHttps || !audiences.Any(audience => Audience(payload.RootElement, audience)) || Expired(payload.RootElement)) return null;
        string? subject = payload.RootElement.TryGetProperty("sub", out var sub) ? sub.GetString() : null;
        if (string.IsNullOrWhiteSpace(subject) || !Policy(payload.RootElement, provider)) return null;
        string? kid = header.RootElement.TryGetProperty("kid", out var kidValue) ? kidValue.GetString() : null;
        using JsonDocument jwks = JsonDocument.Parse(await _http.GetStringAsync(jwksUri, ct));
        JsonElement key = default;
        bool found = false;
        foreach (JsonElement candidate in jwks.RootElement.GetProperty("keys").EnumerateArray())
        {
            if (candidate.GetProperty("kty").GetString() == "RSA" && candidate.GetProperty("kid").GetString() == kid)
            { key = candidate; found = true; break; }
        }
        if (!found) return null;
        using RSA rsa = RSA.Create();
        rsa.ImportParameters(new RSAParameters { Modulus = Decode(key.GetProperty("n").GetString()!), Exponent = Decode(key.GetProperty("e").GetString()!) });
        if (!rsa.VerifyData(Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]), Decode(parts[2]), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)) return null;
        return new ValidatedIdentity(issuer!, subject);
    }
    private static bool Audience(JsonElement p, string expected) => p.TryGetProperty("aud", out var a) && (a.ValueKind == JsonValueKind.String ? a.GetString() == expected : a.ValueKind == JsonValueKind.Array && a.EnumerateArray().Any(x => x.GetString() == expected));
    private static bool Expired(JsonElement p) => !p.TryGetProperty("exp", out var e) || e.GetInt64() <= DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    private static bool Policy(JsonElement p, ProviderOptions o)
    {
        string? hostedDomain = p.TryGetProperty("hd", out JsonElement hd) && hd.ValueKind == JsonValueKind.String
            ? hd.GetString()
            : null;
        return HostedDomainPolicy.Matches(hostedDomain, o.AllowedHostedDomains) && Any(p, "groups", o.AllowedGroups);
    }
    private static bool Any(JsonElement p, string claim, List<string>? allowed) { if (allowed is not { Count: > 0 }) return true; if (!p.TryGetProperty(claim, out var v)) return false; return v.ValueKind == JsonValueKind.Array ? v.EnumerateArray().Any(x => allowed.Contains(x.GetString()!)) : allowed.Contains(v.GetString()!); }
    private static byte[] Decode(string value) { string s = value.Replace('-', '+').Replace('_', '/'); if (s.Length % 4 == 2) s += "=="; else if (s.Length % 4 == 3) s += "="; return Convert.FromBase64String(s); }
}

static class Ticket
{
    public static string Create(string key, string issuer, string subject, string did)
    {
        long expiry = DateTimeOffset.UtcNow.AddMinutes(1).ToUnixTimeSeconds();
        string ticketId = Guid.NewGuid().ToString("N");
        byte[] body = Encoding.UTF8.GetBytes($"basis-sso-ticket-v2\n{expiry}\n{ticketId}\n{issuer}\n{subject}\n{did}");
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key));
        return Encode(body) + "." + Encode(hmac.ComputeHash(body));
    }
    private static string Encode(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
