package admission

import (
	"strconv"
	"strings"
	"testing"
	"time"
)

// TestCreateTicketFields_Golden pins the exact wire bytes for a fixed set
// of inputs. The expected value was computed independently (Python
// hmac/hashlib/base64, not this package) from the format documented in
// research-sso-broker.md §7-1:
//
//	body   = "basis-sso-ticket-v2\n{expiry}\n{ticketID}\n{issuer}\n{subject}\n{did}"
//	ticket = base64url_nopad(body) + "." + base64url_nopad(HMAC_SHA256(signingKey, body))
//
// Any change to field order, the magic string, the separator, or the
// base64 alphabet/padding must fail this test.
func TestCreateTicketFields_Golden(t *testing.T) {
	const (
		signingKey = "0123456789abcdef0123456789abcdefXYZ"
		issuer     = "https://issuer.example"
		subject    = "user-123"
		did        = "did:key:z6MkExample"
		expiry     = int64(1700000000)
		ticketID   = "0123456789abcdef0123456789abcdef"
	)
	const want = "YmFzaXMtc3NvLXRpY2tldC12MgoxNzAwMDAwMDAwCjAxMjM0NTY3ODlhYmNkZWYwMTIzNDU2Nzg5YWJjZGVmCmh0dHBzOi8vaXNzdWVyLmV4YW1wbGUKdXNlci0xMjMKZGlkOmtleTp6Nk1rRXhhbXBsZQ" +
		"." +
		"-y4x4BpBayiZeoZ38Rtkv6e6t_sXnRny5h6_FsroNZ4"

	got := createTicketFields(signingKey, expiry, ticketID, issuer, subject, did)
	if got != want {
		t.Fatalf("createTicketFields() =\n  %s\nwant:\n  %s", got, want)
	}
}

// TestCreateTicketFields_FieldOrderAndSeparator asserts the body decodes
// back to exactly 6 bare-\n-separated fields with the ticket-v2 magic
// string first, matching what SsoAdmissionTicket.TryValidate on the UDP
// server requires (research-sso-broker.md §2.4).
func TestCreateTicketFields_FieldOrderAndSeparator(t *testing.T) {
	ticket := createTicketFields("0123456789012345678901234567890123", 1700000000, "abc", "iss", "sub", "did")
	dot := strings.IndexByte(ticket, '.')
	if dot < 0 {
		t.Fatalf("ticket has no '.' separator: %q", ticket)
	}
	bodyB64 := ticket[:dot]
	body, err := decodeSegment(bodyB64)
	if err != nil {
		t.Fatalf("decode body: %v", err)
	}
	fields := strings.Split(string(body), "\n")
	if len(fields) != 6 {
		t.Fatalf("len(fields) = %d, want 6: %q", len(fields), fields)
	}
	if fields[0] != ticketMagic {
		t.Errorf("fields[0] = %q, want %q", fields[0], ticketMagic)
	}
	if fields[1] != "1700000000" || fields[2] != "abc" || fields[3] != "iss" || fields[4] != "sub" || fields[5] != "did" {
		t.Errorf("unexpected fields: %v", fields)
	}
	if strings.Contains(string(body), "\r") {
		t.Errorf("body contains \\r, must use bare \\n only")
	}
}

// TestCreateTicket_TicketIDIsNFormatGUID checks that CreateTicket's
// generated ticket id is 32 lowercase hex characters (the "N" format
// Guid.NewGuid().ToString("N") produces, which the UDP server's
// Guid.TryParseExact(id, "N", ...) requires).
func TestCreateTicket_TicketIDIsNFormatGUID(t *testing.T) {
	ticket, err := CreateTicket("0123456789012345678901234567890123456789", "iss", "sub", "did:key:abc")
	if err != nil {
		t.Fatalf("CreateTicket: %v", err)
	}
	dot := strings.IndexByte(ticket, '.')
	body, err := decodeSegment(ticket[:dot])
	if err != nil {
		t.Fatalf("decode body: %v", err)
	}
	fields := strings.Split(string(body), "\n")
	id := fields[2]
	if len(id) != 32 {
		t.Fatalf("ticket id length = %d, want 32: %q", len(id), id)
	}
	for _, c := range id {
		if !((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f')) {
			t.Fatalf("ticket id contains non-lowercase-hex character: %q", id)
		}
	}
}

// TestCreateTicket_ExpiryIsSixtySeconds checks the minted expiry is exactly
// now+60s (within test-execution slack), matching Ticket.Create's hardcoded
// 1-minute lifetime.
func TestCreateTicket_ExpiryIsSixtySeconds(t *testing.T) {
	before := time.Now().UTC().Add(ticketLifetime).Unix()
	ticket, err := CreateTicket("0123456789012345678901234567890123456789", "iss", "sub", "did:key:abc")
	if err != nil {
		t.Fatalf("CreateTicket: %v", err)
	}
	after := time.Now().UTC().Add(ticketLifetime).Unix()

	dot := strings.IndexByte(ticket, '.')
	body, err := decodeSegment(ticket[:dot])
	if err != nil {
		t.Fatalf("decode body: %v", err)
	}
	fields := strings.Split(string(body), "\n")
	expiry, err := strconv.ParseInt(fields[1], 10, 64)
	if err != nil {
		t.Fatalf("parse expiry: %v", err)
	}
	if expiry < before || expiry > after {
		t.Fatalf("expiry = %d, want in [%d, %d]", expiry, before, after)
	}
}
