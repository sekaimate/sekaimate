using System.Net;
using System.Text;
using Xunit;

public sealed class WebOidcTokenExchangeTests
{
    [Fact]
    public async Task ExchangeCodeAddsServerCredentialAndPreservesPkceFields()
    {
        var handler = new RecordingHandler();
        var exchange = new WebOidcTokenExchange(new HttpClient(handler));
        var request = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = "authorization-code",
            ["redirect_uri"] = "https://web.example/sso-callback",
            ["code_verifier"] = "verifier",
        };

        using HttpResponseMessage response = await exchange.SendAsync(
            new Uri("https://oauth2.googleapis.com/token"),
            "web-client-id",
            "server-secret",
            request,
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("authorization_code", handler.Form["grant_type"]);
        Assert.Equal("authorization-code", handler.Form["code"]);
        Assert.Equal("https://web.example/sso-callback", handler.Form["redirect_uri"]);
        Assert.Equal("verifier", handler.Form["code_verifier"]);
        Assert.Equal("web-client-id", handler.Form["client_id"]);
        Assert.Equal("server-secret", handler.Form["client_secret"]);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public Dictionary<string, string> Form { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string body = await request.Content!.ReadAsStringAsync(cancellationToken);
            foreach (string pair in body.Split('&'))
            {
                string[] parts = pair.Split('=', 2);
                Form[Uri.UnescapeDataString(parts[0])] = Uri.UnescapeDataString(parts[1].Replace('+', ' '));
            }
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"access_token\":\"token\"}", Encoding.UTF8, "application/json"),
            };
        }
    }
}
