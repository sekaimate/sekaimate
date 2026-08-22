// Package admission implements the OIDC ID-token validation and
// basis-sso-ticket-v2 minting that Program.cs's TokenValidator and Ticket
// classes perform in the C# broker. It is stateless: a minted ticket is
// signed and immediately forgotten, exactly like the original — replay
// protection and DID binding are enforced entirely by the UDP game server
// (SsoAdmissionTicket.TryValidate), not here (research-sso-broker.md §2.4).
package admission

import (
	"crypto/hmac"
	"crypto/rand"
	"crypto/sha256"
	"encoding/base64"
	"encoding/hex"
	"fmt"
	"time"
)

// ticketMagic is the fixed magic string every ticket body starts with.
// This exact string, the field order, and the "\n" separator are
// load-bearing: the unmodified C# UDP server's SsoAdmissionTicket.TryValidate
// re-derives the same HMAC over the identical body
// (research-sso-broker.md §2.3/§7-1).
const ticketMagic = "basis-sso-ticket-v2"

// ticketLifetime is the fixed 60-second ticket lifetime Ticket.Create uses
// (not configurable at this call site in the C# broker either).
const ticketLifetime = 60 * time.Second

// CreateTicket mints a basis-sso-ticket-v2 for (issuer, subject, did),
// signed with signingKey, expiring 60 seconds from now. It matches
// Ticket.Create byte-for-byte: see createTicketFields for the exact wire
// format.
func CreateTicket(signingKey, issuer, subject, did string) (string, error) {
	id, err := newTicketID()
	if err != nil {
		return "", fmt.Errorf("admission: generate ticket id: %w", err)
	}
	expiry := time.Now().UTC().Add(ticketLifetime).Unix()
	return createTicketFields(signingKey, expiry, id, issuer, subject, did), nil
}

// createTicketFields builds the ticket string for explicit field values.
// Kept separate from CreateTicket so golden tests can pin every input
// (signing key, expiry, ticket id) and assert an exact expected output —
// research-sso-broker.md §7-1 requires this byte-for-byte, not just
// structurally.
//
// Wire format:
//
//	body   = UTF8("basis-sso-ticket-v2\n{expiry}\n{ticketID}\n{issuer}\n{subject}\n{did}")
//	mac    = HMAC-SHA256(UTF8(signingKey), body)
//	ticket = base64url_nopad(body) + "." + base64url_nopad(mac)
func createTicketFields(signingKey string, expiry int64, ticketID, issuer, subject, did string) string {
	body := []byte(fmt.Sprintf("%s\n%d\n%s\n%s\n%s\n%s", ticketMagic, expiry, ticketID, issuer, subject, did))
	mac := hmac.New(sha256.New, []byte(signingKey))
	mac.Write(body)
	return base64URLNoPad(body) + "." + base64URLNoPad(mac.Sum(nil))
}

// newTicketID generates a lowercase-hex, no-dash 32-character id — the "N"
// format Guid.NewGuid().ToString("N") produces. A proper RFC 4122 v4 UUID
// is generated (version/variant bits set) even though the UDP server's
// TryValidate only checks the string shape (32 hex chars), not the UUID
// version, for parity with .NET's Guid.NewGuid().
func newTicketID() (string, error) {
	var b [16]byte
	if _, err := rand.Read(b[:]); err != nil {
		return "", err
	}
	b[6] = (b[6] & 0x0f) | 0x40 // version 4
	b[8] = (b[8] & 0x3f) | 0x80 // variant RFC 4122
	return hex.EncodeToString(b[:]), nil
}

// base64URLNoPad is standard base64url (RFC 4648 §5) with padding
// stripped — identical to the C# broker's
// Convert.ToBase64String(b).TrimEnd('=').Replace('+','-').Replace('/','_'),
// since base64url is defined as exactly that character substitution over
// standard base64.
func base64URLNoPad(b []byte) string {
	return base64.RawURLEncoding.EncodeToString(b)
}
