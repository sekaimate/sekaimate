public static class HostedDomainPolicy
{
    public static bool Matches(string? hostedDomain, IReadOnlyList<string>? allowedDomains)
    {
        if (allowedDomains is not { Count: > 0 }) return true;
        if (string.IsNullOrWhiteSpace(hostedDomain)) return false;

        foreach (string allowedDomain in allowedDomains)
        {
            string candidate = allowedDomain.Trim();
            if (candidate == "*" || string.Equals(candidate, hostedDomain, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    public static Dictionary<string, string> AuthorizationParameters(IReadOnlyList<string>? allowedDomains)
    {
        var parameters = new Dictionary<string, string>
        {
            ["access_type"] = "offline",
            ["prompt"] = "consent",
        };

        if (allowedDomains is not { Count: > 0 }) return parameters;
        string[] domains = allowedDomains
            .Select(domain => domain.Trim())
            .Where(domain => domain.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (domains.Length > 0) parameters["hd"] = domains.Length == 1 ? domains[0] : "*";
        return parameters;
    }
}
