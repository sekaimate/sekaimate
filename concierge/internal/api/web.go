package api

import (
	"context"
	"io"
	"net/http"
	"net/url"
	"os"
	"strings"
	"time"

	"github.com/sekaimate/sekaimate/concierge/internal/config"
	"github.com/sekaimate/sekaimate/concierge/internal/security"
)

var webOIDCHTTPClient = security.NewRestrictedHTTPClient(10 * time.Second)

// webOIDCAllowUnsafeEndpoints is test-only injection. Production code never
// enables it; httptest's loopback TLS listener cannot satisfy the public-IP
// policy used by the real outbound transport.
var webOIDCAllowUnsafeEndpoints bool

func webProviders(organization config.OrganizationConfig, server config.ServerConfig) []config.ProviderConfig {
	providers := organization.Providers
	if len(providers) == 0 {
		providers = server.Providers
	}
	web := make([]config.ProviderConfig, 0, len(providers))
	for _, provider := range providers {
		if provider.IsWebConfigured() {
			web = append(web, provider)
		}
	}
	return web
}

func (a *serverAPI) GetWebClientConfig(w http.ResponseWriter, r *http.Request, serverId ServerId) {
	server, ok := a.deps.Config.FindServer(serverId)
	if !ok {
		w.WriteHeader(http.StatusNotFound)
		return
	}
	organization := a.deps.Config.GetOrganization()
	providers := webProviders(organization, server)
	if len(providers) == 0 {
		writeProblem(w, http.StatusServiceUnavailable, "Web SSO is not configured.")
		return
	}
	origin := a.deps.Config.RequestOrigin(r)
	writeJSON(w, http.StatusOK, webClientConfiguration(origin, server.Id, server.EffectiveTransportPublicKey(), server.WebSocketUri, server.ServerInfoUri, providers, organization.DefaultProviderId))
}

func (a *serverAPI) JoinWebConfig(w http.ResponseWriter, r *http.Request, token Token) {
	meeting, ok := a.deps.Meetings.FindInvite(token)
	if !ok {
		w.WriteHeader(http.StatusNotFound)
		return
	}
	if !applyWebCors(w, r, a.deps.Config) {
		w.WriteHeader(http.StatusForbidden)
		return
	}
	server, serverKnown := a.deps.Config.FindServer(meeting.Id)
	organization := a.deps.Config.GetOrganization()
	if !serverKnown {
		writeProblem(w, http.StatusServiceUnavailable, "Meeting Web SSO configuration is incomplete.")
		return
	}
	providers := webProviders(organization, server)
	if len(providers) == 0 || meeting.TransportPublicKey == "" {
		writeProblem(w, http.StatusServiceUnavailable, "Meeting Web SSO configuration is incomplete.")
		return
	}
	w.Header().Set("Cache-Control", "no-store")
	origin := a.deps.Config.RequestOrigin(r)
	ws, info := meetingBrowserEndpoints(meeting, server, true)
	writeJSON(w, http.StatusOK, webClientConfiguration(origin, meeting.Id, meeting.TransportPublicKey, ws, info, providers, organization.DefaultProviderId))
}

func (a *serverAPI) JoinWebManifest(w http.ResponseWriter, r *http.Request, token Token) {
	meeting, ok := a.deps.Meetings.FindInvite(token)
	if !ok {
		w.WriteHeader(http.StatusNotFound)
		return
	}
	if !applyWebCors(w, r, a.deps.Config) {
		w.WriteHeader(http.StatusForbidden)
		return
	}
	if !strings.EqualFold(meeting.Status, "ready") || strings.TrimSpace(meeting.Host) == "" {
		writeText(w, http.StatusConflict, "This meeting is not ready yet.")
		return
	}
	server, serverKnown := a.deps.Config.FindServer(meeting.Id)
	ws, _ := meetingBrowserEndpoints(meeting, server, serverKnown)
	if ws == "" {
		ws = strings.TrimSpace(getenv("BASIS_WEB_SOCKET_URI"))
	}
	if ws == "" {
		origin := a.deps.Config.RequestOrigin(r)
		u, err := url.Parse(origin)
		if err != nil || u.Host == "" {
			writeProblem(w, http.StatusServiceUnavailable, "Broker origin is invalid.")
			return
		}
		scheme := "ws"
		if u.Scheme == "https" {
			scheme = "wss"
		}
		ws = scheme + "://" + u.Host + "/basis"
	}
	origin := a.deps.Config.RequestOrigin(r)
	w.Header().Set("Cache-Control", "no-store")
	writeJSON(w, http.StatusOK, WebMeetingManifest{
		ConfigUrl:    origin + "/join/" + escapeDataString(token) + "/web-config",
		WebsocketUri: ws,
		UserName:     "web-guest-" + token[:min(len(token), 8)],
		Password:     meeting.Password,
	})
}

func (a *serverAPI) JoinDetails(w http.ResponseWriter, r *http.Request, token Token) {
	meeting, ok := a.deps.Meetings.FindInvite(token)
	if !ok {
		writeText(w, http.StatusNotFound, "This meeting invitation is invalid or has been revoked.")
		return
	}
	if !strings.EqualFold(meeting.Status, "ready") || strings.TrimSpace(meeting.Host) == "" {
		writeText(w, http.StatusConflict, "This meeting is not ready yet.")
		return
	}
	origin := a.deps.Config.RequestOrigin(r)
	manifest := origin + "/join/" + escapeDataString(token) + "/manifest"
	webOrigin := ""
	for _, candidate := range a.deps.Config.AllowedWebOrigins() {
		candidate = strings.TrimRight(strings.TrimSpace(candidate), "/")
		u, err := url.Parse(candidate)
		if err != nil || u.Host == "" {
			continue
		}
		if u.Scheme == "https" || (u.Scheme == "http" && isLoopbackHost(u.Hostname())) {
			webOrigin = candidate
			break
		}
	}
	webJoin := ""
	if webOrigin != "" {
		webJoin = webOrigin + "/?basisMeeting=1&meetingUrl=" + escapeDataString(origin+"/join/"+escapeDataString(token)+"/web-manifest")
	}
	writeJSON(w, http.StatusOK, MeetingDetails{
		Title: meeting.Title, WebJoinUrl: webJoin,
		NativeBridgeUrl: "http://127.0.0.1:56831/basis-join?url=" + escapeDataString(manifest),
	})
}

func (a *serverAPI) WebOidcOptions(w http.ResponseWriter, r *http.Request, serverId ServerId, providerId ProviderId) {
	if !applyAdmissionCors(w, r, a.deps.Config) {
		w.WriteHeader(http.StatusForbidden)
		return
	}
	w.WriteHeader(http.StatusNoContent)
}

func (a *serverAPI) WebOidcToken(w http.ResponseWriter, r *http.Request, serverId ServerId, providerId ProviderId) {
	if !applyAdmissionCors(w, r, a.deps.Config) {
		w.WriteHeader(http.StatusForbidden)
		return
	}
	if !strings.HasPrefix(strings.ToLower(r.Header.Get("Content-Type")), "application/x-www-form-urlencoded") {
		writeError(w, http.StatusBadRequest, "form_encoded_request_required")
		return
	}
	r.Body = http.MaxBytesReader(w, r.Body, security.MaxOIDCRequestBytes)
	server, ok := a.deps.Config.FindServer(serverId)
	if !ok {
		w.WriteHeader(http.StatusNotFound)
		return
	}
	organization := a.deps.Config.GetOrganization()
	providers := organization.Providers
	if len(providers) == 0 {
		providers = server.Providers
	}
	var provider config.ProviderConfig
	found := false
	for _, candidate := range providers {
		if candidate.Id == providerId {
			provider, found = candidate, true
			break
		}
	}
	if !found {
		w.WriteHeader(http.StatusNotFound)
		return
	}
	if !provider.IsWebConfigured() {
		writeProblem(w, http.StatusServiceUnavailable, "Web SSO provider credentials are incomplete.")
		return
	}
	if err := r.ParseForm(); err != nil {
		writeError(w, http.StatusBadRequest, "invalid_request")
		return
	}
	grant := r.Form.Get("grant_type")
	forwarded := url.Values{"grant_type": []string{grant}}
	switch grant {
	case "authorization_code":
		redirect := r.Form.Get("redirect_uri")
		if !allowedWebRedirect(a.deps.Config, redirect) {
			writeError(w, http.StatusBadRequest, "invalid_redirect_uri")
			return
		}
		for _, key := range []string{"code", "redirect_uri", "code_verifier"} {
			if value := r.Form.Get(key); value != "" {
				forwarded.Set(key, value)
			}
		}
		if forwarded.Get("code") == "" || forwarded.Get("code_verifier") == "" {
			writeError(w, http.StatusBadRequest, "invalid_request")
			return
		}
	case "refresh_token":
		if value := r.Form.Get("refresh_token"); value != "" {
			forwarded.Set("refresh_token", value)
		} else {
			writeError(w, http.StatusBadRequest, "invalid_request")
			return
		}
	default:
		writeError(w, http.StatusBadRequest, "unsupported_grant_type")
		return
	}
	forwarded.Set("client_id", provider.WebClientId)
	forwarded.Set("client_secret", provider.WebClientSecret)
	if !webOIDCAllowUnsafeEndpoints {
		if err := security.ValidateHTTPSURL(r.Context(), provider.TokenEndpoint); err != nil {
			writeProblem(w, http.StatusServiceUnavailable, "Web SSO provider credentials are incomplete.")
			return
		}
	}
	ctx, cancel := context.WithTimeout(r.Context(), 10*time.Second)
	defer cancel()
	req, err := http.NewRequestWithContext(ctx, http.MethodPost, provider.TokenEndpoint, strings.NewReader(forwarded.Encode()))
	if err != nil {
		writeProblem(w, http.StatusServiceUnavailable, "Web SSO provider credentials are incomplete.")
		return
	}
	req.Header.Set("Content-Type", "application/x-www-form-urlencoded")
	response, err := webOIDCHTTPClient.Do(req)
	if err != nil {
		writeProblem(w, http.StatusServiceUnavailable, "Web SSO token exchange failed.")
		return
	}
	defer response.Body.Close()
	w.Header().Set("Cache-Control", "no-store")
	if contentType := response.Header.Get("Content-Type"); contentType != "" {
		w.Header().Set("Content-Type", contentType)
	}
	body, err := io.ReadAll(io.LimitReader(response.Body, security.MaxOIDCResponseBytes+1))
	if err != nil || int64(len(body)) > security.MaxOIDCResponseBytes {
		writeProblem(w, http.StatusBadGateway, "Web SSO token exchange response is too large.")
		return
	}
	w.WriteHeader(response.StatusCode)
	_, _ = w.Write(body)
}

func allowedWebRedirect(store *config.Store, value string) bool {
	u, err := url.Parse(value)
	if err != nil || !u.IsAbs() || u.Path != "/sso-callback" || u.User != nil || u.Fragment != "" {
		return false
	}
	origin := strings.TrimRight(u.Scheme+"://"+u.Host, "/")
	for _, allowed := range store.AllowedWebOrigins() {
		if strings.EqualFold(strings.TrimRight(strings.TrimSpace(allowed), "/"), origin) {
			return true
		}
	}
	return false
}

func isLoopbackHost(host string) bool {
	return host == "localhost" || strings.HasPrefix(host, "127.") || host == "::1" || strings.HasPrefix(host, "[::1]")
}

func getenv(key string) string {
	return strings.TrimSpace(os.Getenv(key))
}
