package api

import (
	"net/http"
	"strings"

	"github.com/sekaimate/sekaimate/concierge/internal/config"
)

func allowedOrigin(store *config.Store, origin string) bool {
	normalized := strings.TrimRight(strings.TrimSpace(origin), "/")
	if normalized == "" {
		return true
	}
	for _, allowed := range store.AllowedWebOrigins() {
		if strings.EqualFold(strings.TrimRight(strings.TrimSpace(allowed), "/"), normalized) {
			return true
		}
	}
	return false
}

func applyAdmissionCors(w http.ResponseWriter, r *http.Request, store *config.Store) bool {
	origin := r.Header.Get("Origin")
	if strings.TrimSpace(origin) == "" {
		return true
	}
	normalized := strings.TrimRight(strings.TrimSpace(origin), "/")
	if !allowedOrigin(store, normalized) {
		return false
	}
	w.Header().Set("Access-Control-Allow-Origin", normalized)
	w.Header().Set("Access-Control-Allow-Methods", "POST, OPTIONS")
	w.Header().Set("Access-Control-Allow-Headers", "Content-Type")
	w.Header().Set("Access-Control-Max-Age", "600")
	w.Header().Set("Vary", "Origin")
	return true
}

func applyWebCors(w http.ResponseWriter, r *http.Request, store *config.Store) bool {
	origin := r.Header.Get("Origin")
	if strings.TrimSpace(origin) == "" {
		return true
	}
	normalized := strings.TrimRight(strings.TrimSpace(origin), "/")
	if !allowedOrigin(store, normalized) {
		return false
	}
	w.Header().Set("Access-Control-Allow-Origin", normalized)
	w.Header().Set("Access-Control-Allow-Methods", "GET, OPTIONS")
	w.Header().Set("Access-Control-Allow-Headers", "Content-Type")
	w.Header().Set("Vary", "Origin")
	return true
}
