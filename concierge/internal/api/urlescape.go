package api

import (
	"net/url"
	"strings"
)

// escapeDataString mirrors .NET's Uri.EscapeDataString (percent-encodes
// everything outside the RFC 3986 unreserved set, space -> %20). Go's
// url.QueryEscape uses the same unreserved set but encodes space as '+'
// (form-encoding convention); swapping that back to %20 makes the output
// match byte-for-byte for the query values concierge generates
// (password/meeting/url/config/link — research-sso-broker.md §7-6/§7-7).
func escapeDataString(s string) string {
	return strings.ReplaceAll(url.QueryEscape(s), "+", "%20")
}
