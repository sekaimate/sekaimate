package kube

import (
	"context"
	"testing"

	metav1 "k8s.io/apimachinery/pkg/apis/meta/v1"

	"github.com/sekaimate/sekaimate/concierge/internal/controlplane"
)

func seedGameServer(t *testing.T, m *Manager, id string) {
	t.Helper()
	if err := m.Create(context.Background(), id, testKeys()); err != nil {
		t.Fatalf("seed gameserver %s: %v", id, err)
	}
}

// TestReconcile_MarksMissingGameServerFailed checks that a MeetingRecord
// with no matching GameServer is marked "failed", per design.md §12
// decision 2 (Kubernetes is the source of truth).
func TestReconcile_MarksMissingGameServerFailed(t *testing.T) {
	meetings := newTestStore(t)
	if err := meetings.Add(controlplane.MeetingRecord{Id: "orphan-record", Title: "Orphan", Status: "ready", Host: "1.2.3.4", Port: 4296, Managed: true}); err != nil {
		t.Fatalf("seed meeting: %v", err)
	}
	m, _, _ := newTestManager(t, meetings, ManagerConfig{})

	if err := m.Reconcile(context.Background()); err != nil {
		t.Fatalf("Reconcile: %v", err)
	}

	rec, ok := meetings.Find("orphan-record")
	if !ok || rec.Status != "failed" {
		t.Fatalf("record status = %+v, want failed", rec)
	}
}

// TestReconcile_IgnoresUnmanagedMeetingWithoutGameServer checks that a
// MeetingRecord for an externally-run server (Managed=false, an explicit
// host/port supplied at creation) is never marked failed for lacking a
// GameServer — it was never expected to have one (design.md §4.2).
func TestReconcile_IgnoresUnmanagedMeetingWithoutGameServer(t *testing.T) {
	meetings := newTestStore(t)
	if err := meetings.Add(controlplane.MeetingRecord{Id: "external-record", Title: "External", Status: "ready", Host: "1.2.3.4", Port: 4296, Managed: false}); err != nil {
		t.Fatalf("seed meeting: %v", err)
	}
	m, _, _ := newTestManager(t, meetings, ManagerConfig{})

	if err := m.Reconcile(context.Background()); err != nil {
		t.Fatalf("Reconcile: %v", err)
	}

	rec, ok := meetings.Find("external-record")
	if !ok || rec.Status != "ready" {
		t.Fatalf("record status = %+v, want unchanged ready", rec)
	}
}

// TestReconcile_LeavesMatchedMeetingAlone checks a MeetingRecord backed by
// an existing GameServer is left untouched.
func TestReconcile_LeavesMatchedMeetingAlone(t *testing.T) {
	meetings := newTestStore(t)
	if err := meetings.Add(controlplane.MeetingRecord{Id: "matched", Title: "Matched", Status: "ready", Host: "1.2.3.4", Port: 4296, Managed: true}); err != nil {
		t.Fatalf("seed meeting: %v", err)
	}
	m, _, _ := newTestManager(t, meetings, ManagerConfig{})
	seedGameServer(t, m, "matched")

	if err := m.Reconcile(context.Background()); err != nil {
		t.Fatalf("Reconcile: %v", err)
	}

	rec, ok := meetings.Find("matched")
	if !ok || rec.Status != "ready" {
		t.Fatalf("record status = %+v, want unchanged ready", rec)
	}
}

// TestReconcile_IgnoresLocalBootstrapMeeting checks the "local" Compose
// bootstrap meeting is never marked failed for lacking a GameServer — it is
// never routed through Manager.Create in the first place (design.md §12
// decision 1).
func TestReconcile_IgnoresLocalBootstrapMeeting(t *testing.T) {
	meetings := newTestStore(t)
	meetings.EnsureSingleComposeMeeting("local", "Local", "game.example.com", 4296, "pw", "pubkey")
	m, _, _ := newTestManager(t, meetings, ManagerConfig{})

	if err := m.Reconcile(context.Background()); err != nil {
		t.Fatalf("Reconcile: %v", err)
	}

	rec, ok := meetings.Find("local")
	if !ok || rec.Status != "ready" {
		t.Fatalf("local record status = %+v, want unchanged ready", rec)
	}
}

// TestReconcile_OrphanedResourcesAreNotDeleted checks that a GameServer and
// Secret with no matching MeetingRecord are left in place (only logged).
func TestReconcile_OrphanedResourcesAreNotDeleted(t *testing.T) {
	meetings := newTestStore(t)
	m, agones, core := newTestManager(t, meetings, ManagerConfig{})
	seedGameServer(t, m, "orphan-gs")

	if err := m.Reconcile(context.Background()); err != nil {
		t.Fatalf("Reconcile: %v", err)
	}

	if _, err := agones.AgonesV1().GameServers(testNamespace).Get(context.Background(), "basis-orphan-gs", metav1.GetOptions{}); err != nil {
		t.Errorf("orphaned gameserver was deleted: %v", err)
	}
	if _, err := core.CoreV1().Secrets(testNamespace).Get(context.Background(), "basis-orphan-gs-sso", metav1.GetOptions{}); err != nil {
		t.Errorf("orphaned secret was deleted: %v", err)
	}
}

// TestReconcile_RequiresMeetingsStore checks Reconcile refuses to run
// against a Manager with no meetings store — unlike Create/Delete, it has
// nothing meaningful to fall back to.
func TestReconcile_RequiresMeetingsStore(t *testing.T) {
	m, _, _ := newTestManager(t, nil, ManagerConfig{})
	if err := m.Reconcile(context.Background()); err == nil {
		t.Fatal("Reconcile with nil meetings store: want error, got nil")
	}
}
