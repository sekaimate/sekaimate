package config

import (
	"net/http"
	"net/http/httptest"
	"testing"
)

func TestRequestOrigin(t *testing.T) {
	cases := []struct {
		name           string
		publicBaseURL  string
		host           string
		forwardedHost  string
		forwardedProto string
		trusted        bool
		want           string
	}{
		{"no public base url, plain request", "", "example.com", "", "", false, "http://example.com"},
		{"ignores untrusted X-Forwarded-Host/Proto", "", "internal:8080", "public.example.com", "https", false, "http://internal:8080"},
		{"honors trusted X-Forwarded-Host/Proto", "", "internal:8080", "public.example.com", "https", true, "https://public.example.com"},
		{"https public base url wins", "https://auth.example.com", "internal:8080", "", "", false, "https://auth.example.com"},
		{"http public base url wins", "http://auth.example.com", "example.com", "", "", false, "http://auth.example.com"},
	}
	for _, tc := range cases {
		t.Run(tc.name, func(t *testing.T) {
			trustedCIDRs := []string(nil)
			if tc.trusted {
				trustedCIDRs = []string{"192.0.2.0/24"}
			}
			s := &Store{cfg: BrokerConfig{PublicBaseUrl: tc.publicBaseURL, TrustedProxyCIDRs: trustedCIDRs}}
			r := httptest.NewRequest(http.MethodGet, "/", nil)
			r.Host = tc.host
			r.RemoteAddr = "192.0.2.10:443"
			if tc.forwardedHost != "" {
				r.Header.Set("X-Forwarded-Host", tc.forwardedHost)
			}
			if tc.forwardedProto != "" {
				r.Header.Set("X-Forwarded-Proto", tc.forwardedProto)
			}
			if got := s.RequestOrigin(r); got != tc.want {
				t.Errorf("RequestOrigin() = %q, want %q", got, tc.want)
			}
		})
	}
}
