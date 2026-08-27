// Package security contains the small, shared HTTP hardening primitives used
// by Concierge's outbound OIDC clients.  In particular, URL scheme checks are
// not sufficient for an OIDC endpoint: a valid HTTPS URL can still resolve to
// loopback, link-local, RFC1918, or other non-public address space.
package security

import (
	"context"
	"errors"
	"fmt"
	"net"
	"net/http"
	"net/url"
	"strings"
	"time"
)

const (
	// MaxOIDCRequestBytes bounds form bodies sent to the token relay.
	MaxOIDCRequestBytes int64 = 64 << 10
	// MaxOIDCResponseBytes bounds token and JWKS responses.  OIDC responses are
	// small; accepting megabytes here only creates a memory/streaming DoS.
	MaxOIDCResponseBytes int64 = 1 << 20
)

var errPrivateAddress = errors.New("outbound URL resolves to a non-public address")

// ValidateHTTPSURL validates the URL shape and resolves the hostname before
// the request is made.  The transport below repeats the address check at dial
// time, closing the DNS-rebinding window between validation and connection.
func ValidateHTTPSURL(ctx context.Context, raw string) error {
	raw = strings.TrimSpace(raw)
	// ParseRequestURI intentionally accepts a fragment-like suffix as part of
	// the request URI. Reject it before parsing because fragments are never
	// sent to an HTTPS endpoint and accepting them makes validation differ from
	// the URL that the HTTP client actually requests.
	if strings.Contains(raw, "#") {
		return fmt.Errorf("URL must be an absolute HTTPS URL without user info or fragment")
	}
	u, err := url.ParseRequestURI(raw)
	if err != nil || !u.IsAbs() || !strings.EqualFold(u.Scheme, "https") || u.Host == "" || u.User != nil || u.Fragment != "" {
		return fmt.Errorf("URL must be an absolute HTTPS URL without user info or fragment")
	}
	if ip := net.ParseIP(u.Hostname()); ip != nil {
		if forbiddenIP(ip) {
			return errPrivateAddress
		}
		return nil
	}
	ips, err := net.DefaultResolver.LookupIP(ctx, "ip", u.Hostname())
	if err != nil {
		return fmt.Errorf("resolve outbound URL host: %w", err)
	}
	if len(ips) == 0 {
		return errors.New("outbound URL host has no addresses")
	}
	for _, ip := range ips {
		if forbiddenIP(ip) {
			return errPrivateAddress
		}
	}
	return nil
}

// NewRestrictedHTTPClient returns a client that does not use ambient proxy
// environment variables and validates every address immediately before dial.
// This is deliberately a separate constructor so tests can inject an explicit
// loopback client without weakening production clients.
func NewRestrictedHTTPClient(timeout time.Duration) *http.Client {
	dialer := &net.Dialer{Timeout: timeout}
	transport := &http.Transport{
		Proxy: nil,
		DialContext: func(ctx context.Context, network, address string) (net.Conn, error) {
			host, port, err := net.SplitHostPort(address)
			if err != nil {
				return nil, err
			}
			ips, err := net.DefaultResolver.LookupIP(ctx, "ip", host)
			if err != nil {
				return nil, err
			}
			var lastErr error
			for _, ip := range ips {
				if forbiddenIP(ip) {
					lastErr = errPrivateAddress
					continue
				}
				conn, dialErr := dialer.DialContext(ctx, network, net.JoinHostPort(ip.String(), port))
				if dialErr == nil {
					return conn, nil
				}
				lastErr = dialErr
			}
			if lastErr == nil {
				lastErr = errors.New("host has no usable public address")
			}
			return nil, lastErr
		},
		TLSHandshakeTimeout:   timeout,
		ResponseHeaderTimeout: timeout,
		IdleConnTimeout:       30 * time.Second,
		MaxIdleConns:          32,
	}
	return &http.Client{Transport: transport, Timeout: timeout}
}

func forbiddenIP(ip net.IP) bool {
	if ip == nil || ip.IsLoopback() || ip.IsPrivate() || ip.IsLinkLocalUnicast() || ip.IsLinkLocalMulticast() || ip.IsUnspecified() || ip.IsMulticast() {
		return true
	}
	// net.IP.IsPrivate intentionally does not include RFC 6598 shared space.
	if v4 := ip.To4(); v4 != nil && v4[0] == 100 && v4[1] >= 64 && v4[1] <= 127 {
		return true
	}
	return false
}

// PublicAddressError is kept as a stable predicate for callers/tests without
// exposing the internal reason string to HTTP clients.
func PublicAddressError(err error) bool { return errors.Is(err, errPrivateAddress) }
