using Basis.Network.Core;
using System.Collections.Concurrent;

namespace BasisNetworkServer.Security
{
    /// <summary>Holds a ticket only between connection acceptance and the first DID challenge.</summary>
    public static class BasisSsoAdmissionGate
    {
        private static readonly ConcurrentDictionary<int, string> PendingTickets = new ConcurrentDictionary<int, string>();
        private static readonly ConcurrentDictionary<string, long> ConsumedTicketIds = new ConcurrentDictionary<string, long>();

        public static void SetPendingTicket(int peerId, string ticket)
        {
            if (!string.IsNullOrWhiteSpace(ticket)) PendingTickets[peerId] = ticket;
        }

        public static bool ConsumeForDid(int peerId, string did, Configuration config)
        {
            if (!config.RequireSso) return true;
            if (!PendingTickets.TryRemove(peerId, out string ticket)) return false;
            if (!SsoAdmissionTicket.TryValidate(ticket, config.SsoAdmissionTicketSigningKey, did, out string issuer, out _, out string ticketId, out long expiry))
                return false;

            // The broker is the primary OIDC verifier. A configured server issuer list is an
            // independent defence-in-depth boundary for a shared broker signing key.
            if (config.SsoProviders != null && config.SsoProviders.Count > 0)
            {
                bool issuerAllowed = false;
                foreach (SsoProviderConfiguration provider in config.SsoProviders)
                {
                    if (provider != null && string.Equals(provider.Issuer?.Trim(), issuer, System.StringComparison.Ordinal))
                    {
                        issuerAllowed = true;
                        break;
                    }
                }
                if (!issuerAllowed) return false;
            }

            foreach (var item in ConsumedTicketIds)
                if (item.Value <= System.DateTimeOffset.UtcNow.ToUnixTimeSeconds()) ConsumedTicketIds.TryRemove(item.Key, out _);
            return ConsumedTicketIds.TryAdd(ticketId, expiry);
        }

        public static void Remove(int peerId) => PendingTickets.TryRemove(peerId, out _);
    }
}
