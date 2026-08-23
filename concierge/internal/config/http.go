package config

import (
	"crypto/subtle"
	"net/http"
	"net/url"
)

// constantTimeStringsEqual compares two strings for equality without
// leaking timing information about their content (a length mismatch is
// still a length side-channel, matching CryptographicOperations.FixedTimeEquals
// in the C# broker — see research-sso-broker.md §3.2/§7-13).
func constantTimeStringsEqual(a, b string) bool {
	if len(a) != len(b) {
		return false
	}
	return subtle.ConstantTimeCompare([]byte(a), []byte(b)) == 1
}

// RequestOrigin mirrors RequestOrigin in Program.cs: if PublicBaseUrl is
// configured and parses as an absolute HTTPS URI, its scheme+authority wins
// for every generated link; otherwise the origin is derived from the
// incoming request. Like the C# broker's ForwardedHeaders middleware
// (configured with empty KnownNetworks/KnownProxies, i.e. any proxy is
// trusted unconditionally — an intentional, documented footgun, see
// research-sso-broker.md §6), X-Forwarded-Proto/X-Forwarded-Host are
// honored unconditionally here too rather than silently dropped.
func (s *Store) RequestOrigin(r *http.Request) string {
	if configured := s.PublicBaseUrl(); configured != "" {
		if u, err := url.ParseRequestURI(configured); err == nil && u.IsAbs() && (u.Scheme == "https" || u.Scheme == "http") {
			return u.Scheme + "://" + u.Host
		}
	}
	scheme := "http"
	if r.TLS != nil {
		scheme = "https"
	}
	if forwardedProto := r.Header.Get("X-Forwarded-Proto"); forwardedProto != "" {
		scheme = forwardedProto
	}
	host := r.Host
	if forwardedHost := r.Header.Get("X-Forwarded-Host"); forwardedHost != "" {
		host = forwardedHost
	}
	return scheme + "://" + host
}
