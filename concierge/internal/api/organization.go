package api

import (
	"net/http"

	"github.com/sekaimate/sekaimate/concierge/internal/config"
)

// ListAdminServers implements GET /admin/servers.
func (a *serverAPI) ListAdminServers(w http.ResponseWriter, r *http.Request) {
	if !a.deps.Config.AdminAuthorized(r) {
		unauthorized(w)
		return
	}
	servers := a.deps.Config.GetServers()
	out := make([]AdminServerInfo, len(servers))
	for i, s := range servers {
		out[i] = adminServerInfo(s)
	}
	writeJSON(w, http.StatusOK, out)
}

// GetOrganization implements GET /admin/organization.
func (a *serverAPI) GetOrganization(w http.ResponseWriter, r *http.Request) {
	if !a.deps.Config.AdminAuthorized(r) {
		unauthorized(w)
		return
	}
	writeJSON(w, http.StatusOK, organizationToAPI(a.deps.Config.GetOrganization()))
}

// PutOrganization implements PUT /admin/organization.
func (a *serverAPI) PutOrganization(w http.ResponseWriter, r *http.Request) {
	if !a.deps.Config.AdminAuthorized(r) {
		unauthorized(w)
		return
	}
	var body OrganizationOptions
	if err := decodeJSON(w, r, &body); err != nil {
		writeError(w, http.StatusBadRequest, "invalid organization payload")
		return
	}
	organization := apiToOrganization(body)
	organization.Providers = preserveProviderSecrets(a.deps.Config.GetOrganization().Providers, organization.Providers)
	if ok, msg := a.deps.Config.SetOrganization(organization); !ok {
		writeError(w, http.StatusBadRequest, msg)
		return
	}
	w.WriteHeader(http.StatusNoContent)
}

// PutAdminServer implements PUT /admin/servers/{serverId}. The path id wins
// over any id in the body, matching Program.cs's `server.Id = serverId;`.
func (a *serverAPI) PutAdminServer(w http.ResponseWriter, r *http.Request, serverId ServerId) {
	if !a.deps.Config.AdminAuthorized(r) {
		unauthorized(w)
		return
	}
	var body AdminServerWrite
	if err := decodeJSON(w, r, &body); err != nil {
		writeError(w, http.StatusBadRequest, "invalid server payload")
		return
	}
	server := apiToServerConfig(serverId, body)
	// The GET representation intentionally never returns literal key material.
	// Preserve fields omitted by an AdminUi edit so changing browser endpoints
	// cannot accidentally make an otherwise-ready static server invalid.
	if existing, found := a.deps.Config.FindServer(serverId); found {
		if body.TicketSigningKeyEnvironmentVariable == nil && body.TicketSigningKey == nil {
			server.TicketSigningKeyEnvironmentVariable = existing.TicketSigningKeyEnvironmentVariable
			server.TicketSigningKey = existing.TicketSigningKey
		}
		if body.TransportPublicKeyEnvironmentVariable == nil && body.TransportPublicKey == nil {
			server.TransportPublicKeyEnvironmentVariable = existing.TransportPublicKeyEnvironmentVariable
			server.TransportPublicKey = existing.TransportPublicKey
		}
		if body.Providers == nil {
			server.Providers = existing.Providers
		} else {
			server.Providers = preserveProviderSecrets(existing.Providers, server.Providers)
		}
		if body.WebSocketUri == nil {
			server.WebSocketUri = existing.WebSocketUri
		}
		if body.ServerInfoUri == nil {
			server.ServerInfoUri = existing.ServerInfoUri
		}
	}
	if ok, msg := a.deps.Config.UpsertServer(server); !ok {
		writeError(w, http.StatusBadRequest, msg)
		return
	}
	w.WriteHeader(http.StatusNoContent)
}

// DeleteAdminServer implements DELETE /admin/servers/{serverId}. The
// client-config file path is resolved *before* removing the registry entry:
// ClientConfigPath requires the server to still be known, and the C#
// broker's equivalent handler resolves it *after* removing the entry
// (Program.cs), so ClientConfigPath always returns null there and the file
// is never actually deleted — a latent order-of-operations bug, not a
// documented behavior. concierge fixes the ordering so deletion works as
// evidently intended, matching the same "fix, don't silently reproduce"
// treatment research-sso-broker.md §7-11/§8 applies to appsettings.json's
// missing chmod.
func (a *serverAPI) DeleteAdminServer(w http.ResponseWriter, r *http.Request, serverId ServerId) {
	if !a.deps.Config.AdminAuthorized(r) {
		unauthorized(w)
		return
	}
	configPath, hasConfigPath := a.deps.Config.ClientConfigPath(serverId)
	if err := a.deps.Config.RemoveServer(serverId); err != nil {
		if err == config.ErrServerNotFound {
			w.WriteHeader(http.StatusNotFound)
			return
		}
		writeError(w, http.StatusInternalServerError, err.Error())
		return
	}
	if hasConfigPath {
		_ = removeIfExists(configPath)
	}
	w.WriteHeader(http.StatusNoContent)
}
