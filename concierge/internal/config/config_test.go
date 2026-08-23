package config

import (
	"net/http"
	"net/http/httptest"
	"os"
	"path/filepath"
	"testing"
)

func TestAdminAuthorized_AllowUnauthenticated(t *testing.T) {
	s := &Store{cfg: BrokerConfig{AllowUnauthenticatedAdmin: true}}
	r := httptest.NewRequest(http.MethodGet, "/admin/servers", nil)
	if !s.AdminAuthorized(r) {
		t.Errorf("AdminAuthorized() = false, want true when AllowUnauthenticatedAdmin is set")
	}
}

func TestAdminAuthorized_TokenRequirements(t *testing.T) {
	const envVar = "CONCIERGE_TEST_ADMIN_TOKEN"
	const validToken = "01234567890123456789012345678901" // 34 chars, >=32
	t.Setenv(envVar, validToken)

	s := &Store{cfg: BrokerConfig{AdminTokenEnvironmentVariable: envVar}}

	cases := []struct {
		name   string
		header string
		want   bool
	}{
		{"missing header", "", false},
		{"correct token", "Bearer " + validToken, true},
		{"case-insensitive prefix", "bearer " + validToken, true},
		{"wrong token", "Bearer not-the-token-not-the-token", false},
		{"no prefix", validToken, false},
		{"wrong prefix", "Basic " + validToken, false},
	}
	for _, tc := range cases {
		t.Run(tc.name, func(t *testing.T) {
			r := httptest.NewRequest(http.MethodGet, "/admin/servers", nil)
			if tc.header != "" {
				r.Header.Set("Authorization", tc.header)
			}
			if got := s.AdminAuthorized(r); got != tc.want {
				t.Errorf("AdminAuthorized() = %v, want %v", got, tc.want)
			}
		})
	}
}

func TestAdminAuthorized_MinimumTokenLength(t *testing.T) {
	const envVar = "CONCIERGE_TEST_SHORT_ADMIN_TOKEN"
	t.Setenv(envVar, "short-token") // < 32 chars

	s := &Store{cfg: BrokerConfig{AdminTokenEnvironmentVariable: envVar}}
	r := httptest.NewRequest(http.MethodGet, "/admin/servers", nil)
	r.Header.Set("Authorization", "Bearer short-token")
	if s.AdminAuthorized(r) {
		t.Errorf("AdminAuthorized() = true with a <32-char configured token, want false")
	}
}

func TestServerConfig_ReadyAndEffectiveKeys(t *testing.T) {
	const envVar = "CONCIERGE_TEST_TICKET_KEY"
	t.Setenv(envVar, "012345678901234567890123456789012") // 35 chars

	s := ServerConfig{
		Id:                                  "srv-1",
		TicketSigningKeyEnvironmentVariable: envVar,
		TransportPublicKey:                  "pubkey",
		Providers:                           []ProviderConfig{{Id: "p1", Issuer: "https://issuer.example", Audience: "aud", JwksUri: "https://issuer.example/jwks"}},
	}
	if !s.HasTicketSigningKey() {
		t.Errorf("HasTicketSigningKey() = false, want true (env var >=32 chars)")
	}
	if !s.HasTransportPublicKey() {
		t.Errorf("HasTransportPublicKey() = false, want true")
	}
	if !s.IsReady() {
		t.Errorf("IsReady() = false, want true")
	}

	// Removing the only provider must flip IsReady to false.
	s.Providers = nil
	if s.IsReady() {
		t.Errorf("IsReady() = true with no providers, want false")
	}
}

func TestProviderConfig_WebOnlyAndRoundTrip(t *testing.T) {
	provider := ProviderConfig{
		Id: "web", Issuer: "https://issuer.example", WebClientId: "web-client",
		WebClientSecret: "server-secret", TokenEndpoint: "https://issuer.example/token",
		JwksUri: "https://issuer.example/jwks",
	}
	if !provider.IsWebConfigured() || !provider.IsStructurallyValid() {
		t.Fatalf("web-only provider should be valid: %+v", provider)
	}
	copy := provider.Copy()
	if copy.WebClientId != provider.WebClientId || copy.WebClientSecret != provider.WebClientSecret || copy.TokenEndpoint != provider.TokenEndpoint {
		t.Fatalf("web fields lost by Copy: %+v", copy)
	}
}

func TestStore_WebProviderPersists(t *testing.T) {
	path := filepath.Join(t.TempDir(), "appsettings.json")
	s, err := Load(path)
	if err != nil {
		t.Fatal(err)
	}
	provider := ProviderConfig{Id: "web", Issuer: "https://issuer.example", WebClientId: "web-client", WebClientSecret: "server-secret", TokenEndpoint: "https://issuer.example/token", JwksUri: "https://issuer.example/jwks"}
	if ok, msg := s.SetOrganization(OrganizationConfig{Providers: []ProviderConfig{provider}}); !ok {
		t.Fatalf("SetOrganization: %s", msg)
	}
	reloaded, err := Load(path)
	if err != nil {
		t.Fatal(err)
	}
	org := reloaded.GetOrganization()
	if len(org.Providers) != 1 || org.Providers[0].WebClientId != "web-client" || org.Providers[0].WebClientSecret != "server-secret" || org.Providers[0].TokenEndpoint != provider.TokenEndpoint {
		t.Fatalf("reloaded web provider = %+v", org.Providers)
	}
}

func TestValidateBrowserEndpoints(t *testing.T) {
	cases := []struct {
		name, websocket, info string
		wantErr               bool
	}{
		{"empty", "", "", false},
		{"secure remote", "wss://game.example/basis", "https://game.example/server-info", false},
		{"loopback development", "ws://127.0.0.1:4297/basis", "http://localhost:4297/server-info", false},
		{"missing pair", "wss://game.example/basis", "", true},
		{"insecure remote websocket", "ws://game.example/basis", "https://game.example/server-info", true},
		{"fragment", "wss://game.example/basis#x", "https://game.example/server-info", true},
	}
	for _, tc := range cases {
		t.Run(tc.name, func(t *testing.T) {
			if err := ValidateBrowserEndpoints(tc.websocket, tc.info); (err != nil) != tc.wantErr {
				t.Fatalf("ValidateBrowserEndpoints() error = %v, wantErr %v", err, tc.wantErr)
			}
		})
	}
}

func TestValidateBrowserEndpointTemplates(t *testing.T) {
	cases := []struct {
		name, websocket, info string
		wantErr               bool
	}{
		{"empty", "", "", false},
		{"complete", "wss://{host}:{port}/basis", "https://{host}:{port}/server-info", false},
		{"missing pair", "wss://{host}:{port}/basis", "", true},
		{"missing placeholder", "wss://{host}/basis", "https://{host}:{port}/server-info", true},
		{"insecure remote", "ws://{host}:{port}/basis", "http://{host}:{port}/server-info", true},
	}
	for _, tc := range cases {
		t.Run(tc.name, func(t *testing.T) {
			if err := ValidateBrowserEndpointTemplates(tc.websocket, tc.info); (err != nil) != tc.wantErr {
				t.Fatalf("ValidateBrowserEndpointTemplates() error = %v, wantErr %v", err, tc.wantErr)
			}
		})
	}
}

func TestStore_UpdateBrowserEndpoints(t *testing.T) {
	s, err := Load(filepath.Join(t.TempDir(), "appsettings.json"))
	if err != nil {
		t.Fatalf("Load: %v", err)
	}
	if err := s.AddServer(ServerConfig{Id: "managed"}); err != nil {
		t.Fatalf("AddServer: %v", err)
	}
	if !s.UpdateBrowserEndpoints("managed", "wss://room.example/basis", "https://room.example/server-info") {
		t.Fatal("UpdateBrowserEndpoints() = false, want true")
	}
	server, ok := s.FindServer("managed")
	if !ok || server.WebSocketUri != "wss://room.example/basis" || server.ServerInfoUri != "https://room.example/server-info" {
		t.Fatalf("server endpoints = %+v, want persisted pair", server)
	}
	if s.UpdateBrowserEndpoints("managed", "wss://room.example/basis", "") {
		t.Fatal("UpdateBrowserEndpoints() accepted an incomplete pair")
	}
}

func TestStore_AddServer_DuplicateRejected(t *testing.T) {
	s, err := Load(filepath.Join(t.TempDir(), "appsettings.json"))
	if err != nil {
		t.Fatalf("Load: %v", err)
	}
	if err := s.AddServer(ServerConfig{Id: "srv-1"}); err != nil {
		t.Fatalf("AddServer: %v", err)
	}
	if err := s.AddServer(ServerConfig{Id: "srv-1"}); err != ErrDuplicateServerID {
		t.Errorf("AddServer duplicate: err = %v, want ErrDuplicateServerID", err)
	}
}

func TestStore_PersistenceRoundTrip(t *testing.T) {
	path := filepath.Join(t.TempDir(), "appsettings.json")
	s1, err := Load(path)
	if err != nil {
		t.Fatalf("Load: %v", err)
	}
	server := ServerConfig{
		Id:               "srv-1",
		Providers:        []ProviderConfig{{Id: "p1", Issuer: "https://issuer.example", Audience: "aud", JwksUri: "https://issuer.example/jwks"}},
		TicketSigningKey: "0123456789012345678901234567890123456789",
		WebSocketUri:     "wss://game.example/basis",
		ServerInfoUri:    "https://game.example/server-info",
	}
	if err := s1.AddServer(server); err != nil {
		t.Fatalf("AddServer: %v", err)
	}

	info, err := os.Stat(path)
	if err != nil {
		t.Fatalf("stat persisted file: %v", err)
	}
	if perm := info.Mode().Perm(); perm != 0o600 {
		t.Errorf("persisted file mode = %o, want 0600", perm)
	}

	s2, err := Load(path)
	if err != nil {
		t.Fatalf("reload: %v", err)
	}
	reloaded, ok := s2.FindServer("srv-1")
	if !ok {
		t.Fatalf("reloaded store: srv-1 not found")
	}
	if reloaded.Id != server.Id || len(reloaded.Providers) != 1 || reloaded.Providers[0].Id != "p1" || reloaded.WebSocketUri != server.WebSocketUri || reloaded.ServerInfoUri != server.ServerInfoUri {
		t.Errorf("reloaded server = %+v", reloaded)
	}
}

func TestStore_GetOrganization_FallsBackToLegacyServer(t *testing.T) {
	s, err := Load(filepath.Join(t.TempDir(), "appsettings.json"))
	if err != nil {
		t.Fatalf("Load: %v", err)
	}
	legacy := ServerConfig{
		Id: "legacy",
		Providers: []ProviderConfig{
			{Id: "google", Issuer: "https://accounts.google.com", Audience: "aud", JwksUri: "https://www.googleapis.com/oauth2/v3/certs"},
		},
	}
	if err := s.AddServer(legacy); err != nil {
		t.Fatalf("AddServer: %v", err)
	}
	org := s.GetOrganization()
	if len(org.Providers) != 1 || org.Providers[0].Id != "google" || org.DefaultProviderId != "google" {
		t.Errorf("GetOrganization() = %+v, want fallback to legacy server's provider", org)
	}
}
