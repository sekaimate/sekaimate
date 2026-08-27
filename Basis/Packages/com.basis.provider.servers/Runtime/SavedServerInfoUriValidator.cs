using Basis.Network.Core;
using System;

namespace Basis.BasisUI
{
    public static class SavedServerInfoUriValidator
    {
        public static string Validate(string serverInfoUri, bool webGlPlayer)
        {
            string trimmedUri = serverInfoUri?.Trim() ?? string.Empty;
            if (!webGlPlayer && trimmedUri.Length == 0)
            {
                return string.Empty;
            }
            if (trimmedUri.Length == 0)
            {
                throw new InvalidOperationException("The server directory entry does not provide a server-info URI.");
            }

            ServerInfoHttpUri.Parse(trimmedUri);
            return trimmedUri;
        }
    }
}
