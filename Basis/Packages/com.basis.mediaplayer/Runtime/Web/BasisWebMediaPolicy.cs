using System;

public static class BasisWebMediaPolicy
{
    public static bool TryValidate(
        string mediaUrl,
        string audioUrl,
        bool pageUsesHttps,
        bool hasCustomHeaders,
        out string reason)
    {
        reason = null;
        if (!Uri.TryCreate(mediaUrl, UriKind.Absolute, out Uri uri))
        {
            reason = "Web media URL must be absolute.";
            return false;
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            reason = "Web media supports only HTTP and HTTPS URLs.";
            return false;
        }

        if (pageUsesHttps && uri.Scheme != Uri.UriSchemeHttps)
        {
            reason = "An HTTPS page cannot load HTTP media.";
            return false;
        }

        if (!string.IsNullOrEmpty(audioUrl))
        {
            reason = "Separate audio URLs are not supported by the Web media backend.";
            return false;
        }

        if (hasCustomHeaders)
        {
            reason = "Custom HTTP headers are not supported by the Web media backend.";
            return false;
        }

        return true;
    }
}
