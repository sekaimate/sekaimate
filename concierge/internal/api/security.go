package api

import (
	"net/http"
	"strings"
)

// SecurityHeaders applies headers that are safe for both JSON API responses
// and the embedded Admin UI/join HTML. The inline script in the join shell is
// intentionally covered by 'unsafe-inline' for compatibility; object/embed,
// framing, and base-URI attacks remain blocked.
func SecurityHeaders(next http.Handler) http.Handler {
	return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		w.Header().Set("X-Content-Type-Options", "nosniff")
		w.Header().Set("X-Frame-Options", "DENY")
		w.Header().Set("Referrer-Policy", "no-referrer")
		w.Header().Set("Content-Security-Policy", "default-src 'self'; base-uri 'self'; object-src 'none'; frame-ancestors 'none'; form-action 'self'; connect-src 'self' https: wss:; img-src 'self' data:; style-src 'self' 'unsafe-inline'; script-src 'self' 'unsafe-inline'; frame-src http://127.0.0.1:56831")
		if strings.HasPrefix(r.URL.Path, "/admin") || strings.HasPrefix(r.URL.Path, "/api/admin") || strings.HasPrefix(r.URL.Path, "/join/") || strings.HasPrefix(r.URL.Path, "/enroll/") {
			w.Header().Set("Cache-Control", "no-store, private")
		}
		next.ServeHTTP(w, r)
	})
}
