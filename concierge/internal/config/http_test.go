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
		want           string
	}{
		{"no public base url, plain request", "", "example.com", "", "", "http://example.com"},
		{"honors X-Forwarded-Host/Proto", "", "internal:8080", "public.example.com", "https", "https://public.example.com"},
		{"https public base url wins", "https://auth.example.com", "internal:8080", "", "", "https://auth.example.com"},
		{"non-https public base url ignored", "http://auth.example.com", "example.com", "", "", "http://example.com"},
	}
	for _, tc := range cases {
		t.Run(tc.name, func(t *testing.T) {
			s := &Store{cfg: BrokerConfig{PublicBaseUrl: tc.publicBaseURL}}
			r := httptest.NewRequest(http.MethodGet, "/", nil)
			r.Host = tc.host
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
