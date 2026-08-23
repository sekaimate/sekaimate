package api

import (
	"encoding/json"
	"net/http"

	"github.com/sekaimate/sekaimate/concierge/internal/admission"
)

const (
	maxIDTokenLength = 16384
	maxDIDLength     = 256
)

// Admit implements POST /admission/{serverId}, matching
// CreateAdmissionAsync in Program.cs field-for-field, including setting
// Cache-Control: no-store unconditionally as the very first thing the
// handler does (research-sso-broker.md §1.3/§7-4).
func (a *serverAPI) Admit(w http.ResponseWriter, r *http.Request, serverId ServerId) {
	w.Header().Set("Cache-Control", "no-store")
	if !applyAdmissionCors(w, r, a.deps.Config) {
		w.WriteHeader(http.StatusForbidden)
		return
	}

	var req AdmissionRequest
	if err := json.NewDecoder(r.Body).Decode(&req); err != nil {
		writeError(w, http.StatusBadRequest, "idToken and did are required")
		return
	}
	if req.IdToken == "" || req.Did == "" || len(req.IdToken) > maxIDTokenLength || len(req.Did) > maxDIDLength {
		writeError(w, http.StatusBadRequest, "idToken and did are required")
		return
	}
	for _, c := range req.Did {
		if c == '\n' || c == '\r' {
			writeError(w, http.StatusBadRequest, "invalid did")
			return
		}
	}

	server, ok := a.deps.Config.FindServer(serverId)
	if !ok {
		w.WriteHeader(http.StatusNotFound)
		return
	}
	if !server.IsReady() {
		writeProblem(w, http.StatusServiceUnavailable, "Broker server is not configured.")
		return
	}

	providers := make([]admission.Provider, len(server.Providers))
	for i, p := range server.Providers {
		providers[i] = admission.Provider{
			ID: p.Id, Issuer: p.Issuer, Audience: p.Audience, WebClientID: p.WebClientId, JwksURI: p.JwksUri,
			AllowedHostedDomains: p.AllowedHostedDomains, AllowedGroups: p.AllowedGroups,
		}
	}
	identity, err := a.deps.Validator.Validate(r.Context(), req.IdToken, providers)
	if err != nil {
		unauthorized(w)
		return
	}

	ticket, err := admission.CreateTicket(server.EffectiveTicketSigningKey(), identity.Issuer, identity.Subject, req.Did)
	if err != nil {
		writeProblem(w, http.StatusServiceUnavailable, "Failed to mint ticket.")
		return
	}
	writeJSON(w, http.StatusOK, AdmissionResponse{Ticket: ticket})
}

// AdmissionOptions handles browser preflight for the admission endpoint.
func (a *serverAPI) AdmissionOptions(w http.ResponseWriter, r *http.Request, serverId ServerId) {
	if !applyAdmissionCors(w, r, a.deps.Config) {
		w.WriteHeader(http.StatusForbidden)
		return
	}
	w.WriteHeader(http.StatusNoContent)
}
