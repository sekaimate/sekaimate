public sealed class WebOidcTokenExchange
{
    private readonly HttpClient _httpClient;

    public WebOidcTokenExchange(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public Task<HttpResponseMessage> SendAsync(
        Uri tokenEndpoint,
        string clientId,
        string clientSecret,
        IReadOnlyDictionary<string, string> request,
        CancellationToken cancellationToken)
    {
        var form = new Dictionary<string, string>(request, StringComparer.Ordinal)
        {
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
        };
        return _httpClient.PostAsync(tokenEndpoint, new FormUrlEncodedContent(form), cancellationToken);
    }
}
