// Command server runs concierge: the Go rewrite of the Basis SSO admission
// broker (docs/concierge/design.md). Phase 1 (this binary, currently) has
// no Kubernetes/Agones integration — see internal/kube.NoopProvisioner and
// docs/concierge/design.md §12 "決定事項" for what phase 2 adds.
package main

import (
	"log"
	"net/http"
	"net/url"
	"os"
	"path/filepath"
	"strconv"
	"strings"

	"github.com/sekaimate/sekaimate/concierge/internal/adminui"
	"github.com/sekaimate/sekaimate/concierge/internal/admission"
	"github.com/sekaimate/sekaimate/concierge/internal/api"
	"github.com/sekaimate/sekaimate/concierge/internal/config"
	"github.com/sekaimate/sekaimate/concierge/internal/controlplane"
	"github.com/sekaimate/sekaimate/concierge/internal/kube"
)

// defaultPath resolves name relative to the running binary's directory,
// matching the C# broker's {AppContext.BaseDirectory}/name default for
// BASIS_SSO_BROKER_CONFIG_PATH / BASIS_CONTROL_PLANE_STORE_PATH.
func defaultPath(name string) string {
	exe, err := os.Executable()
	if err != nil {
		return name
	}
	return filepath.Join(filepath.Dir(exe), name)
}

func envOrDefault(key, def string) string {
	if v := os.Getenv(key); v != "" {
		return v
	}
	return def
}

// listenAddr resolves the bind address. LISTEN_ADDR takes precedence; for
// smoother migration from the C# deployment it otherwise honors
// ASPNETCORE_URLS if that is set to a single http:// URL (e.g.
// "http://0.0.0.0:5080"), matching research-sso-broker.md §5's documented
// default; the broker's own default of :5080 applies if neither is set.
func listenAddr() string {
	if v := os.Getenv("LISTEN_ADDR"); v != "" {
		return v
	}
	if v := os.Getenv("ASPNETCORE_URLS"); v != "" {
		// ASPNETCORE_URLS may list multiple semicolon-separated bindings;
		// only the first is honored here since net/http.ListenAndServe
		// binds a single address.
		first := strings.SplitN(v, ";", 2)[0]
		if u, err := url.Parse(first); err == nil && u.Host != "" {
			host := u.Hostname()
			if host == "" || host == "+" || host == "*" {
				host = ""
			}
			return host + ":" + u.Port()
		}
	}
	return ":5080"
}

// bootstrapLocalMeeting mirrors the top-level bootstrap block in
// Program.cs: if a "local" server is registered, register/refresh a
// matching "local" meeting from BASIS_MEETING_PUBLIC_HOST/SetPort/Password.
// Kept per design.md §12's decision to preserve the Compose bootstrap path.
func bootstrapLocalMeeting(cfg *config.Store, meetings *controlplane.Store) {
	local, ok := cfg.FindServer("local")
	if !ok {
		return
	}
	host := strings.TrimSpace(os.Getenv("BASIS_MEETING_PUBLIC_HOST"))
	port := uint16(4296)
	if raw := os.Getenv("SetPort"); raw != "" {
		if parsed, err := strconv.ParseUint(raw, 10, 16); err == nil {
			port = uint16(parsed)
		}
	}
	password := os.Getenv("Password")
	meetings.EnsureSingleComposeMeeting("local", "Local meeting", host, port, password, local.EffectiveTransportPublicKey())
}

func main() {
	configPath := envOrDefault("BASIS_SSO_BROKER_CONFIG_PATH", defaultPath("appsettings.json"))
	cfg, err := config.Load(configPath)
	if err != nil {
		log.Fatalf("concierge: load config %s: %v", configPath, err)
	}

	controlPlanePath := envOrDefault("BASIS_CONTROL_PLANE_STORE_PATH", defaultPath("control-plane.json"))
	meetings := controlplane.NewStore(controlPlanePath)

	bootstrapLocalMeeting(cfg, meetings)

	deps := api.Deps{
		Config:      cfg,
		Meetings:    meetings,
		Enrollments: controlplane.NewEnrollmentStore(),
		Validator:   admission.NewValidator(),
		Provisioner: kube.NoopProvisioner{},
	}

	mux := api.NewMux(deps)
	adminui.Mount(mux, mux, os.Getenv("ADMIN_UI_DIR"))

	addr := listenAddr()
	log.Printf("concierge: listening on %s (config=%s, control-plane=%s)", addr, configPath, controlPlanePath)
	if err := http.ListenAndServe(addr, mux); err != nil {
		log.Fatal(err)
	}
}
