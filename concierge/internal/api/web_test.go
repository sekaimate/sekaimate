package api

import (
	"encoding/json"
	"io"
	"net/http"
	"net/http/httptest"
	"os"
	"path/filepath"
	"strings"
	"testing"

	"github.com/sekaimate/sekaimate/concierge/internal/config"
	"github.com/sekaimate/sekaimate/concierge/internal/controlplane"
)

func newWebDeps(t *testing.T) Deps {
	return newWebDepsAt(t, "https://tokens.example/token")
}

func newWebDepsAt(t *testing.T, tokenEndpoint string) Deps {
	t.Helper()
	dir := t.TempDir()
	configPath := filepath.Join(dir, "appsettings.json")
	contents := `{"Broker":{"PublicBaseUrl":"https://broker.example","AllowedWebOrigins":["https://web.example"],"Organization":{"DefaultProviderId":"google","Providers":[{"Id":"google","Label":"Google","Issuer":"https://accounts.example","Audience":"native","WebClientId":"web-client","WebClientSecret":"server-secret","TokenEndpoint":"` + tokenEndpoint + `","JwksUri":"https://accounts.example/jwks","AllowedHostedDomains":["example.com"]}]}}}`
	if err := os.WriteFile(configPath, []byte(contents), 0o600); err != nil {
		t.Fatal(err)
	}
	cfg, err := config.Load(configPath)
	if err != nil {
		t.Fatal(err)
	}
	deps := newTestDeps(t)
	deps.Config = cfg
	if err := deps.Config.AddServer(config.ServerConfig{
		Id: "srv", TicketSigningKey: strings.Repeat("k", 32), TransportPublicKey: "public",
		WebSocketUri: "wss://game.example/basis", ServerInfoUri: "https://game.example/server-info",
		Providers: []config.ProviderConfig{{Id: "google", Issuer: "https://accounts.example", Audience: "native", WebClientId: "web-client", WebClientSecret: "server-secret", TokenEndpoint: "https://tokens.example/token", JwksUri: "https://accounts.example/jwks"}},
	}); err != nil {
		t.Fatal(err)
	}
	if err := deps.Meetings.Add(controlplane.MeetingRecord{Id: "srv", Title: "Web room", Status: "ready", Host: "game.example", Port: 4296, Password: "password", InviteToken: "invite", TransportPublicKey: "public", WebSocketUri: "wss://game.example/basis"}); err != nil {
		t.Fatal(err)
	}
	return deps
}

func TestWebConfigManifestDetailsAndCors(t *testing.T) {
	deps := newWebDeps(t)
	mux := NewMux(deps)

	req := httptest.NewRequest(http.MethodGet, "/web-client-config/srv", nil)
	rec := httptest.NewRecorder()
	mux.ServeHTTP(rec, req)
	if rec.Code != http.StatusOK || strings.Contains(rec.Body.String(), "server-secret") {
		t.Fatalf("web client config: status=%d body=%s", rec.Code, rec.Body.String())
	}
	var cfg ClientConfiguration
	if err := json.Unmarshal(rec.Body.Bytes(), &cfg); err != nil {
		t.Fatal(err)
	}
	if cfg.Providers == nil || len(*cfg.Providers) != 1 || *(*cfg.Providers)[0].TokenEndpoint != "https://broker.example/web-oidc/srv/google/token" {
		t.Fatalf("web provider config = %+v", cfg.Providers)
	}
	if (*cfg.Providers)[0].ExtraAuthParams == nil || (*(*cfg.Providers)[0].ExtraAuthParams)["hd"] != "example.com" {
		t.Fatalf("web auth params = %+v", (*cfg.Providers)[0].ExtraAuthParams)
	}

	req = httptest.NewRequest(http.MethodGet, "/join/invite/web-config", nil)
	req.Header.Set("Origin", "https://web.example")
	rec = httptest.NewRecorder()
	mux.ServeHTTP(rec, req)
	if rec.Code != http.StatusOK || rec.Header().Get("Access-Control-Allow-Origin") != "https://web.example" || rec.Header().Get("Cache-Control") != "no-store" {
		t.Fatalf("join web config: status=%d headers=%v body=%s", rec.Code, rec.Header(), rec.Body.String())
	}

	req = httptest.NewRequest(http.MethodGet, "/join/invite/web-manifest", nil)
	req.Header.Set("Origin", "https://web.example")
	rec = httptest.NewRecorder()
	mux.ServeHTTP(rec, req)
	if rec.Code != http.StatusOK || rec.Header().Get("Cache-Control") != "no-store" {
		t.Fatalf("manifest: status=%d headers=%v body=%s", rec.Code, rec.Header(), rec.Body.String())
	}
	var manifest WebMeetingManifest
	if err := json.Unmarshal(rec.Body.Bytes(), &manifest); err != nil {
		t.Fatal(err)
	}
	if manifest.ConfigUrl != "https://broker.example/join/invite/web-config" || manifest.WebsocketUri != "wss://game.example/basis" || manifest.UserName != "web-guest-invite" || manifest.Password != "password" {
		t.Fatalf("manifest = %+v", manifest)
	}

	req = httptest.NewRequest(http.MethodGet, "/join/invite/details", nil)
	rec = httptest.NewRecorder()
	mux.ServeHTTP(rec, req)
	if rec.Code != http.StatusOK || !strings.Contains(rec.Body.String(), "basis-join?url=") || !strings.Contains(rec.Body.String(), "basisMeeting=1") {
		t.Fatalf("details: status=%d body=%s", rec.Code, rec.Body.String())
	}

	req = httptest.NewRequest(http.MethodGet, "/join/invite/web-manifest", nil)
	req.Header.Set("Origin", "https://evil.example")
	rec = httptest.NewRecorder()
	mux.ServeHTTP(rec, req)
	if rec.Code != http.StatusForbidden {
		t.Fatalf("evil origin: status=%d body=%s", rec.Code, rec.Body.String())
	}
}

func TestWebOidcRelayRejectsUnsupportedAndInvalidRedirect(t *testing.T) {
	deps := newWebDeps(t)
	mux := NewMux(deps)
	for _, form := range []string{
		"grant_type=client_credentials",
		"grant_type=authorization_code&code=c&code_verifier=v&redirect_uri=https%3A%2F%2Fevil.example%2Fsso-callback",
	} {
		req := httptest.NewRequest(http.MethodPost, "/web-oidc/srv/google/token", strings.NewReader(form))
		req.Header.Set("Content-Type", "application/x-www-form-urlencoded")
		req.Header.Set("Origin", "https://web.example")
		rec := httptest.NewRecorder()
		mux.ServeHTTP(rec, req)
		if rec.Code != http.StatusBadRequest {
			t.Fatalf("form %q: status=%d body=%s", form, rec.Code, rec.Body.String())
		}
	}

	req := httptest.NewRequest(http.MethodOptions, "/web-oidc/srv/google/token", nil)
	req.Header.Set("Origin", "https://web.example")
	rec := httptest.NewRecorder()
	mux.ServeHTTP(rec, req)
	if rec.Code != http.StatusNoContent || rec.Header().Get("Access-Control-Allow-Origin") != "https://web.example" {
		t.Fatalf("preflight: status=%d headers=%v", rec.Code, rec.Header())
	}
}

func TestWebOidcRelayForwardsOnlyAllowedFieldsAndServerSecret(t *testing.T) {
	var received string
	upstream := httptest.NewTLSServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		body, _ := io.ReadAll(r.Body)
		received = string(body)
		w.Header().Set("Content-Type", "application/json")
		w.WriteHeader(http.StatusCreated)
		_, _ = w.Write([]byte(`{"access_token":"ok"}`))
	}))
	defer upstream.Close()
	oldClient := webOIDCHTTPClient
	oldAllowUnsafe := webOIDCAllowUnsafeEndpoints
	webOIDCHTTPClient = upstream.Client()
	webOIDCAllowUnsafeEndpoints = true
	t.Cleanup(func() {
		webOIDCHTTPClient = oldClient
		webOIDCAllowUnsafeEndpoints = oldAllowUnsafe
	})
	deps := newWebDepsAt(t, upstream.URL+"/token")
	mux := NewMux(deps)
	form := "grant_type=authorization_code&code=code-value&redirect_uri=https%3A%2F%2Fweb.example%2Fsso-callback&code_verifier=verifier&scope=should-not-forward"
	req := httptest.NewRequest(http.MethodPost, "/web-oidc/srv/google/token", strings.NewReader(form))
	req.Header.Set("Content-Type", "application/x-www-form-urlencoded")
	req.Header.Set("Origin", "https://web.example")
	rec := httptest.NewRecorder()
	mux.ServeHTTP(rec, req)
	if rec.Code != http.StatusCreated || rec.Header().Get("Cache-Control") != "no-store" {
		t.Fatalf("relay: status=%d headers=%v body=%s", rec.Code, rec.Header(), rec.Body.String())
	}
	if !strings.Contains(received, "client_id=web-client") || !strings.Contains(received, "client_secret=server-secret") || strings.Contains(received, "scope%3D") || strings.Contains(received, "scope=") {
		t.Fatalf("upstream form = %q", received)
	}
}
