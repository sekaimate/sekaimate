package api

import (
	"bytes"
	"encoding/json"
	"net/http"
	"net/http/httptest"
	"os"
	"path/filepath"
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
