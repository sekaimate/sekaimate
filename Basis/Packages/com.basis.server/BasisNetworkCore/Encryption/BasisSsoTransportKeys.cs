#nullable enable

using System;
using System.Security.Cryptography;

namespace Basis.Network.Core
{
    /// <summary>Owns the server's long-lived X25519 transport keypair used for client pinning.</summary>
    public static class BasisSsoTransportKeys
    {
        public static bool Ensure(Configuration configuration, out bool generated)
        {
            generated = false;
            if (configuration == null) return false;
            bool hasTransportPair = TryDecode(configuration.SsoTransportPrivateKey, out byte[] privateKey)
                && TryDecode(configuration.SsoTransportPublicKey, out byte[] publicKey)
                && privateKey.Length == BasisCryptoHandshake.PrivateKeySize
                && publicKey.Length == BasisCryptoHandshake.PublicKeySize;
            if (!hasTransportPair)
            {
                BasisCryptoHandshake.GenerateKeyPair(out privateKey, out publicKey);
                configuration.SsoTransportPrivateKey = Encode(privateKey);
                configuration.SsoTransportPublicKey = Encode(publicKey);
                generated = true;
            }
            if (string.IsNullOrWhiteSpace(configuration.SsoAdmissionTicketSigningKey))
            {
                byte[] ticketKey = new byte[48];
                using var rng = RandomNumberGenerator.Create();
                rng.GetBytes(ticketKey);
                configuration.SsoAdmissionTicketSigningKey = Encode(ticketKey);
                generated = true;
            }
            return true;
        }

        public static bool TryGetPrivate(Configuration configuration, out byte[] key) =>
            TryDecode(configuration?.SsoTransportPrivateKey, out key) && key.Length == BasisCryptoHandshake.PrivateKeySize;

        public static bool TryGetPublic(Configuration configuration, out byte[] key) =>
            TryDecode(configuration?.SsoTransportPublicKey, out key) && key.Length == BasisCryptoHandshake.PublicKeySize;

        private static string Encode(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        private static bool TryDecode(string? value, out byte[] bytes)
        {
            bytes = Array.Empty<byte>();
            if (string.IsNullOrWhiteSpace(value)) return false;
            try
            {
                string s = value.Replace('-', '+').Replace('_', '/');
                if (s.Length % 4 == 2) s += "=="; else if (s.Length % 4 == 3) s += "="; else if (s.Length % 4 == 1) return false;
                bytes = Convert.FromBase64String(s);
                return true;
            }
            catch { return false; }
        }
    }
}
