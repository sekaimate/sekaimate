using Basis.Network.Core;

namespace Basis.BasisUI
{
    public static class SavedServerWebSocketUriValidator
    {
        public static string Validate(string webSocketUri, bool webGlPlayer)
        {
            string trimmedUri = webSocketUri?.Trim() ?? string.Empty;
            if (!webGlPlayer && trimmedUri.Length == 0)
            {
                return string.Empty;
            }

            ClientConnectionTargetSelector.Select(null, trimmedUri, true);
            return trimmedUri;
        }
    }
}
