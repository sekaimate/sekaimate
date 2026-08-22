// Package kube provides the Agones-backed RoomProvisioner (Manager) that
// creates/destroys the Kubernetes Secret + Agones GameServer pair backing a
// concierge-managed meeting, per docs/concierge/design.md §5. It mirrors the
// approach of basis-k8s's internal/kube.Manager (direct, synchronous calls
// against the Agones typed clientset; no controller-runtime, no
// informers/reconcile loop), extended with the per-meeting SSO Secret that
// basis-k8s does not need.
package kube

import (
	"context"
	"errors"
	"fmt"
	"log"
	"strconv"
	"strings"
	"time"

	agonesv1 "agones.dev/agones/pkg/apis/agones/v1"
	agonesclient "agones.dev/agones/pkg/client/clientset/versioned"
	corev1 "k8s.io/api/core/v1"
	apierrors "k8s.io/apimachinery/pkg/api/errors"
	metav1 "k8s.io/apimachinery/pkg/apis/meta/v1"

	"github.com/sekaimate/sekaimate/concierge/internal/config"
	"github.com/sekaimate/sekaimate/concierge/internal/controlplane"
	"k8s.io/client-go/kubernetes"
)

const (
	appLabelKey      = "app"
	appLabelValue    = "basis-server"
	instanceLabelKey = "instance"

	gameContainerName = "basis-server"

	readyContainerName  = "agones-ready"
	readyContainerImage = "curlimages/curl:latest"

	// readyContainerScript retries POST /ready until it succeeds (the
	// GameServer only becomes Ready once the Agones SDK sidecar receives
	// this call), then keeps sending POST /health every 2 seconds. Basis
	// Server does not integrate the Agones SDK itself, so this sidecar
	// drives the SDK HTTP gateway (port 9358) on its behalf — identical to
	// basis-k8s's internal/kube.Manager (see the reference implementation
	// this package ports from).
	readyContainerScript = `until curl -sf -X POST -H "Content-Type: application/json" -d "{}" http://localhost:9358/ready; do sleep 1; done
while true; do curl -sf -X POST -H "Content-Type: application/json" -d "{}" http://localhost:9358/health; sleep 2; done`

	defaultImage                  = "basis-server:latest"
	defaultContainerPort          = int32(4296)
	defaultWebSocketContainerPort = int32(4297)
	defaultReadyTimeout           = 120 * time.Second
	defaultPollInterval           = 2 * time.Second
)

// ErrAlreadyExists is returned by Create when the Secret or GameServer for
// meetingID already exists.
var ErrAlreadyExists = errors.New("kube: instance already exists")

// RoomKeys is re-exported for readability at call sites; RoomKeys itself is
// declared in provisioner.go.

// ManagerConfig holds the Manager's tunables. Zero values fall back to the
// documented defaults (see docs/concierge/design.md §8 / implementation.md
// phase-2 section).
type ManagerConfig struct {
	// Namespace is the Kubernetes namespace GameServers/Secrets are created
	// in. Required; NewManager panics if empty (a namespace-less Manager is
	// always a caller bug, not a runtime condition).
	Namespace string
	// Image is the basis-server container image. Defaults to
	// "basis-server:latest".
	Image string
	// ContainerPort is the UDP port the basis-server container listens on
	// and the GameServer requests as a Dynamic port. Defaults to 4296
	// (Configuration.SetPort's own default), and is also injected into the
	// container as the SetPort environment variable so the two never drift
	// apart.
	ContainerPort int32
	// WebSocketEnabled adds a named dynamic TCP port and injects the
	// web-support Configuration fields. It defaults false for UDP-only
	// backwards compatibility.
	WebSocketEnabled        bool
	WebSocketContainerPort  int32
	WebSocketPath           string
	ServerInfoPath          string
	WebSocketUseTLS         bool
	WebSocketAllowedOrigins []string
	WebSocketUriTemplate    string
	ServerInfoUriTemplate   string
	// ReadyTimeout bounds how long Create's background watch waits for the
	// GameServer to become Ready before marking the meeting failed.
	// Defaults to 120s (design.md §12 decision 3).
	ReadyTimeout time.Duration
	// PollInterval is how often the background watch polls GameServer
	// status. Defaults to 2s.
	PollInterval time.Duration
}

func (c ManagerConfig) withDefaults() ManagerConfig {
	if c.Image == "" {
		c.Image = defaultImage
	}
	if c.ContainerPort == 0 {
		c.ContainerPort = defaultContainerPort
	}
	if c.WebSocketContainerPort == 0 {
		c.WebSocketContainerPort = defaultWebSocketContainerPort
	}
	if c.WebSocketPath == "" {
		c.WebSocketPath = "/basis"
	}
	if c.ServerInfoPath == "" {
		c.ServerInfoPath = "/server-info"
	}
	if c.ReadyTimeout == 0 {
		c.ReadyTimeout = defaultReadyTimeout
	}
	if c.PollInterval == 0 {
		c.PollInterval = defaultPollInterval
	}
	return c
}

// Manager is the Agones-backed RoomProvisioner. It implements
// kube.RoomProvisioner (see provisioner.go); construct it with NewManager.
type Manager struct {
	agones   agonesclient.Interface
	core     kubernetes.Interface
	meetings *controlplane.Store
	servers  *config.Store
	cfg      ManagerConfig
}

var _ RoomProvisioner = (*Manager)(nil)

// NewManager builds a Manager. agones and core may be fake clientsets in
// tests. meetings receives the outcome of the background Ready-wait
// triggered by Create (see watchReady) via UpdateStatus; it may be nil in
// tests that only exercise Create/Delete/Reconcile's synchronous behavior,
// in which case the background watch is skipped entirely.
func NewManager(agones agonesclient.Interface, core kubernetes.Interface, meetings *controlplane.Store, cfg ManagerConfig) *Manager {
	if cfg.Namespace == "" {
		panic("kube: NewManager requires a non-empty Namespace")
	}
	return &Manager{agones: agones, core: core, meetings: meetings, cfg: cfg.withDefaults()}
}

// SetServerRegistry attaches the admission registry to the manager. Managed
// meetings are represented in both the control plane and Servers[] so the
// admission route can find their keys; when Agones resolves browser URIs,
// both records must be updated together. Kept as a setter to preserve the
// small NewManager constructor used by existing callers and tests.
func (m *Manager) SetServerRegistry(servers *config.Store) {
	m.servers = servers
}

func secretName(meetingID string) string     { return "basis-" + meetingID + "-sso" }
func gameServerName(meetingID string) string { return "basis-" + meetingID }

// Create implements RoomProvisioner. It synchronously creates the Secret and
// GameServer (rolling back the Secret if the GameServer create fails), then
// — if a *controlplane.Store was supplied to NewManager — starts a
// background goroutine that polls the GameServer until Ready (or times out)
// and reports the outcome via Store.UpdateStatus. This split matches
// design.md §4.2: step "5. kube.Manager.Create を呼び…" is synchronous, the
// Ready-wait that follows it is explicitly "(非同期)". The RoomProvisioner
// interface is unchanged (still just Create/Delete returning an error for
// the synchronous part); the async continuation lives entirely inside
// Manager rather than requiring internal/api's handlers to change.
func (m *Manager) Create(ctx context.Context, meetingID string, keys RoomKeys) error {
	labels := map[string]string{appLabelKey: appLabelValue, instanceLabelKey: meetingID}

	secret := &corev1.Secret{
		ObjectMeta: metav1.ObjectMeta{
			Name:      secretName(meetingID),
			Namespace: m.cfg.Namespace,
			Labels:    labels,
		},
		Type: corev1.SecretTypeOpaque,
		// Data keys are the exact Basis Configuration field names
		// (docs/concierge/design.md §5): envFrom.secretRef below exposes
		// each key as an identically-named container env var. Data (not
		// StringData) is set directly so the values are visible
		// immediately through every client (a real API server converts
		// StringData into Data server-side, but the fake clientsets used in
		// tests do not).
		Data: map[string][]byte{
			"SsoAdmissionTicketSigningKey": []byte(keys.TicketSigningKey),
			"SsoTransportPrivateKey":       []byte(keys.TransportPrivateKey),
			"SsoTransportPublicKey":        []byte(keys.TransportPublicKey),
		},
	}
	if _, err := m.core.CoreV1().Secrets(m.cfg.Namespace).Create(ctx, secret, metav1.CreateOptions{}); err != nil {
		if apierrors.IsAlreadyExists(err) {
			return fmt.Errorf("%w: secret %s", ErrAlreadyExists, secret.Name)
		}
		return fmt.Errorf("kube: create secret: %w", err)
	}

	gs := &agonesv1.GameServer{
		ObjectMeta: metav1.ObjectMeta{
			Name:      gameServerName(meetingID),
			Namespace: m.cfg.Namespace,
			Labels:    labels,
		},
		Spec: agonesv1.GameServerSpec{
			Container: gameContainerName,
			Ports: []agonesv1.GameServerPort{
				{
					Name:          "game",
					PortPolicy:    agonesv1.Dynamic,
					ContainerPort: m.cfg.ContainerPort,
					Protocol:      corev1.ProtocolUDP,
				},
			},
			Template: corev1.PodTemplateSpec{
				ObjectMeta: metav1.ObjectMeta{Labels: labels},
				Spec: corev1.PodSpec{
					// No liveness/readiness probes: Agones injects its own
					// via the SDK sidecar (agones-ready, below), same as
					// basis-k8s.
					Containers: []corev1.Container{
						{
							Name:  gameContainerName,
							Image: m.cfg.Image,
							EnvFrom: []corev1.EnvFromSource{
								{SecretRef: &corev1.SecretEnvSource{LocalObjectReference: corev1.LocalObjectReference{Name: secret.Name}}},
							},
							Env: []corev1.EnvVar{
								// Field-name-as-env-var overrides on
								// Basis Server's Configuration
								// (BasisServerConfiguration.cs:249-302),
								// per design.md §5.1 point 3.
								{Name: "RequireSso", Value: "true"},
								{Name: "AutoStartSsoBroker", Value: "false"},
								{Name: "SetPort", Value: strconv.FormatInt(int64(m.cfg.ContainerPort), 10)},
							},
						},
						{
							Name:    readyContainerName,
							Image:   readyContainerImage,
							Command: []string{"sh", "-c", readyContainerScript},
						},
					},
				},
			},
		},
	}
	if m.cfg.WebSocketEnabled {
		gs.Spec.Ports = append(gs.Spec.Ports, agonesv1.GameServerPort{
			Name:          "websocket",
			PortPolicy:    agonesv1.Dynamic,
			ContainerPort: m.cfg.WebSocketContainerPort,
			Protocol:      corev1.ProtocolTCP,
		})
		webEnv := []corev1.EnvVar{
			{Name: "WebSocketEnabled", Value: "true"},
			{Name: "WebSocketPort", Value: strconv.FormatInt(int64(m.cfg.WebSocketContainerPort), 10)},
			{Name: "WebSocketPath", Value: m.cfg.WebSocketPath},
			{Name: "WebSocketServerInfoPath", Value: m.cfg.ServerInfoPath},
			{Name: "WebSocketUseTls", Value: strconv.FormatBool(m.cfg.WebSocketUseTLS)},
		}
		if len(m.cfg.WebSocketAllowedOrigins) > 0 {
			webEnv = append(webEnv, corev1.EnvVar{Name: "WebSocketAllowedOrigins", Value: strings.Join(m.cfg.WebSocketAllowedOrigins, ",")})
		}
		gs.Spec.Template.Spec.Containers[0].Env = append(gs.Spec.Template.Spec.Containers[0].Env, webEnv...)
	}
	if _, err := m.agones.AgonesV1().GameServers(m.cfg.Namespace).Create(ctx, gs, metav1.CreateOptions{}); err != nil {
		// Roll back the Secret so a failed Create never leaves an orphaned
		// Secret behind. Use context.Background() for the rollback: ctx may
		// already be why the create failed (e.g. caller cancellation).
		if delErr := m.core.CoreV1().Secrets(m.cfg.Namespace).Delete(context.Background(), secret.Name, metav1.DeleteOptions{}); delErr != nil && !apierrors.IsNotFound(delErr) {
			log.Printf("kube: create %s: rollback of secret %s failed: %v", meetingID, secret.Name, delErr)
		}
		if apierrors.IsAlreadyExists(err) {
			return fmt.Errorf("%w: gameserver %s", ErrAlreadyExists, gs.Name)
		}
		return fmt.Errorf("kube: create gameserver: %w", err)
	}

	if m.meetings != nil {
		if keys.WebSocketUri != "" && keys.ServerInfoUri != "" {
			m.meetings.UpdateBrowserEndpoints(meetingID, keys.WebSocketUri, keys.ServerInfoUri)
		}
		go m.watchReady(meetingID)
	}
	return nil
}

// watchReady polls the GameServer named after meetingID until it reports
// Ready with a resolved Status.Address/Ports, or until cfg.ReadyTimeout
// elapses, then reports the outcome via m.meetings.UpdateStatus. It runs
// detached from the request context (a client disconnecting from
// POST /admin/meetings must not cancel the watch), bounded only by its own
// timeout. Per design.md §12 decision 3, a timeout marks the meeting
// "failed" — there is no automatic retry.
func (m *Manager) watchReady(meetingID string) {
	ctx, cancel := context.WithTimeout(context.Background(), m.cfg.ReadyTimeout)
	defer cancel()

	name := gameServerName(meetingID)
	ticker := time.NewTicker(m.cfg.PollInterval)
	defer ticker.Stop()

	for {
		gs, err := m.agones.AgonesV1().GameServers(m.cfg.Namespace).Get(ctx, name, metav1.GetOptions{})
		switch {
		case err == nil && gs.Status.State == agonesv1.GameServerStateReady && gs.Status.Address != "":
			udpPort, tcpPort, ok := resolvedPorts(gs.Status.Ports)
			if !ok {
				break
			}
			m.meetings.UpdateStatus(meetingID, "ready", "Kubernetes GameServer is ready.", gs.Status.Address, uint16(udpPort))
			if m.cfg.WebSocketEnabled {
				record, _ := m.meetings.Find(meetingID)
				webSocketURI := record.WebSocketUri
				serverInfoURI := record.ServerInfoUri
				if webSocketURI == "" {
					webSocketURI = expandEndpointTemplate(m.cfg.WebSocketUriTemplate, gs.Status.Address, tcpPort)
				}
				if serverInfoURI == "" {
					serverInfoURI = expandEndpointTemplate(m.cfg.ServerInfoUriTemplate, gs.Status.Address, tcpPort)
				}
				if webSocketURI != "" && serverInfoURI != "" {
					m.meetings.UpdateBrowserEndpoints(meetingID, webSocketURI, serverInfoURI)
					if m.servers != nil && !m.servers.UpdateBrowserEndpoints(meetingID, webSocketURI, serverInfoURI) {
						log.Printf("kube: meeting %s browser endpoints could not be persisted to Servers[]", meetingID)
					}
				}
			}
			return
		case err != nil && apierrors.IsNotFound(err):
			// Deleted out from under the watch (e.g. the meeting was
			// deleted while still provisioning). Nothing more to report.
			return
		}

		select {
		case <-ctx.Done():
			log.Printf("kube: meeting %s: GameServer %s did not become Ready within %s; marking failed", meetingID, name, m.cfg.ReadyTimeout)
			m.meetings.UpdateStatus(meetingID, "failed", fmt.Sprintf("Kubernetes GameServer did not become Ready within %s.", m.cfg.ReadyTimeout), "", 0)
			return
		case <-ticker.C:
		}
	}
}

func resolvedPorts(ports []agonesv1.GameServerStatusPort) (udpPort, tcpPort int32, ok bool) {
	for _, p := range ports {
		switch p.Name {
		case "game":
			udpPort = p.Port
		case "websocket":
			tcpPort = p.Port
		}
	}
	// Existing GameServers created before named-port support may have a
	// single unnamed status port. Preserve their UDP behavior.
	if udpPort == 0 && len(ports) == 1 {
		udpPort = ports[0].Port
	}
	if udpPort == 0 || (tcpPort == 0 && len(ports) > 1) {
		return 0, 0, false
	}
	return udpPort, tcpPort, true
}

func expandEndpointTemplate(template, host string, port int32) string {
	if template == "" || port <= 0 {
		return ""
	}
	return strings.NewReplacer("{host}", host, "{port}", strconv.FormatInt(int64(port), 10)).Replace(template)
}

// Delete implements RoomProvisioner. It removes the GameServer and Secret
// for meetingID, tolerating either being already gone (matching
// RoomProvisioner's documented "deleting an unknown meetingID is a no-op
// success" contract, used by internal/api's create-rollback path).
func (m *Manager) Delete(ctx context.Context, meetingID string) error {
	if err := m.agones.AgonesV1().GameServers(m.cfg.Namespace).Delete(ctx, gameServerName(meetingID), metav1.DeleteOptions{}); err != nil && !apierrors.IsNotFound(err) {
		return fmt.Errorf("kube: delete gameserver: %w", err)
	}
	if err := m.core.CoreV1().Secrets(m.cfg.Namespace).Delete(ctx, secretName(meetingID), metav1.DeleteOptions{}); err != nil && !apierrors.IsNotFound(err) {
		return fmt.Errorf("kube: delete secret: %w", err)
	}
	return nil
}
