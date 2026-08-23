package config

import (
	"crypto/subtle"
	"net"
	"net/http"
	"net/url"
	"strings"
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
// incoming request. Forwarded headers are honored only when the request peer
// belongs to an explicitly configured TrustedProxyCIDRs network.
func (s *Store) RequestOrigin(r *http.Request) string {
	if configured := s.PublicBaseUrl(); configured != "" {
		if u, err := url.ParseRequestURI(configured); validOrigin(u, err) {
			return u.Scheme + "://" + u.Host
		}
	}
	scheme := "http"
	if r.TLS != nil {
		scheme = "https"
	}
	host := r.Host
	if s.trustedProxy(r.RemoteAddr) {
		if forwardedProto := strings.ToLower(strings.TrimSpace(r.Header.Get("X-Forwarded-Proto"))); forwardedProto == "http" || forwardedProto == "https" {
			scheme = forwardedProto
		}
		if forwardedHost := strings.TrimSpace(r.Header.Get("X-Forwarded-Host")); forwardedHost != "" {
			if candidate, err := url.Parse("//" + forwardedHost); err == nil && candidate.Host != "" && candidate.Path == "" && candidate.RawQuery == "" && candidate.Fragment == "" && candidate.User == nil {
				host = candidate.Host
			}
		}
	}
	return scheme + "://" + host
}

func validOrigin(u *url.URL, err error) bool {
	return err == nil && u != nil && u.IsAbs() && (u.Scheme == "https" || u.Scheme == "http") && u.Host != "" && u.User == nil && u.Path == "" && u.RawQuery == "" && u.Fragment == ""
}

func (s *Store) trustedProxy(remoteAddr string) bool {
	host, _, err := net.SplitHostPort(remoteAddr)
	if err != nil {
		host = remoteAddr
	}
	ip := net.ParseIP(host)
	if ip == nil {
		return false
	}
	for _, raw := range s.TrustedProxyCIDRs() {
		_, network, err := net.ParseCIDR(strings.TrimSpace(raw))
		if err == nil && network.Contains(ip) {
			return true
		}
	}
	return false
}
