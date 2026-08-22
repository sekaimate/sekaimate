package api

import (
	"bytes"
	"context"
	"encoding/json"
	"net/http"
	"net/http/httptest"
	"os"
	"path/filepath"
	"strings"
	"testing"

	"github.com/sekaimate/sekaimate/concierge/internal/admission"
	"github.com/sekaimate/sekaimate/concierge/internal/config"
	"github.com/sekaimate/sekaimate/concierge/internal/controlplane"
	"github.com/sekaimate/sekaimate/concierge/internal/kube"
)

func newTestDeps(t *testing.T) Deps {
	t.Helper()
	dir := t.TempDir()
	cfg, err := config.Load(filepath.Join(dir, "appsettings.json"))
	if err != nil {
		t.Fatalf("config.Load: %v", err)
	}
	return Deps{
		Config:      cfg,
		Meetings:    controlplane.NewStore(filepath.Join(dir, "control-plane.json")),
		Enrollments: controlplane.NewEnrollmentStore(),
		Validator:   admission.NewValidator(),
		Provisioner: kube.NoopProvisioner{},
	}
}

// newTestDepsAdminBypassed is newTestDeps with AllowUnauthenticatedAdmin
// set, for tests that exercise admin-only business logic rather than
// AdminAuthorized itself (which TestAdmin_RequiresAuth covers directly).
func newTestDepsAdminBypassed(t *testing.T) Deps {
	t.Helper()
	dir := t.TempDir()
	cfgPath := filepath.Join(dir, "appsettings.json")
	if err := os.WriteFile(cfgPath, []byte(`{"Broker":{"AllowUnauthenticatedAdmin":true}}`), 0o644); err != nil {
		t.Fatalf("write config: %v", err)
	}
	cfg, err := config.Load(cfgPath)
	if err != nil {
		t.Fatalf("config.Load: %v", err)
	}
	return Deps{
		Config:      cfg,
		Meetings:    controlplane.NewStore(filepath.Join(dir, "control-plane.json")),
		Enrollments: controlplane.NewEnrollmentStore(),
		Validator:   admission.NewValidator(),
		Provisioner: kube.NoopProvisioner{},
	}
}

func doRequest(t *testing.T, mux *http.ServeMux, method, target string, body any, headers map[string]string) *httptest.ResponseRecorder {
	t.Helper()
	var reader *bytes.Reader
	if body != nil {
		b, err := json.Marshal(body)
		if err != nil {
			t.Fatalf("marshal body: %v", err)
		}
		reader = bytes.NewReader(b)
	} else {
		reader = bytes.NewReader(nil)
	}
	req := httptest.NewRequest(method, target, reader)
	for k, v := range headers {
		req.Header.Set(k, v)
	}
	rec := httptest.NewRecorder()
	mux.ServeHTTP(rec, req)
	return rec
}

func TestHealth_NotReadyWithNoServers(t *testing.T) {
	mux := NewMux(newTestDeps(t))
	rec := doRequest(t, mux, http.MethodGet, "/health", nil, nil)
	if rec.Code != http.StatusServiceUnavailable {
		t.Fatalf("status = %d, want %d, body=%s", rec.Code, http.StatusServiceUnavailable, rec.Body.String())
	}
	var resp HealthResponse
	if err := json.Unmarshal(rec.Body.Bytes(), &resp); err != nil {
		t.Fatalf("unmarshal: %v", err)
	}
	if resp.Status != "not_ready" {
		t.Errorf("status field = %q, want not_ready", resp.Status)
	}
}

func TestHealth_ReadyWhenServerReady(t *testing.T) {
	deps := newTestDeps(t)
	if err := deps.Config.AddServer(config.ServerConfig{
		Id:                 "srv-1",
		TicketSigningKey:   "0123456789012345678901234567890123456789",
		TransportPublicKey: "pubkey",
		Providers:          []config.ProviderConfig{{Id: "p1", Issuer: "https://issuer.example", Audience: "aud", JwksUri: "https://issuer.example/jwks"}},
	}); err != nil {
		t.Fatalf("AddServer: %v", err)
	}
	mux := NewMux(deps)
	rec := doRequest(t, mux, http.MethodGet, "/health", nil, nil)
	if rec.Code != http.StatusOK {
		t.Fatalf("status = %d, want 200, body=%s", rec.Code, rec.Body.String())
	}
}

func TestAdmin_RequiresAuth(t *testing.T) {
	deps := newTestDeps(t)
	mux := NewMux(deps)

	rec := doRequest(t, mux, http.MethodGet, "/admin/servers", nil, nil)
	if rec.Code != http.StatusUnauthorized {
		t.Fatalf("no token: status = %d, want 401", rec.Code)
	}
	if rec.Body.Len() != 0 {
		t.Errorf("401 body = %q, want empty (Results.Unauthorized() has no body)", rec.Body.String())
	}

	const envVar = "CONCIERGE_TEST_ADMIN_TOKEN_HANDLER"
	const token = "01234567890123456789012345678901" // 34 chars
	t.Setenv(envVar, token)

	// AdminTokenEnvironmentVariable can only be set via the config file in
	// this Store API surface: reload from a config carrying it.
	cfgPath := filepath.Join(t.TempDir(), "appsettings.json")
	raw := []byte(`{"Broker":{"AdminTokenEnvironmentVariable":"` + envVar + `"}}`)
	if err := os.WriteFile(cfgPath, raw, 0o644); err != nil {
		t.Fatalf("write config: %v", err)
	}
	cfg2, err := config.Load(cfgPath)
	if err != nil {
		t.Fatalf("config.Load: %v", err)
	}
	deps.Config = cfg2
	mux = NewMux(deps)

	rec = doRequest(t, mux, http.MethodGet, "/admin/servers", nil, map[string]string{"Authorization": "Bearer " + token})
	if rec.Code != http.StatusOK {
		t.Fatalf("with token: status = %d, want 200, body=%s", rec.Code, rec.Body.String())
	}
}

func TestPutAdminServer_PersistsBrowserEndpoints(t *testing.T) {
	deps := newTestDepsAdminBypassed(t)
	mux := NewMux(deps)
	key := "01234567890123456789012345678901"
	pub := "transport-public-key"
	ws := "wss://game.example.com:4297/basis"
	info := "https://game.example.com:4297/server-info"
	id := "static-web"
	providerID, issuer, audience, jwks := "p", "https://issuer.example", "aud", "https://issuer.example/jwks"
	providers := []ProviderOptions{{Id: &providerID, Issuer: &issuer, Audience: &audience, JwksUri: &jwks}}
	rec := doRequest(t, mux, http.MethodPut, "/admin/servers/"+id, AdminServerWrite{
		TicketSigningKey: &key, TransportPublicKey: &pub, WebSocketUri: &ws, ServerInfoUri: &info,
		Providers: &providers,
	}, nil)
	if rec.Code != http.StatusNoContent {
		t.Fatalf("put: status = %d, body=%s", rec.Code, rec.Body.String())
	}
	rec = doRequest(t, mux, http.MethodGet, "/admin/servers", nil, nil)
	if rec.Code != http.StatusOK {
		t.Fatalf("list: status = %d, body=%s", rec.Code, rec.Body.String())
	}
	var servers []AdminServerInfo
	if err := json.Unmarshal(rec.Body.Bytes(), &servers); err != nil {
		t.Fatalf("unmarshal: %v", err)
	}
	if len(servers) != 1 || servers[0].WebSocketUri == nil || *servers[0].WebSocketUri != ws || servers[0].ServerInfoUri == nil || *servers[0].ServerInfoUri != info {
		t.Fatalf("static browser endpoints = %+v", servers)
	}
}

func TestAdmission_UnknownServer(t *testing.T) {
	mux := NewMux(newTestDeps(t))
	rec := doRequest(t, mux, http.MethodPost, "/admission/unknown-server", AdmissionRequest{IdToken: "x", Did: "did:key:z6Mk"}, nil)
	if rec.Code != http.StatusNotFound {
		t.Fatalf("status = %d, want 404, body=%s", rec.Code, rec.Body.String())
	}
	if got := rec.Header().Get("Cache-Control"); got != "no-store" {
		t.Errorf("Cache-Control = %q, want no-store (must be set even on 404)", got)
	}
}

func TestAdmission_MissingFields(t *testing.T) {
	mux := NewMux(newTestDeps(t))
	rec := doRequest(t, mux, http.MethodPost, "/admission/srv-1", map[string]any{"idToken": ""}, nil)
	if rec.Code != http.StatusBadRequest {
		t.Fatalf("status = %d, want 400, body=%s", rec.Code, rec.Body.String())
	}
	var errResp ErrorResponse
	if err := json.Unmarshal(rec.Body.Bytes(), &errResp); err != nil {
		t.Fatalf("unmarshal: %v", err)
	}
	if errResp.Error == "" {
		t.Errorf("error message is empty")
	}
}

func TestAdmission_ServerNotReady(t *testing.T) {
	deps := newTestDeps(t)
	if err := deps.Config.AddServer(config.ServerConfig{Id: "srv-1"}); err != nil {
		t.Fatalf("AddServer: %v", err)
	}
	mux := NewMux(deps)
	rec := doRequest(t, mux, http.MethodPost, "/admission/srv-1", AdmissionRequest{IdToken: "x", Did: "did:key:z6Mk"}, nil)
	if rec.Code != http.StatusServiceUnavailable {
		t.Fatalf("status = %d, want 503, body=%s", rec.Code, rec.Body.String())
	}
	if ct := rec.Header().Get("Content-Type"); ct != "application/problem+json" {
		t.Errorf("Content-Type = %q, want application/problem+json", ct)
	}
}

func TestCreateMeeting_NotEnabled(t *testing.T) {
	mux := NewMux(newTestDepsAdminBypassed(t))
	rec := doRequest(t, mux, http.MethodPost, "/admin/meetings", CreateMeetingRequest{Title: "Room"}, nil)
	if rec.Code != http.StatusNotImplemented {
		t.Fatalf("status = %d, want 501, body=%s", rec.Code, rec.Body.String())
	}
	if ct := rec.Header().Get("Content-Type"); ct != "application/problem+json" {
		t.Errorf("Content-Type = %q, want application/problem+json", ct)
	}
}

func TestCreateMeeting_FullFlow(t *testing.T) {
	t.Setenv("BASIS_CONTROL_PLANE_ALLOW_MANUAL_MEETINGS", "true")
	deps := newTestDepsAdminBypassed(t)
	if ok, msg := deps.Config.SetOrganization(config.OrganizationConfig{
		Providers: []config.ProviderConfig{{Id: "p", Issuer: "https://issuer.example", Audience: "aud", JwksUri: "https://issuer.example/jwks"}},
	}); !ok {
		t.Fatalf("SetOrganization: %s", msg)
	}
	mux := NewMux(deps)

	rec := doRequest(t, mux, http.MethodPost, "/admin/meetings", CreateMeetingRequest{Title: "My Room"}, nil)
	if rec.Code != http.StatusCreated {
		t.Fatalf("create: status = %d, want 201, body=%s", rec.Code, rec.Body.String())
	}
	var view MeetingView
	if err := json.Unmarshal(rec.Body.Bytes(), &view); err != nil {
		t.Fatalf("unmarshal: %v", err)
	}
	if view.Id == "" || view.Status != "provisioning" {
		t.Fatalf("created meeting = %+v, want non-empty id and provisioning status", view)
	}
	if loc := rec.Header().Get("Location"); loc != "/admin/meetings/"+view.Id {
		t.Errorf("Location = %q, want /admin/meetings/%s", loc, view.Id)
	}

	// The matching server registry entry must exist and be ready.
	server, ok := deps.Config.FindServer(view.Id)
	if !ok {
		t.Fatalf("no matching server registry entry for meeting %s", view.Id)
	}
	if !server.IsReady() {
		t.Errorf("server for created meeting is not ready: %+v", server)
	}

	// Listing must include it.
	rec = doRequest(t, mux, http.MethodGet, "/admin/meetings", nil, nil)
	var list []MeetingView
	if err := json.Unmarshal(rec.Body.Bytes(), &list); err != nil {
		t.Fatalf("unmarshal list: %v", err)
	}
	found := false
	for _, m := range list {
		if m.Id == view.Id {
			found = true
		}
	}
	if !found {
		t.Errorf("created meeting %s missing from GET /admin/meetings", view.Id)
	}

	// Delete: server registry entry must be removed too.
	rec = doRequest(t, mux, http.MethodDelete, "/admin/meetings/"+view.Id, nil, nil)
	if rec.Code != http.StatusNoContent {
		t.Fatalf("delete: status = %d, want 204, body=%s", rec.Code, rec.Body.String())
	}
	if _, ok := deps.Config.FindServer(view.Id); ok {
		t.Errorf("server registry entry for %s still present after meeting delete", view.Id)
	}
	rec = doRequest(t, mux, http.MethodDelete, "/admin/meetings/"+view.Id, nil, nil)
	if rec.Code != http.StatusNotFound {
		t.Fatalf("second delete: status = %d, want 404", rec.Code)
	}
}

// TestCreateMeeting_TimestampsNonZero guards against a wire-compatibility
// regression: the 201 response body must carry real CreatedAt/UpdatedAt
// timestamps, matching the C# broker's MeetingRecord constructor
// (ControlPlane.cs), which sets both fields at construction rather than
// leaving them for a later persistence step. A prior version of the handler
// rendered the response from a local MeetingRecord built before
// Store.Add assigned timestamps, so the 201 body always showed the Go zero
// time even though the record GET returned afterward was correct.
func TestCreateMeeting_TimestampsNonZero(t *testing.T) {
	t.Setenv("BASIS_CONTROL_PLANE_ALLOW_MANUAL_MEETINGS", "true")
	deps := newTestDepsAdminBypassed(t)
	if ok, msg := deps.Config.SetOrganization(config.OrganizationConfig{
		Providers: []config.ProviderConfig{{Id: "p", Issuer: "https://issuer.example", Audience: "aud", JwksUri: "https://issuer.example/jwks"}},
	}); !ok {
		t.Fatalf("SetOrganization: %s", msg)
	}
	mux := NewMux(deps)

	rec := doRequest(t, mux, http.MethodPost, "/admin/meetings", CreateMeetingRequest{Title: "Timestamped Room"}, nil)
	if rec.Code != http.StatusCreated {
		t.Fatalf("create: status = %d, want 201, body=%s", rec.Code, rec.Body.String())
	}
	var created MeetingView
	if err := json.Unmarshal(rec.Body.Bytes(), &created); err != nil {
		t.Fatalf("unmarshal: %v", err)
	}
	if created.CreatedAt.IsZero() {
		t.Errorf("201 body CreatedAt is zero, want a real timestamp")
	}
	if created.UpdatedAt.IsZero() {
		t.Errorf("201 body UpdatedAt is zero, want a real timestamp")
	}

	rec = doRequest(t, mux, http.MethodGet, "/admin/meetings", nil, nil)
	if rec.Code != http.StatusOK {
		t.Fatalf("list: status = %d, want 200, body=%s", rec.Code, rec.Body.String())
	}
	var list []MeetingView
	if err := json.Unmarshal(rec.Body.Bytes(), &list); err != nil {
		t.Fatalf("unmarshal list: %v", err)
	}
	var found *MeetingView
	for i := range list {
		if list[i].Id == created.Id {
			found = &list[i]
		}
	}
	if found == nil {
		t.Fatalf("created meeting %s missing from GET /admin/meetings", created.Id)
	}
	if !found.CreatedAt.Equal(created.CreatedAt) {
		t.Errorf("GET CreatedAt = %v, want %v (from POST response)", found.CreatedAt, created.CreatedAt)
	}
	if !found.UpdatedAt.Equal(created.UpdatedAt) {
		t.Errorf("GET UpdatedAt = %v, want %v (from POST response)", found.UpdatedAt, created.UpdatedAt)
	}
}

// countingProvisioner is a kube.RoomProvisioner test double that records how
// many times Create/Delete were called and with which meeting ids, so tests
// can assert provisioning was (or was not) attempted.
type countingProvisioner struct {
	createIDs []string
	deleteIDs []string
	createErr error
}

func (p *countingProvisioner) Create(_ context.Context, meetingID string, _ kube.RoomKeys) error {
	p.createIDs = append(p.createIDs, meetingID)
	return p.createErr
}

func (p *countingProvisioner) Delete(_ context.Context, meetingID string) error {
	p.deleteIDs = append(p.deleteIDs, meetingID)
	return nil
}

// TestCreateMeeting_ExplicitHost_SkipsProvisioning checks that supplying an
// explicit host at creation time (an externally-run server, per design.md
// §4.2) does not call RoomProvisioner.Create and the meeting is immediately
// "ready", matching the C# broker's semantics for statically-hosted rooms.
func TestCreateMeeting_ExplicitHost_SkipsProvisioning(t *testing.T) {
	t.Setenv("BASIS_CONTROL_PLANE_ALLOW_MANUAL_MEETINGS", "true")
	deps := newTestDepsAdminBypassed(t)
	if ok, msg := deps.Config.SetOrganization(config.OrganizationConfig{
		Providers: []config.ProviderConfig{{Id: "p", Issuer: "https://issuer.example", Audience: "aud", JwksUri: "https://issuer.example/jwks"}},
	}); !ok {
		t.Fatalf("SetOrganization: %s", msg)
	}
	provisioner := &countingProvisioner{}
	deps.Provisioner = provisioner
	mux := NewMux(deps)

	host := "game.example.com"
	rec := doRequest(t, mux, http.MethodPost, "/admin/meetings", CreateMeetingRequest{Title: "External Room", Host: &host}, nil)
	if rec.Code != http.StatusCreated {
		t.Fatalf("create: status = %d, want 201, body=%s", rec.Code, rec.Body.String())
	}
	var view MeetingView
	if err := json.Unmarshal(rec.Body.Bytes(), &view); err != nil {
		t.Fatalf("unmarshal: %v", err)
	}
	if view.Status != "ready" {
		t.Errorf("status = %q, want ready", view.Status)
	}
	if view.Host != host {
		t.Errorf("host = %q, want %q", view.Host, host)
	}
	if len(provisioner.createIDs) != 0 {
		t.Errorf("Provisioner.Create called %d times, want 0 for an explicit-host meeting", len(provisioner.createIDs))
	}

	stored, ok := deps.Meetings.Find(view.Id)
	if !ok {
		t.Fatalf("meeting %s not found in store", view.Id)
	}
	if stored.Managed {
		t.Errorf("stored meeting Managed = true, want false for an explicit-host meeting")
	}

	// Delete must not call Provisioner.Delete for an unmanaged meeting.
	rec = doRequest(t, mux, http.MethodDelete, "/admin/meetings/"+view.Id, nil, nil)
	if rec.Code != http.StatusNoContent {
		t.Fatalf("delete: status = %d, want 204, body=%s", rec.Code, rec.Body.String())
	}
	if len(provisioner.deleteIDs) != 0 {
		t.Errorf("Provisioner.Delete called %d times, want 0 for an explicit-host meeting", len(provisioner.deleteIDs))
	}
}

func TestCreateMeeting_BrowserEndpointsFlowToManifestAndDeepLink(t *testing.T) {
	t.Setenv("BASIS_CONTROL_PLANE_ALLOW_MANUAL_MEETINGS", "true")
	deps := newTestDepsAdminBypassed(t)
	if ok, msg := deps.Config.SetOrganization(config.OrganizationConfig{
		Providers: []config.ProviderConfig{{Id: "p", Issuer: "https://issuer.example", Audience: "aud", JwksUri: "https://issuer.example/jwks"}},
	}); !ok {
		t.Fatalf("SetOrganization: %s", msg)
	}
	mux := NewMux(deps)
	host := "game.example.com"
	ws := "wss://game.example.com:4297/basis"
	info := "https://game.example.com:4297/server-info"
	rec := doRequest(t, mux, http.MethodPost, "/admin/meetings", CreateMeetingRequest{
		Title: "Browser Room", Host: &host, WebSocketUri: &ws, ServerInfoUri: &info,
	}, nil)
	if rec.Code != http.StatusCreated {
		t.Fatalf("create: status = %d, body=%s", rec.Code, rec.Body.String())
	}
	var view MeetingView
	if err := json.Unmarshal(rec.Body.Bytes(), &view); err != nil {
		t.Fatalf("unmarshal view: %v", err)
	}
	if view.WebSocketUri == nil || *view.WebSocketUri != ws || view.ServerInfoUri == nil || *view.ServerInfoUri != info {
		t.Fatalf("view browser endpoints = %v / %v", view.WebSocketUri, view.ServerInfoUri)
	}
	rec = doRequest(t, mux, http.MethodGet, "/join/"+mustMeetingInvite(t, deps, view.Id)+"/manifest", nil, nil)
	if rec.Code != http.StatusOK {
		t.Fatalf("manifest: status = %d, body=%s", rec.Code, rec.Body.String())
	}
	var manifest JoinManifest
	if err := json.Unmarshal(rec.Body.Bytes(), &manifest); err != nil {
		t.Fatalf("unmarshal manifest: %v", err)
	}
	if manifest.Connection == nil || manifest.Connection.WebSocketUri == nil || *manifest.Connection.WebSocketUri != ws {
		t.Fatalf("manifest connection = %+v", manifest.Connection)
	}
	if !strings.Contains(view.JoinUrl, "/join/") {
		t.Fatalf("join url = %q", view.JoinUrl)
	}
	page := doRequest(t, mux, http.MethodGet, view.JoinUrl, nil, nil)
	if page.Code != http.StatusOK || !strings.Contains(page.Body.String(), "websocketUri") {
		t.Fatalf("join page: status=%d body=%s", page.Code, page.Body.String())
	}
}

func TestJoin_UsesStaticServerBrowserEndpointsForLegacyMeeting(t *testing.T) {
	deps := newTestDeps(t)
	ws := "wss://legacy.example.com:4297/basis"
	info := "https://legacy.example.com:4297/server-info"
	provider := config.ProviderConfig{Id: "p", Issuer: "https://issuer.example", Audience: "aud", JwksUri: "https://issuer.example/jwks"}
	if err := deps.Config.AddServer(config.ServerConfig{
		Id: "legacy", TransportPublicKey: "legacy-public", TicketSigningKey: strings.Repeat("k", 32),
		WebSocketUri: ws, ServerInfoUri: info, Providers: []config.ProviderConfig{provider},
	}); err != nil {
		t.Fatalf("add static server: %v", err)
	}
	meeting := controlplane.MeetingRecord{
		Id: "legacy", Title: "Legacy room", Status: "ready", Host: "game.example.com", Port: 4296,
		Password: "secret", InviteToken: "legacy-invite", TransportPublicKey: "legacy-public",
	}
	if err := deps.Meetings.Add(meeting); err != nil {
		t.Fatalf("add meeting: %v", err)
	}
	mux := NewMux(deps)

	rec := doRequest(t, mux, http.MethodGet, "/join/legacy-invite/config", nil, nil)
	if rec.Code != http.StatusOK || !strings.Contains(rec.Body.String(), ws) || !strings.Contains(rec.Body.String(), info) {
		t.Fatalf("join config: status=%d body=%s", rec.Code, rec.Body.String())
	}
	rec = doRequest(t, mux, http.MethodGet, "/join/legacy-invite/manifest", nil, nil)
	var manifest JoinManifest
	if rec.Code != http.StatusOK || json.Unmarshal(rec.Body.Bytes(), &manifest) != nil {
		t.Fatalf("join manifest: status=%d body=%s", rec.Code, rec.Body.String())
	}
	if manifest.Connection == nil || manifest.Connection.WebSocketUri == nil || *manifest.Connection.WebSocketUri != ws || manifest.Connection.ServerInfoUri == nil || *manifest.Connection.ServerInfoUri != info {
		t.Fatalf("manifest browser endpoints = %+v", manifest.Connection)
	}
	rec = doRequest(t, mux, http.MethodGet, "/join/legacy-invite", nil, nil)
	if rec.Code != http.StatusOK || !strings.Contains(rec.Body.String(), "websocketUri") || !strings.Contains(rec.Body.String(), escapeDataString(ws)) {
		t.Fatalf("join page: status=%d body=%s", rec.Code, rec.Body.String())
	}
}

func mustMeetingInvite(t *testing.T, deps Deps, id string) string {
	t.Helper()
	meeting, ok := deps.Meetings.Find(id)
	if !ok {
		t.Fatalf("meeting %s not found", id)
	}
	return meeting.InviteToken
}

// TestCreateMeeting_NoHost_ProvisionsAndDeletes checks that omitting host
// (concierge-managed room) calls RoomProvisioner.Create at creation and
// RoomProvisioner.Delete at deletion, and that the stored record is Managed.
func TestCreateMeeting_NoHost_ProvisionsAndDeletes(t *testing.T) {
	t.Setenv("BASIS_CONTROL_PLANE_ALLOW_MANUAL_MEETINGS", "true")
	deps := newTestDepsAdminBypassed(t)
	if ok, msg := deps.Config.SetOrganization(config.OrganizationConfig{
		Providers: []config.ProviderConfig{{Id: "p", Issuer: "https://issuer.example", Audience: "aud", JwksUri: "https://issuer.example/jwks"}},
	}); !ok {
		t.Fatalf("SetOrganization: %s", msg)
	}
	provisioner := &countingProvisioner{}
	deps.Provisioner = provisioner
	mux := NewMux(deps)

	rec := doRequest(t, mux, http.MethodPost, "/admin/meetings", CreateMeetingRequest{Title: "Managed Room"}, nil)
	if rec.Code != http.StatusCreated {
		t.Fatalf("create: status = %d, want 201, body=%s", rec.Code, rec.Body.String())
	}
	var view MeetingView
	if err := json.Unmarshal(rec.Body.Bytes(), &view); err != nil {
		t.Fatalf("unmarshal: %v", err)
	}
	if view.Status != "provisioning" {
		t.Errorf("status = %q, want provisioning", view.Status)
	}
	if len(provisioner.createIDs) != 1 || provisioner.createIDs[0] != view.Id {
		t.Errorf("Provisioner.Create calls = %v, want exactly [%s]", provisioner.createIDs, view.Id)
	}

	stored, ok := deps.Meetings.Find(view.Id)
	if !ok {
		t.Fatalf("meeting %s not found in store", view.Id)
	}
	if !stored.Managed {
		t.Errorf("stored meeting Managed = false, want true for a concierge-provisioned meeting")
	}

	rec = doRequest(t, mux, http.MethodDelete, "/admin/meetings/"+view.Id, nil, nil)
	if rec.Code != http.StatusNoContent {
		t.Fatalf("delete: status = %d, want 204, body=%s", rec.Code, rec.Body.String())
	}
	if len(provisioner.deleteIDs) != 1 || provisioner.deleteIDs[0] != view.Id {
		t.Errorf("Provisioner.Delete calls = %v, want exactly [%s]", provisioner.deleteIDs, view.Id)
	}
}

func TestPutOrganization_InvalidBody(t *testing.T) {
	deps := newTestDepsAdminBypassed(t)
	mux := NewMux(deps)
	body := OrganizationOptions{} // no providers: structurally invalid
	rec := doRequest(t, mux, http.MethodPut, "/admin/organization", body, nil)
	if rec.Code != http.StatusBadRequest {
		t.Fatalf("status = %d, want 400, body=%s", rec.Code, rec.Body.String())
	}
}

func TestJoinManifest_UnknownToken(t *testing.T) {
	mux := NewMux(newTestDeps(t))
	rec := doRequest(t, mux, http.MethodGet, "/join/does-not-exist/manifest", nil, nil)
	if rec.Code != http.StatusNotFound {
		t.Fatalf("status = %d, want 404", rec.Code)
	}
}
