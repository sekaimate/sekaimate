using System;
using System.Security.Cryptography;
using System.Text;

namespace Basis.Network.Core
{
    /// <summary>
    /// Opaque, short-lived admission proof issued by the HTTPS OIDC broker. It is safe to carry
    /// over the game transport because it is bound to the DID that must subsequently answer the
    /// server's fresh DID challenge; it is never an OAuth bearer token.
    /// </summary>
    public static class SsoAdmissionTicket
    {
        public static string Create(string signingKey, string issuer, string subject, string did, TimeSpan lifetime)
        {
            if (string.IsNullOrWhiteSpace(signingKey) || string.IsNullOrWhiteSpace(issuer)
                || string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(did))
                throw new ArgumentException("Signing key, issuer, subject and DID are required.");
            if (issuer.Contains('\n') || subject.Contains('\n') || did.Contains('\n')
                || issuer.Contains('\r') || subject.Contains('\r') || did.Contains('\r'))
                throw new ArgumentException("Ticket fields cannot contain line breaks.");
            long expiry = DateTimeOffset.UtcNow.Add(lifetime > TimeSpan.Zero && lifetime <= TimeSpan.FromMinutes(1)
                ? lifetime : TimeSpan.FromMinutes(1)).ToUnixTimeSeconds();
            string ticketId = Guid.NewGuid().ToString("N");
            byte[] body = Encoding.UTF8.GetBytes(string.Join("\n", "basis-sso-ticket-v2", expiry, ticketId, issuer, subject, did));
            using var mac = new HMACSHA256(Encoding.UTF8.GetBytes(signingKey));
            return Encode(body) + "." + Encode(mac.ComputeHash(body));
        }
        public static bool TryValidate(string ticket, string signingKey, string expectedDid, out string issuer, out string subject,
            out string ticketId, out long expiry)
        {
            issuer = subject = ticketId = null;
            expiry = 0;
            if (string.IsNullOrWhiteSpace(ticket) || string.IsNullOrWhiteSpace(signingKey)) return false;
            string[] parts = ticket.Split('.');
            if (parts.Length != 2) return false;
            byte[] body;
            try { body = Decode(parts[0]); }
            catch { return false; }
            byte[] expected;
            using (var mac = new HMACSHA256(Encoding.UTF8.GetBytes(signingKey))) expected = mac.ComputeHash(body);
            byte[] actual;
            try { actual = Decode(parts[1]); }
            catch { return false; }
            if (!FixedTimeEquals(expected, actual)) return false;
            string[] fields = Encoding.UTF8.GetString(body).Split('\n');
            if (fields.Length != 6 || !string.Equals(fields[0], "basis-sso-ticket-v2", StringComparison.Ordinal)) return false;
            if (!long.TryParse(fields[1], out expiry) || expiry <= DateTimeOffset.UtcNow.ToUnixTimeSeconds()) return false;
            ticketId = fields[2];
            if (ticketId.Length != 32 || !Guid.TryParseExact(ticketId, "N", out _)) return false;
            if (!string.Equals(fields[5], expectedDid, StringComparison.Ordinal)) return false;
            issuer = fields[3]; subject = fields[4];
            return !string.IsNullOrEmpty(issuer) && !string.IsNullOrEmpty(subject);
        }

        private static byte[] Decode(string value)
        {
            string s = value.Replace('-', '+').Replace('_', '/');
            if (s.Length % 4 == 2) s += "=="; else if (s.Length % 4 == 3) s += "="; else if (s.Length % 4 == 1) throw new FormatException();
            return Convert.FromBase64String(s);
        }

        private static string Encode(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        private static bool FixedTimeEquals(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            int diff = 0; for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i]; return diff == 0;
        }
    }
}
