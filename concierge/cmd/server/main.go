// Command server runs concierge: the Go rewrite of the Basis SSO admission
// broker (docs/concierge/design.md), including Kubernetes/Agones-backed
// meeting provisioning when a Kubernetes config is available. See
// buildProvisioner and internal/kube.Manager for the Agones integration;
// deployments with no Kubernetes config fall back to
// internal/kube.NoopProvisioner and behave exactly as in phase 1.
package main

import (
	"context"
	"fmt"
	"log"
	"net/http"
	"net/url"
	"os"
	"path/filepath"
	"strconv"
	"strings"
	"time"

	agonesclient "agones.dev/agones/pkg/client/clientset/versioned"
	"k8s.io/client-go/kubernetes"

	"github.com/sekaimate/sekaimate/concierge/internal/adminui"
	"github.com/sekaimate/sekaimate/concierge/internal/admission"
	"github.com/sekaimate/sekaimate/concierge/internal/api"
	"github.com/sekaimate/sekaimate/concierge/internal/config"
	"github.com/sekaimate/sekaimate/concierge/internal/controlplane"
	"github.com/sekaimate/sekaimate/concierge/internal/kube"
)

// defaultBasisServerPort is the Basis Server UDP game port
// (Configuration.SetPort's own default), used both as the GameServer's
// requested container port and injected back into the container as SetPort
// when Kubernetes provisioning is enabled (see buildProvisioner).
const defaultBasisServerPort = 4296

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

// checkNoStaticMeetingIDCollision implements design.md §4.1's startup
// requirement: a *hand-authored* static Servers[] entry and a control-plane
// MeetingRecord must never share an id (concierge would not know which one
// is authoritative for that id's admission routing). It is not a collision
// when the Servers[] entry has FromMeeting set: every meeting concierge
// creates deliberately registers matching entries in both stores by design
// (internal/api.CreateMeeting adds both together, and NewID already avoids
// colliding with anything in either store at creation time), so that
// pairing is normal for any currently-existing meeting, not an error. The
// other sanctioned exception is "local": bootstrapLocalMeeting deliberately
// registers a MeetingRecord with the same id as the "local" static server
// entry it reads from (design.md §12 decision 1).
func checkNoStaticMeetingIDCollision(cfg *config.Store, meetings *controlplane.Store) error {
	for _, m := range meetings.List() {
		if m.Id == "local" {
			continue
		}
		if server, exists := cfg.FindServer(m.Id); exists && !server.FromMeeting {
			return fmt.Errorf("meeting id %q is registered both as a static Servers[] entry and as a control-plane meeting", m.Id)
		}
	}
	return nil
}

// buildProvisioner wires up the Agones-backed kube.Manager when a
// Kubernetes config is available (in-cluster, or $KUBECONFIG/
// $HOME/.kube/config), otherwise falls back to kube.NoopProvisioner so
// non-Kubernetes deployments keep working exactly as in phase 1
// (docs/concierge/design.md §12 decision 2/3 only apply once Kubernetes
// integration is actually enabled).
func buildProvisioner(meetings *controlplane.Store) kube.RoomProvisioner {
	restCfg, ok, err := kube.ResolveRESTConfig()
	if err != nil {
		log.Fatalf("concierge: resolve kubeconfig: %v", err)
	}
	if !ok {
		log.Printf("concierge: no Kubernetes config found (in-cluster or $KUBECONFIG/$HOME/.kube/config); Kubernetes meeting provisioning is disabled")
		return kube.NoopProvisioner{}
	}

	agonesClientset, err := agonesclient.NewForConfig(restCfg)
	if err != nil {
		log.Fatalf("concierge: create agones client: %v", err)
	}
	coreClientset, err := kubernetes.NewForConfig(restCfg)
	if err != nil {
		log.Fatalf("concierge: create kubernetes client: %v", err)
	}

	namespace := envOrDefault("NAMESPACE", "basis")
	readyTimeout := 120 * time.Second
	if raw := os.Getenv("GAMESERVER_READY_TIMEOUT_SECONDS"); raw != "" {
		if seconds, err := strconv.Atoi(raw); err == nil && seconds > 0 {
			readyTimeout = time.Duration(seconds) * time.Second
		} else {
			log.Printf("concierge: ignoring invalid GAMESERVER_READY_TIMEOUT_SECONDS=%q", raw)
		}
	}
	containerPort := int32(defaultBasisServerPort)
	if raw := os.Getenv("BASIS_SERVER_PORT"); raw != "" {
		if parsed, err := strconv.ParseUint(raw, 10, 16); err == nil {
			containerPort = int32(parsed)
		} else {
			log.Printf("concierge: ignoring invalid BASIS_SERVER_PORT=%q", raw)
		}
	}

	manager := kube.NewManager(agonesClientset, coreClientset, meetings, kube.ManagerConfig{
		Namespace:     namespace,
		Image:         envOrDefault("BASIS_SERVER_IMAGE", ""),
		ContainerPort: containerPort,
		ReadyTimeout:  readyTimeout,
	})

	if err := manager.Reconcile(context.Background()); err != nil {
		log.Printf("concierge: startup Kubernetes reconciliation failed (continuing): %v", err)
	}

	log.Printf("concierge: Kubernetes meeting provisioning enabled (namespace=%s)", namespace)
	return manager
}

func main() {
	configPath := envOrDefault("BASIS_SSO_BROKER_CONFIG_PATH", defaultPath("appsettings.json"))
	cfg, err := config.Load(configPath)
	if err != nil {
		log.Fatalf("concierge: load config %s: %v", configPath, err)
	}

	controlPlanePath := envOrDefault("BASIS_CONTROL_PLANE_STORE_PATH", defaultPath("control-plane.json"))
	meetings := controlplane.NewStore(controlPlanePath)

	if err := checkNoStaticMeetingIDCollision(cfg, meetings); err != nil {
		log.Fatalf("concierge: %v", err)
	}

	bootstrapLocalMeeting(cfg, meetings)

	deps := api.Deps{
		Config:      cfg,
		Meetings:    meetings,
		Enrollments: controlplane.NewEnrollmentStore(),
		Validator:   admission.NewValidator(),
		Provisioner: buildProvisioner(meetings),
	}

	mux := api.NewMux(deps)
	adminui.Mount(mux, mux, os.Getenv("ADMIN_UI_DIR"))

	addr := listenAddr()
	log.Printf("concierge: listening on %s (config=%s, control-plane=%s)", addr, configPath, controlPlanePath)
	if err := http.ListenAndServe(addr, mux); err != nil {
		log.Fatal(err)
	}
}
