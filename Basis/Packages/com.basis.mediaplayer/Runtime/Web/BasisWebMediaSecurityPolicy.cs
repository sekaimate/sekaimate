public static class BasisWebMediaSecurityPolicy
{
    public static bool TryValidate(
        string mediaUrl,
        string audioUrl,
        bool pageUsesHttps,
        bool hasCustomHeaders,
        out string reason)
    {
        if (!BasisMediaPlayerSecurity.IsUrlAllowed(mediaUrl, out reason)) return false;
        return BasisWebMediaPolicy.TryValidate(
            mediaUrl,
            audioUrl,
            pageUsesHttps,
            hasCustomHeaders,
            out reason);
    }
}
