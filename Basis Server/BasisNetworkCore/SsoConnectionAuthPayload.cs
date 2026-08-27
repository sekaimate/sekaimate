using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Basis.Contrib.Crypto;

namespace Basis.Network.Core
{
    /// <summary>Connection-auth envelope carrying the existing join password and an opaque SSO admission ticket.</summary>
    public static class SsoConnectionAuthPayload
    {
        private static readonly byte[] LegacyMagic = { (byte)'B', (byte)'S', (byte)'S', (byte)'O', 1 };
        private static readonly byte[] EncryptedMagic = { (byte)'B', (byte)'S', (byte)'S', (byte)'O', 2 };
        private const int MaxPayloadBytes = 8192;

        public static byte[] Encode(string password, string ticket)
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
            writer.Write(LegacyMagic);
            writer.Write(password ?? string.Empty);
            writer.Write(ticket ?? string.Empty);
            writer.Flush();
            return stream.ToArray();
        }

        public static bool TryDecode(byte[] payload, out string password, out string ticket)
        {
            password = ticket = null;
            if (payload == null || payload.Length < LegacyMagic.Length || payload.Length > MaxPayloadBytes) return false;
            for (int i = 0; i < LegacyMagic.Length; i++) if (payload[i] != LegacyMagic[i]) return false;
            try
            {
                using var stream = new MemoryStream(payload, false);
                using var reader = new BinaryReader(stream, Encoding.UTF8, false);
                reader.ReadBytes(LegacyMagic.Length);
                password = reader.ReadString();
                ticket = reader.ReadString();
                return stream.Position == stream.Length;
            }
            catch { password = ticket = null; return false; }
        }

        /// <summary>Encrypts the password and opaque ticket to a server's pinned X25519 key.</summary>
        public static byte[] EncodeEncrypted(string password, string ticket, string serverPublicKey)
        {
            if (!TryDecodeKey(serverPublicKey, out byte[] serverPublic) || serverPublic.Length != BasisCryptoHandshake.PublicKeySize)
                return null;
            byte[] plaintext = Encode(password, ticket);
            // Omit the legacy envelope marker inside the encrypted payload.
            byte[] cipherText = new byte[plaintext.Length - LegacyMagic.Length];
            Buffer.BlockCopy(plaintext, LegacyMagic.Length, cipherText, 0, cipherText.Length);
            BasisCryptoHandshake.GenerateKeyPair(out byte[] clientPrivate, out byte[] clientPublic);
            if (!BasisCryptoHandshake.DeriveSsoClientKeys(clientPrivate, clientPublic, serverPublic, out byte[] sendKey, out _)) return null;
            byte[] nonce = new byte[BasisAeadCipher.NonceSize];
            using (var random = RandomNumberGenerator.Create()) random.GetBytes(nonce);
            byte[] tag = new byte[BasisAeadCipher.TagSize];
            using (var cipher = new BasisAeadCipher(sendKey)) cipher.Seal(nonce, EncryptedMagic[4], cipherText, 0, cipherText.Length, tag, 0);
            byte[] result = new byte[EncryptedMagic.Length + clientPublic.Length + nonce.Length + tag.Length + cipherText.Length];
            int offset = 0;
            Buffer.BlockCopy(EncryptedMagic, 0, result, offset, EncryptedMagic.Length); offset += EncryptedMagic.Length;
            Buffer.BlockCopy(clientPublic, 0, result, offset, clientPublic.Length); offset += clientPublic.Length;
            Buffer.BlockCopy(nonce, 0, result, offset, nonce.Length); offset += nonce.Length;
            Buffer.BlockCopy(tag, 0, result, offset, tag.Length); offset += tag.Length;
            Buffer.BlockCopy(cipherText, 0, result, offset, cipherText.Length);
            Array.Clear(clientPrivate, 0, clientPrivate.Length);
            Array.Clear(sendKey, 0, sendKey.Length);
            return result;
        }

        /// <summary>Decrypts a v2 connection envelope. Legacy envelopes are deliberately rejected.</summary>
        public static bool TryDecodeEncrypted(byte[] payload, Configuration configuration, out string password, out string ticket)
        {
            password = ticket = null;
            int headerSize = EncryptedMagic.Length + BasisCryptoHandshake.PublicKeySize + BasisAeadCipher.NonceSize + BasisAeadCipher.TagSize;
            if (payload == null || payload.Length <= headerSize || payload.Length > MaxPayloadBytes || configuration == null) return false;
            for (int i = 0; i < EncryptedMagic.Length; i++) if (payload[i] != EncryptedMagic[i]) return false;
            if (!BasisSsoTransportKeys.TryGetPrivate(configuration, out byte[] serverPrivate)
                || !BasisSsoTransportKeys.TryGetPublic(configuration, out byte[] serverPublic)) return false;
            int offset = EncryptedMagic.Length;
            byte[] clientPublic = new byte[BasisCryptoHandshake.PublicKeySize];
            Buffer.BlockCopy(payload, offset, clientPublic, 0, clientPublic.Length); offset += clientPublic.Length;
            byte[] nonce = new byte[BasisAeadCipher.NonceSize];
            Buffer.BlockCopy(payload, offset, nonce, 0, nonce.Length); offset += nonce.Length;
            byte[] tag = new byte[BasisAeadCipher.TagSize];
            Buffer.BlockCopy(payload, offset, tag, 0, tag.Length); offset += tag.Length;
            byte[] clear = new byte[payload.Length - offset];
            Buffer.BlockCopy(payload, offset, clear, 0, clear.Length);
            // The server receives the client's first message, so it must use the
            // second (receive) key returned by the directional SSO derivation.
            if (!BasisCryptoHandshake.DeriveSsoServerKeys(serverPrivate, clientPublic, serverPublic, out _, out byte[] recvKey)) return false;
            bool opened;
            using (var cipher = new BasisAeadCipher(recvKey)) opened = cipher.Open(nonce, EncryptedMagic[4], clear, 0, clear.Length, tag, 0);
            Array.Clear(serverPrivate, 0, serverPrivate.Length);
            Array.Clear(recvKey, 0, recvKey.Length);
            if (!opened) return false;
            try
            {
                using var stream = new MemoryStream(clear, false);
                using var reader = new BinaryReader(stream, Encoding.UTF8, false);
                password = reader.ReadString();
                ticket = reader.ReadString();
                return stream.Position == stream.Length && !string.IsNullOrWhiteSpace(ticket);
            }
            catch { password = ticket = null; return false; }
        }

        private static bool TryDecodeKey(string value, out byte[] key)
        {
            key = null;
            if (string.IsNullOrWhiteSpace(value)) return false;
            try
            {
                string padded = value.Replace('-', '+').Replace('_', '/');
                if (padded.Length % 4 == 2) padded += "==";
                else if (padded.Length % 4 == 3) padded += "=";
                else if (padded.Length % 4 == 1) return false;
                key = Convert.FromBase64String(padded);
                return true;
            }
            catch { return false; }
        }
    }
}
