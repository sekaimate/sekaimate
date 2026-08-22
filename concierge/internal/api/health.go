package api

import "net/http"

// Health implements GET /health, matching Program.cs: ready iff every
// configured server is individually ready (AND across all servers, not OR
// — research-sso-broker.md §7-15).
func (a *serverAPI) Health(w http.ResponseWriter, r *http.Request) {
	servers := a.deps.Config.GetServers()
	infos := make([]HealthServerInfo, 0, len(servers))
	ready := len(servers) > 0
	for _, s := range servers {
		var providerIDs []string
		for _, p := range s.Providers {
			if p.Id != "" {
				providerIDs = append(providerIDs, p.Id)
			}
		}
		if providerIDs == nil {
			providerIDs = []string{}
		}
		infos = append(infos, HealthServerInfo{Id: s.Id, Ready: s.IsReady(), Providers: providerIDs})
		if !s.IsReady() {
			ready = false
		}
	}
	if ready {
		writeJSON(w, http.StatusOK, HealthResponse{Status: "ready", Servers: infos})
		return
	}
	errMsg := "Configure providers and ticket signing keys for every broker server."
	writeJSON(w, http.StatusServiceUnavailable, HealthResponse{Status: "not_ready", Error: &errMsg, Servers: infos})
}
