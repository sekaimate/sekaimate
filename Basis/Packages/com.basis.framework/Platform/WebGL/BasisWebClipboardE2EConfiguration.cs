using System;

internal static class BasisWebClipboardE2EConfiguration
{
    private const string EnabledParameter = "basisClipboardE2E";

    public static bool IsEnabled(string absoluteUrl)
    {
        if (!Uri.TryCreate(absoluteUrl, UriKind.Absolute, out Uri uri))
        {
            return false;
        }

        foreach (string parameter in uri.Query.TrimStart('?').Split('&'))
        {
            int separator = parameter.IndexOf('=');
            if (separator < 0)
            {
                continue;
            }

            string key = Uri.UnescapeDataString(parameter.Substring(0, separator));
            string value = Uri.UnescapeDataString(parameter.Substring(separator + 1));
            if (string.Equals(key, EnabledParameter, StringComparison.Ordinal))
            {
                return value == "1";
            }
        }

        return false;
    }
}
