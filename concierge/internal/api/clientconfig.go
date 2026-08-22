package api

import (
	"encoding/json"
	"io"
	"net/http"
	"os"
	"path/filepath"
	"strings"
)

// maxClientConfigBytes matches the 262144-byte cap PUT /admin/client-config
// enforces in Program.cs.
const maxClientConfigBytes = 262144

// GetClientConfig implements the public GET /client-config/{serverId}: the
// stored client-config file with every "clientSecret" key stripped
// recursively (research-sso-broker.md §7-10).
func (a *serverAPI) GetClientConfig(w http.ResponseWriter, r *http.Request, serverId ServerId) {
	if _, ok := a.deps.Config.FindServer(serverId); !ok {
		w.WriteHeader(http.StatusNotFound)
		return
	}
	path, ok := a.deps.Config.ClientConfigPath(serverId)
	if !ok {
		w.WriteHeader(http.StatusNotFound)
		return
	}
	raw, err := os.ReadFile(path)
	if err != nil {
		w.WriteHeader(http.StatusNotFound)
		return
	}
	cleaned, err := removeSecretsJSON(raw)
	if err != nil {
		// RemoveSecrets falls back to "{}" if the stored document does not
		// parse — should not happen since PUT validates JSON up front, but
		// matches JsonNode.Parse(...)?.ToJsonString() ?? "{}" defensively.
		cleaned = []byte("{}")
	}
	w.Header().Set("Content-Type", "application/json; charset=utf-8")
	w.WriteHeader(http.StatusOK)
	_, _ = w.Write(cleaned)
}

// GetClientConfigTemplate implements GET /admin/client-config-template/{serverId}:
// the canonical, pretty-printed client configuration including clientSecret
// (admin-only).
func (a *serverAPI) GetClientConfigTemplate(w http.ResponseWriter, r *http.Request, serverId ServerId) {
	if !a.deps.Config.AdminAuthorized(r) {
		unauthorized(w)
		return
	}
	server, ok := a.deps.Config.FindServer(serverId)
	if !ok {
		w.WriteHeader(http.StatusNotFound)
		return
	}
	publicKey := server.EffectiveTransportPublicKey()
	if publicKey == "" {
		writeProblem(w, http.StatusServiceUnavailable, "The server transport public key is not available to this broker.")
		return
	}
	organization := a.deps.Config.GetOrganization()
	providers := organization.Providers
	if len(providers) == 0 {
		providers = server.Providers
	}
	webSocketURI, serverInfoURI := server.WebSocketUri, server.ServerInfoUri
	if meeting, ok := a.deps.Meetings.Find(server.Id); ok {
		if meeting.WebSocketUri != "" {
			webSocketURI = meeting.WebSocketUri
		}
		if meeting.ServerInfoUri != "" {
			serverInfoURI = meeting.ServerInfoUri
		}
	}
	origin := a.deps.Config.RequestOrigin(r)
	writeJSONIndent(w, http.StatusOK, clientConfiguration(origin, server.Id, publicKey, webSocketURI, serverInfoURI, providers, organization.DefaultProviderId))
}

// GetAdminClientConfig implements GET /admin/client-config/{serverId}: the
// raw stored file bytes, unmodified (admin-only, so clientSecret is not
// stripped).
func (a *serverAPI) GetAdminClientConfig(w http.ResponseWriter, r *http.Request, serverId ServerId) {
	if !a.deps.Config.AdminAuthorized(r) {
		unauthorized(w)
		return
	}
	if _, ok := a.deps.Config.FindServer(serverId); !ok {
		w.WriteHeader(http.StatusNotFound)
		return
	}
	path, ok := a.deps.Config.ClientConfigPath(serverId)
	if !ok {
		w.WriteHeader(http.StatusNotFound)
		return
	}
	raw, err := os.ReadFile(path)
	if err != nil {
		w.WriteHeader(http.StatusNotFound)
		return
	}
	w.Header().Set("Content-Type", "application/json; charset=utf-8")
	w.WriteHeader(http.StatusOK)
	_, _ = w.Write(raw)
}

// PutAdminClientConfig implements PUT /admin/client-config/{serverId}:
// validates only that the body parses as a JSON object (no schema check,
// matching ClientConfig.TryValidate), then writes it atomically. Unlike the
// C# broker (which checks Content-Length but does not cap the actual read),
// this also wraps the body reader in http.MaxBytesReader so a client that
// lies about Content-Length cannot bypass the 262144-byte cap — a strictly
// safer version of the same documented limit, not an observable behavior
// change for any well-behaved client.
func (a *serverAPI) PutAdminClientConfig(w http.ResponseWriter, r *http.Request, serverId ServerId) {
	if !a.deps.Config.AdminAuthorized(r) {
		unauthorized(w)
		return
	}
	if _, ok := a.deps.Config.FindServer(serverId); !ok {
		w.WriteHeader(http.StatusNotFound)
		return
	}
	if r.ContentLength > maxClientConfigBytes {
		writeError(w, http.StatusBadRequest, "Configuration is too large.")
		return
	}
	body, err := io.ReadAll(http.MaxBytesReader(w, r.Body, maxClientConfigBytes))
	if err != nil {
		writeError(w, http.StatusBadRequest, "Configuration is too large.")
		return
	}
	if strings.TrimSpace(string(body)) == "" {
		writeError(w, http.StatusBadRequest, "Configuration must not be empty.")
		return
	}
	var probe any
	if err := json.Unmarshal(body, &probe); err != nil {
		writeError(w, http.StatusBadRequest, "Invalid JSON: "+err.Error())
		return
	}
	if _, ok := probe.(map[string]any); !ok {
		writeError(w, http.StatusBadRequest, "Configuration root must be a JSON object.")
		return
	}

	path, ok := a.deps.Config.ClientConfigPath(serverId)
	if !ok {
		writeProblem(w, http.StatusServiceUnavailable, "Client configuration storage is not configured.")
		return
	}
	if dir := filepath.Dir(path); dir != "." {
		if err := os.MkdirAll(dir, 0o755); err != nil {
			writeError(w, http.StatusInternalServerError, err.Error())
			return
		}
	}
	tmp := path + ".tmp"
	if err := os.WriteFile(tmp, body, 0o644); err != nil {
		writeError(w, http.StatusInternalServerError, err.Error())
		return
	}
	if err := os.Rename(tmp, path); err != nil {
		writeError(w, http.StatusInternalServerError, err.Error())
		return
	}
	w.WriteHeader(http.StatusNoContent)
}

// removeSecretsJSON parses raw as JSON and recursively removes any object
// key equal to "clientSecret" (case-insensitive), including inside nested
// objects and arrays, then re-serializes it pretty-printed — matching
// ClientConfig.RemoveSecrets (research-sso-broker.md §7-10).
func removeSecretsJSON(raw []byte) ([]byte, error) {
	var value any
	if err := json.Unmarshal(raw, &value); err != nil {
		return nil, err
	}
	return json.MarshalIndent(removeSecretsValue(value), "", "  ")
}

func removeSecretsValue(value any) any {
	switch v := value.(type) {
	case map[string]any:
		out := make(map[string]any, len(v))
		for key, child := range v {
			if strings.EqualFold(key, "clientSecret") {
				continue
			}
			out[key] = removeSecretsValue(child)
		}
		return out
	case []any:
		out := make([]any, len(v))
		for i, child := range v {
			out[i] = removeSecretsValue(child)
		}
		return out
	default:
		return v
	}
}
