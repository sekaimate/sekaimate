package kube

import (
	"context"
	"errors"
	"path/filepath"
	"testing"
	"time"

	agonesv1 "agones.dev/agones/pkg/apis/agones/v1"
	agonesfake "agones.dev/agones/pkg/client/clientset/versioned/fake"
	corev1 "k8s.io/api/core/v1"
	apierrors "k8s.io/apimachinery/pkg/api/errors"
	metav1 "k8s.io/apimachinery/pkg/apis/meta/v1"
	k8sfake "k8s.io/client-go/kubernetes/fake"

	"github.com/sekaimate/sekaimate/concierge/internal/config"
	"github.com/sekaimate/sekaimate/concierge/internal/controlplane"
)

const testNamespace = "basis"

func newTestStore(t *testing.T) *controlplane.Store {
	t.Helper()
	return controlplane.NewStore(filepath.Join(t.TempDir(), "control-plane.json"))
}

func newTestManager(t *testing.T, meetings *controlplane.Store, cfg ManagerConfig) (*Manager, *agonesfake.Clientset, *k8sfake.Clientset) {
	t.Helper()
	if cfg.Namespace == "" {
		cfg.Namespace = testNamespace
	}
	agones := agonesfake.NewSimpleClientset()
	core := k8sfake.NewSimpleClientset()
	return NewManager(agones, core, meetings, cfg), agones, core
}

func testKeys() RoomKeys {
	return RoomKeys{
		TicketSigningKey:    "signing-key-value",
		TransportPrivateKey: "private-key-value",
		TransportPublicKey:  "public-key-value",
	}
}

func TestCreate_SecretAndGameServer(t *testing.T) {
	m, agones, core := newTestManager(t, nil, ManagerConfig{})
	ctx := context.Background()

	if err := m.Create(ctx, "room-1", testKeys()); err != nil {
		t.Fatalf("Create: %v", err)
	}

	secret, err := core.CoreV1().Secrets(testNamespace).Get(ctx, "basis-room-1-sso", metav1.GetOptions{})
	if err != nil {
		t.Fatalf("get secret: %v", err)
	}
	wantLabels := map[string]string{"app": "basis-server", "instance": "room-1"}
	if secret.Labels["app"] != wantLabels["app"] || secret.Labels["instance"] != wantLabels["instance"] {
		t.Errorf("secret labels = %v, want %v", secret.Labels, wantLabels)
	}
	wantData := map[string]string{
		"SsoAdmissionTicketSigningKey": "signing-key-value",
		"SsoTransportPrivateKey":       "private-key-value",
		"SsoTransportPublicKey":        "public-key-value",
	}
	for k, v := range wantData {
		if got := string(secret.Data[k]); got != v {
			t.Errorf("secret.Data[%s] = %q, want %q", k, got, v)
		}
	}

	gs, err := agones.AgonesV1().GameServers(testNamespace).Get(ctx, "basis-room-1", metav1.GetOptions{})
	if err != nil {
		t.Fatalf("get gameserver: %v", err)
	}
	if gs.Labels["app"] != wantLabels["app"] || gs.Labels["instance"] != wantLabels["instance"] {
		t.Errorf("gameserver labels = %v, want %v", gs.Labels, wantLabels)
	}
	if gs.Spec.Container != gameContainerName {
		t.Errorf("spec.container = %q, want %q", gs.Spec.Container, gameContainerName)
	}
	if len(gs.Spec.Ports) != 1 {
		t.Fatalf("spec.ports = %v, want 1 entry", gs.Spec.Ports)
	}
	wantPort := agonesv1.GameServerPort{
		Name:          "game",
		PortPolicy:    agonesv1.Dynamic,
		ContainerPort: defaultContainerPort,
		Protocol:      corev1.ProtocolUDP,
	}
	if gs.Spec.Ports[0] != wantPort {
		t.Errorf("spec.ports[0] = %+v, want %+v", gs.Spec.Ports[0], wantPort)
	}

	containers := gs.Spec.Template.Spec.Containers
	if len(containers) != 2 {
		t.Fatalf("containers = %v, want 2 (game + agones-ready sidecar)", containers)
	}
	game := containers[0]
	if game.Name != gameContainerName || game.Image != defaultImage {
		t.Errorf("game container = %q/%q, want %q/%q", game.Name, game.Image, gameContainerName, defaultImage)
	}
	if len(game.EnvFrom) != 1 || game.EnvFrom[0].SecretRef == nil || game.EnvFrom[0].SecretRef.Name != secret.Name {
		t.Errorf("game.EnvFrom = %+v, want envFrom referencing secret %s", game.EnvFrom, secret.Name)
	}
	wantEnv := map[string]string{"RequireSso": "true", "AutoStartSsoBroker": "false", "SetPort": "4296"}
	gotEnv := map[string]string{}
	for _, e := range game.Env {
		gotEnv[e.Name] = e.Value
	}
	for k, v := range wantEnv {
		if gotEnv[k] != v {
			t.Errorf("env[%s] = %q, want %q", k, gotEnv[k], v)
		}
	}
	if len(gotEnv) != len(wantEnv) {
		t.Errorf("env = %v, want exactly %v", gotEnv, wantEnv)
	}

	sidecar := containers[1]
	if sidecar.Name != readyContainerName || sidecar.Image != readyContainerImage {
		t.Errorf("sidecar = %q/%q, want %q/%q", sidecar.Name, sidecar.Image, readyContainerName, readyContainerImage)
	}
	if len(sidecar.Command) != 3 || sidecar.Command[0] != "sh" || sidecar.Command[1] != "-c" {
		t.Errorf("sidecar command = %v, want [sh -c <script>]", sidecar.Command)
	}
}

func TestCreate_WebSocketAddsNamedTCPPortAndOverrides(t *testing.T) {
	m, agones, _ := newTestManager(t, nil, ManagerConfig{
		WebSocketEnabled:            true,
		WebSocketContainerPort:      4297,
		WebSocketPath:               "/basis",
		ServerInfoPath:              "/server-info",
		WebSocketUseTLS:             true,
		WebSocketCertificatePath:    "/run/certs/server.pem",
		WebSocketCertificateKeyPath: "/run/certs/server-key.pem",
		WebSocketAllowedOrigins:     []string{"https://web.example"},
	})
	if err := m.Create(context.Background(), "web-room", testKeys()); err != nil {
		t.Fatalf("Create: %v", err)
	}
	gs, err := agones.AgonesV1().GameServers(testNamespace).Get(context.Background(), "basis-web-room", metav1.GetOptions{})
	if err != nil {
		t.Fatalf("get gameserver: %v", err)
	}
	if len(gs.Spec.Ports) != 2 || gs.Spec.Ports[1].Name != "websocket" || gs.Spec.Ports[1].Protocol != corev1.ProtocolTCP {
		t.Fatalf("ports = %+v, want game UDP + websocket TCP", gs.Spec.Ports)
	}
	env := map[string]string{}
	for _, item := range gs.Spec.Template.Spec.Containers[0].Env {
		env[item.Name] = item.Value
	}
	for key, want := range map[string]string{
		"WebSocketEnabled": "true", "WebSocketPort": "4297", "WebSocketPath": "/basis",
		"WebSocketServerInfoPath": "/server-info", "WebSocketUseTls": "true",
		"WebSocketCertificatePath": "/run/certs/server.pem", "WebSocketCertificateKeyPath": "/run/certs/server-key.pem",
		"WebSocketAllowedOrigins": "https://web.example",
	} {
		if env[key] != want {
			t.Errorf("env[%s] = %q, want %q", key, env[key], want)
		}
	}
}

func TestCreate_CustomImageAndPort(t *testing.T) {
	m, agones, _ := newTestManager(t, nil, ManagerConfig{Image: "registry.example/basis-server:v2", ContainerPort: 5000})
	ctx := context.Background()

	if err := m.Create(ctx, "room-2", testKeys()); err != nil {
		t.Fatalf("Create: %v", err)
	}
	gs, err := agones.AgonesV1().GameServers(testNamespace).Get(ctx, "basis-room-2", metav1.GetOptions{})
	if err != nil {
		t.Fatalf("get gameserver: %v", err)
	}
	if gs.Spec.Ports[0].ContainerPort != 5000 {
		t.Errorf("ContainerPort = %d, want 5000", gs.Spec.Ports[0].ContainerPort)
	}
	game := gs.Spec.Template.Spec.Containers[0]
	if game.Image != "registry.example/basis-server:v2" {
		t.Errorf("image = %q, want registry.example/basis-server:v2", game.Image)
	}
	found := false
	for _, e := range game.Env {
		if e.Name == "SetPort" {
			found = true
			if e.Value != "5000" {
				t.Errorf("SetPort = %q, want 5000", e.Value)
			}
		}
	}
	if !found {
		t.Error("SetPort env var not set")
	}
}

// TestCreate_RollsBackSecretOnGameServerFailure pre-creates a conflicting
// GameServer so the Secret create succeeds but the GameServer create fails
// with AlreadyExists, and checks Create deletes the Secret it just made
// rather than leaving it orphaned.
func TestCreate_RollsBackSecretOnGameServerFailure(t *testing.T) {
	m, agones, core := newTestManager(t, nil, ManagerConfig{})
	ctx := context.Background()

	existing := &agonesv1.GameServer{ObjectMeta: metav1.ObjectMeta{Name: "basis-room-3", Namespace: testNamespace}}
	if _, err := agones.AgonesV1().GameServers(testNamespace).Create(ctx, existing, metav1.CreateOptions{}); err != nil {
		t.Fatalf("seed conflicting gameserver: %v", err)
	}

	err := m.Create(ctx, "room-3", testKeys())
	if !errors.Is(err, ErrAlreadyExists) {
		t.Fatalf("Create error = %v, want ErrAlreadyExists", err)
	}

	if _, err := core.CoreV1().Secrets(testNamespace).Get(ctx, "basis-room-3-sso", metav1.GetOptions{}); err == nil {
		t.Error("secret still exists after GameServer create failure; want rollback")
	} else if !apierrors.IsNotFound(err) {
		t.Errorf("get secret after rollback: unexpected error %v", err)
	}
}

func TestDelete_RemovesGameServerAndSecret(t *testing.T) {
	m, agones, core := newTestManager(t, nil, ManagerConfig{})
	ctx := context.Background()
	if err := m.Create(ctx, "room-4", testKeys()); err != nil {
		t.Fatalf("Create: %v", err)
	}

	if err := m.Delete(ctx, "room-4"); err != nil {
		t.Fatalf("Delete: %v", err)
	}

	if _, err := agones.AgonesV1().GameServers(testNamespace).Get(ctx, "basis-room-4", metav1.GetOptions{}); err == nil {
		t.Error("gameserver still exists after Delete")
	}
	if _, err := core.CoreV1().Secrets(testNamespace).Get(ctx, "basis-room-4-sso", metav1.GetOptions{}); err == nil {
		t.Error("secret still exists after Delete")
	}
}

func TestDelete_TolerantOfAlreadyGone(t *testing.T) {
	m, _, _ := newTestManager(t, nil, ManagerConfig{})
	if err := m.Delete(context.Background(), "never-created"); err != nil {
		t.Fatalf("Delete on unknown meeting: %v, want nil (no-op success)", err)
	}
}

// TestCreate_WatchReadySuccess exercises the background Ready-wait: after
// Create returns, it flips the fake GameServer's status to Ready and checks
// the meeting record eventually reflects "ready" with the resolved
// host/port.
func TestCreate_WatchReadySuccess(t *testing.T) {
	meetings := newTestStore(t)
	if err := meetings.Add(controlplane.MeetingRecord{Id: "room-5", Title: "Room Five", Status: "provisioning"}); err != nil {
		t.Fatalf("seed meeting record: %v", err)
	}
	m, agones, _ := newTestManager(t, meetings, ManagerConfig{PollInterval: 10 * time.Millisecond, ReadyTimeout: 2 * time.Second})
	ctx := context.Background()

	if err := m.Create(ctx, "room-5", testKeys()); err != nil {
		t.Fatalf("Create: %v", err)
	}

	gs, err := agones.AgonesV1().GameServers(testNamespace).Get(ctx, "basis-room-5", metav1.GetOptions{})
	if err != nil {
		t.Fatalf("get gameserver: %v", err)
	}
	gs.Status = agonesv1.GameServerStatus{
		State:   agonesv1.GameServerStateReady,
		Address: "10.0.0.9",
		Ports:   []agonesv1.GameServerStatusPort{{Name: "game", Port: 7777}},
	}
	if _, err := agones.AgonesV1().GameServers(testNamespace).Update(ctx, gs, metav1.UpdateOptions{}); err != nil {
		t.Fatalf("update gameserver status: %v", err)
	}

	deadline := time.Now().Add(2 * time.Second)
	for {
		rec, ok := meetings.Find("room-5")
		if !ok {
			t.Fatalf("meeting record disappeared")
		}
		if rec.Status == "ready" {
			if rec.Host != "10.0.0.9" || rec.Port != 7777 {
				t.Errorf("record host/port = %s/%d, want 10.0.0.9/7777", rec.Host, rec.Port)
			}
			return
		}
		if time.Now().After(deadline) {
			t.Fatalf("meeting record never became ready, last status = %q", rec.Status)
		}
		time.Sleep(5 * time.Millisecond)
	}
}

func TestCreate_WatchReadyExpandsBrowserEndpoints(t *testing.T) {
	meetings := newTestStore(t)
	if err := meetings.Add(controlplane.MeetingRecord{Id: "web-room", Title: "Web", Status: "provisioning"}); err != nil {
		t.Fatalf("seed meeting record: %v", err)
	}
	m, agones, _ := newTestManager(t, meetings, ManagerConfig{
		WebSocketEnabled: true, WebSocketUriTemplate: "wss://{host}:{port}/basis",
		ServerInfoUriTemplate: "https://{host}:{port}/server-info",
		PollInterval:          10 * time.Millisecond, ReadyTimeout: 2 * time.Second,
	})
	if err := m.Create(context.Background(), "web-room", testKeys()); err != nil {
		t.Fatalf("Create: %v", err)
	}
	gs, err := agones.AgonesV1().GameServers(testNamespace).Get(context.Background(), "basis-web-room", metav1.GetOptions{})
	if err != nil {
		t.Fatalf("get gameserver: %v", err)
	}
	gs.Status = agonesv1.GameServerStatus{
		State: agonesv1.GameServerStateReady, Address: "10.0.0.9",
		Ports: []agonesv1.GameServerStatusPort{{Name: "game", Port: 7777}, {Name: "websocket", Port: 8777}},
	}
	if _, err := agones.AgonesV1().GameServers(testNamespace).Update(context.Background(), gs, metav1.UpdateOptions{}); err != nil {
		t.Fatalf("update gameserver status: %v", err)
	}
	deadline := time.Now().Add(2 * time.Second)
	for time.Now().Before(deadline) {
		rec, _ := meetings.Find("web-room")
		if rec.Status == "ready" {
			if rec.WebSocketUri != "wss://10.0.0.9:8777/basis" || rec.ServerInfoUri != "https://10.0.0.9:8777/server-info" {
				t.Fatalf("browser endpoints = %q / %q", rec.WebSocketUri, rec.ServerInfoUri)
			}
			return
		}
		time.Sleep(5 * time.Millisecond)
	}
	t.Fatal("meeting did not become ready")
}

func TestCreate_WatchReadyPersistsBrowserEndpointsToServerRegistry(t *testing.T) {
	meetings := newTestStore(t)
	if err := meetings.Add(controlplane.MeetingRecord{Id: "web-registry", Title: "Web", Status: "provisioning"}); err != nil {
		t.Fatalf("seed meeting record: %v", err)
	}
	servers, err := config.Load(filepath.Join(t.TempDir(), "appsettings.json"))
	if err != nil {
		t.Fatalf("load server registry: %v", err)
	}
	if err := servers.AddServer(config.ServerConfig{Id: "web-registry"}); err != nil {
		t.Fatalf("seed server registry: %v", err)
	}
	m, agones, _ := newTestManager(t, meetings, ManagerConfig{
		WebSocketEnabled: true, WebSocketUriTemplate: "wss://{host}:{port}/basis",
		ServerInfoUriTemplate: "https://{host}:{port}/server-info",
		PollInterval:          10 * time.Millisecond, ReadyTimeout: 2 * time.Second,
	})
	m.SetServerRegistry(servers)
	if err := m.Create(context.Background(), "web-registry", testKeys()); err != nil {
		t.Fatalf("Create: %v", err)
	}
	gs, err := agones.AgonesV1().GameServers(testNamespace).Get(context.Background(), "basis-web-registry", metav1.GetOptions{})
	if err != nil {
		t.Fatalf("get gameserver: %v", err)
	}
	gs.Status = agonesv1.GameServerStatus{
		State: agonesv1.GameServerStateReady, Address: "10.0.0.9",
		Ports: []agonesv1.GameServerStatusPort{{Name: "game", Port: 7777}, {Name: "websocket", Port: 8777}},
	}
	if _, err := agones.AgonesV1().GameServers(testNamespace).Update(context.Background(), gs, metav1.UpdateOptions{}); err != nil {
		t.Fatalf("update gameserver status: %v", err)
	}
	deadline := time.Now().Add(2 * time.Second)
	for time.Now().Before(deadline) {
		server, _ := servers.FindServer("web-registry")
		if server.WebSocketUri != "" {
			if server.WebSocketUri != "wss://10.0.0.9:8777/basis" || server.ServerInfoUri != "https://10.0.0.9:8777/server-info" {
				t.Fatalf("server browser endpoints = %q / %q", server.WebSocketUri, server.ServerInfoUri)
			}
			return
		}
		time.Sleep(5 * time.Millisecond)
	}
	t.Fatal("server registry browser endpoints were not updated")
}

// TestCreate_WatchReadyTimeout checks that the background watch marks the
// meeting "failed" (no retry) once ReadyTimeout elapses without the
// GameServer becoming Ready.
func TestCreate_WatchReadyTimeout(t *testing.T) {
	meetings := newTestStore(t)
	if err := meetings.Add(controlplane.MeetingRecord{Id: "room-6", Title: "Room Six", Status: "provisioning"}); err != nil {
		t.Fatalf("seed meeting record: %v", err)
	}
	m, _, _ := newTestManager(t, meetings, ManagerConfig{PollInterval: 5 * time.Millisecond, ReadyTimeout: 30 * time.Millisecond})
	ctx := context.Background()

	if err := m.Create(ctx, "room-6", testKeys()); err != nil {
		t.Fatalf("Create: %v", err)
	}

	deadline := time.Now().Add(2 * time.Second)
	for {
		rec, ok := meetings.Find("room-6")
		if !ok {
			t.Fatalf("meeting record disappeared")
		}
		if rec.Status == "failed" {
			return
		}
		if time.Now().After(deadline) {
			t.Fatalf("meeting record never marked failed, last status = %q", rec.Status)
		}
		time.Sleep(5 * time.Millisecond)
	}
}
