package controlplane

import (
	"crypto/rand"
	"encoding/base64"
	"net"
	"regexp"
	"strings"
)

// idPattern mirrors MeetingIdentity.IsValid's ^[A-Za-z0-9_-]{1,48}$.
var idPattern = regexp.MustCompile(`^[A-Za-z0-9_-]{1,48}$`)

// IsValidID reports whether id matches MeetingIdentity.IsValid.
func IsValidID(id string) bool {
	return id != "" && idPattern.MatchString(id)
}

// RandomToken returns n random bytes, base64url (no padding) encoded —
// matching MeetingIdentity.RandomToken.
func RandomToken(n int) string {
	b := make([]byte, n)
	if _, err := rand.Read(b); err != nil {
		panic(err) // crypto/rand failing indicates a broken host; matches the
		// C# RandomNumberGenerator.GetBytes contract of never returning a
		// partial/failed result silently.
	}
	return base64.RawURLEncoding.EncodeToString(b)
}

// RandomPassword mirrors MeetingIdentity.RandomPassword (RandomToken(18)).
func RandomPassword() string {
	return RandomToken(18)
}

// KubernetesName normalizes id into a DNS-label-compatible form, matching
// MeetingIdentity.KubernetesName. Unused until phase 2's kube integration,
// kept for parity with the C# control plane's forward-looking helper.
func KubernetesName(id string) string {
	return strings.Trim(strings.ReplaceAll(normalize(id), "_", "-"), "-")
}

// normalize lowercases value, keeps only alphanumerics, and collapses runs
// of '-', '_', or whitespace into a single '-', matching
// MeetingIdentity.Normalize.
func normalize(value string) string {
	value = strings.TrimSpace(strings.ToLower(value))
	var b strings.Builder
	dash := false
	for _, r := range value {
		switch {
		case r >= 'a' && r <= 'z' || r >= '0' && r <= '9':
			b.WriteRune(r)
			dash = false
		case r == '-' || r == '_' || r == ' ':
			if !dash {
				b.WriteByte('-')
				dash = true
			}
		}
	}
	return strings.Trim(b.String(), "-")
}

// NewID mirrors MeetingIdentity.NewId: normalize requestedID (falling back
// to title, then "meeting"), truncate to 42 chars, then retry with either a
// random 5-byte base64url suffix (first attempt) or an incrementing numeric
// suffix until exists returns false.
func NewID(requestedID, title string, exists func(string) bool) string {
	candidate := normalize(requestedID)
	if candidate == "" {
		candidate = normalize(title)
	}
	if candidate == "" {
		candidate = "meeting"
	}
	if len(candidate) > 42 {
		candidate = candidate[:42]
	}
	baseID := candidate
	for number := 0; ; number++ {
		var suffix string
		if number == 0 {
			suffix = "-" + strings.ToLower(RandomToken(5))
		} else {
			suffix = "-" + itoa(number)
		}
		maxBaseLen := 48 - len(suffix)
		if maxBaseLen < 0 {
			maxBaseLen = 0
		}
		trimmedBase := baseID
		if len(trimmedBase) > maxBaseLen {
			trimmedBase = trimmedBase[:maxBaseLen]
		}
		id := strings.Trim(trimmedBase+suffix, "-")
		if !exists(id) {
			return id
		}
	}
}

func itoa(n int) string {
	if n == 0 {
		return "0"
	}
	var b [20]byte
	i := len(b)
	for n > 0 {
		i--
		b[i] = byte('0' + n%10)
		n /= 10
	}
	return string(b[i:])
}

// dnsLabel mirrors the DNS-label grammar Uri.CheckHostName accepts for a
// DNS-type host: 1-63 chars, alphanumeric with internal hyphens, no
// leading/trailing hyphen. This is a best-effort re-implementation of
// Uri.CheckHostName's DNS branch (there is no .NET-identical host-name
// grammar checker in the Go standard library); it is not part of the
// client/server wire-compatibility contract (research-sso-broker.md §7),
// only a server-side safety check on admin-supplied connection hosts.
var dnsLabel = regexp.MustCompile(`^[A-Za-z0-9]([A-Za-z0-9-]{0,61}[A-Za-z0-9])?$`)

func isValidDNSName(host string) bool {
	if host == "" || len(host) > 253 {
		return false
	}
	for _, label := range strings.Split(host, ".") {
		if !dnsLabel.MatchString(label) {
			return false
		}
	}
	return true
}

// IsSafeHost mirrors IsSafeHost in Program.cs: reject hosts containing
// '/', '?', '#', or whitespace, length 1-253, otherwise valid as a DNS
// name, IPv4, or IPv6 address (after stripping surrounding '[' ']').
func IsSafeHost(value string) bool {
	host := strings.TrimSpace(value)
	if len(host) < 1 || len(host) > 253 {
		return false
	}
	if strings.ContainsAny(host, "/?# \t\n\r\v\f") {
		return false
	}
	stripped := strings.Trim(host, "[]")
	if ip := net.ParseIP(stripped); ip != nil {
		return true
	}
	return isValidDNSName(stripped)
}
