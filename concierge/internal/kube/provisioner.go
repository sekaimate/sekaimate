// Package kube will host the Agones GameServer/Secret provisioning that
// backs concierge-managed meetings, mirroring basis-k8s's internal/kube.
// Phase 1 (this package, currently) ships only the RoomProvisioner
// interface and a no-op implementation: concierge has no Kubernetes
// integration yet (see docs/concierge/design.md §12 "決定事項" and the
// SCOPE note in the phase-1 task). The interface exists now so the
// admin/meetings handlers in internal/api can depend on it without
// reshaping once phase 2 adds the Agones-backed Manager.
package kube

import "context"

// RoomKeys are the per-meeting values a concierge-managed game server needs
// injected as environment variables (design.md §5): the join password, the
// ticket-signing HMAC key, and the X25519 transport keypair.
type RoomKeys struct {
	Password            string
	TicketSigningKey    string
	TransportPrivateKey string
	TransportPublicKey  string
	// Optional explicit browser endpoints. When omitted, Manager may expand
	// its explicitly configured URI templates after Agones assigns ports.
	WebSocketUri  string
	ServerInfoUri string
}

// RoomProvisioner creates and destroys the compute backing a
// concierge-managed meeting. In phase 2 this will create/delete a
// Kubernetes Secret (holding RoomKeys) and an Agones GameServer that
// references it; in phase 1 the only implementation is NoopProvisioner.
type RoomProvisioner interface {
	// Create provisions compute for meetingID, handing it keys. It is only
	// called for meetings concierge itself manages (POST /admin/meetings),
	// never for static Servers[] entries.
	Create(ctx context.Context, meetingID string, keys RoomKeys) error
	// Delete tears down compute for meetingID. Implementations must treat
	// deleting an unknown meetingID as a no-op success, since callers may
	// invoke Delete defensively during create-rollback.
	Delete(ctx context.Context, meetingID string) error
}

// NoopProvisioner is the phase-1 RoomProvisioner: it does not talk to
// Kubernetes at all. POST /admin/meetings still works exactly as the C#
// broker's does today (a meeting is "ready" the moment an admin supplies a
// host/port, or stays "provisioning" until one is set by some other means);
// it simply never becomes backed by a real GameServer until phase 2 swaps
// this out.
type NoopProvisioner struct{}

var _ RoomProvisioner = NoopProvisioner{}

// Create implements RoomProvisioner and does nothing.
func (NoopProvisioner) Create(context.Context, string, RoomKeys) error { return nil }

// Delete implements RoomProvisioner and does nothing.
func (NoopProvisioner) Delete(context.Context, string) error { return nil }
