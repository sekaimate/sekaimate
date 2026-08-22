package main

import (
	"path/filepath"
	"testing"

	"github.com/sekaimate/sekaimate/concierge/internal/config"
	"github.com/sekaimate/sekaimate/concierge/internal/controlplane"
)

func newTestConfigStore(t *testing.T) *config.Store {
	t.Helper()
	s, err := config.Load(filepath.Join(t.TempDir(), "appsettings.json"))
	if err != nil {
		t.Fatalf("config.Load: %v", err)
	}
	return s
}

func newTestMeetingsStore(t *testing.T) *controlplane.Store {
	t.Helper()
	return controlplane.NewStore(filepath.Join(t.TempDir(), "control-plane.json"))
}

// TestCheckNoStaticMeetingIDCollision_Detects checks that a meeting id also
// registered as a static Servers[] entry is rejected, per design.md §4.1.
func TestCheckNoStaticMeetingIDCollision_Detects(t *testing.T) {
	cfg := newTestConfigStore(t)
	if err := cfg.AddServer(config.ServerConfig{Id: "room-1"}); err != nil {
		t.Fatalf("AddServer: %v", err)
	}
	meetings := newTestMeetingsStore(t)
	if err := meetings.Add(controlplane.MeetingRecord{Id: "room-1", Title: "Room One"}); err != nil {
		t.Fatalf("meetings.Add: %v", err)
	}

	if err := checkNoStaticMeetingIDCollision(cfg, meetings); err == nil {
		t.Fatal("checkNoStaticMeetingIDCollision: want error for colliding id, got nil")
	}
}

// TestCheckNoStaticMeetingIDCollision_AllowsLocal checks the sanctioned
// "local" Compose-bootstrap pairing (design.md §12 decision 1) does not
// trip the collision check.
func TestCheckNoStaticMeetingIDCollision_AllowsLocal(t *testing.T) {
	cfg := newTestConfigStore(t)
	if err := cfg.AddServer(config.ServerConfig{Id: "local"}); err != nil {
		t.Fatalf("AddServer: %v", err)
	}
	meetings := newTestMeetingsStore(t)
	meetings.EnsureSingleComposeMeeting("local", "Local", "game.example.com", 4296, "pw", "pubkey")

	if err := checkNoStaticMeetingIDCollision(cfg, meetings); err != nil {
		t.Fatalf("checkNoStaticMeetingIDCollision: want nil for local pairing, got %v", err)
	}
}

// TestCheckNoStaticMeetingIDCollision_AllowsMeetingCreatedServer checks that
// a Servers[] entry created by internal/api.CreateMeeting (FromMeeting:
// true) paired with its own matching MeetingRecord does not trip the
// collision check. This is the state every concierge-managed meeting is in
// for as long as it exists (CreateMeeting always registers both together),
// so this check must not fatal a fresh restart while any meeting is active —
// only a genuinely hand-authored Servers[] entry colliding with an
// unrelated meeting id should.
func TestCheckNoStaticMeetingIDCollision_AllowsMeetingCreatedServer(t *testing.T) {
	cfg := newTestConfigStore(t)
	if err := cfg.AddServer(config.ServerConfig{Id: "room-1", FromMeeting: true}); err != nil {
		t.Fatalf("AddServer: %v", err)
	}
	meetings := newTestMeetingsStore(t)
	if err := meetings.Add(controlplane.MeetingRecord{Id: "room-1", Title: "Room One", Managed: true}); err != nil {
		t.Fatalf("meetings.Add: %v", err)
	}

	if err := checkNoStaticMeetingIDCollision(cfg, meetings); err != nil {
		t.Fatalf("checkNoStaticMeetingIDCollision: want nil for a meeting-created server pairing, got %v", err)
	}
}

// TestCheckNoStaticMeetingIDCollision_NoCollision checks disjoint id sets
// pass cleanly.
func TestCheckNoStaticMeetingIDCollision_NoCollision(t *testing.T) {
	cfg := newTestConfigStore(t)
	if err := cfg.AddServer(config.ServerConfig{Id: "static-only"}); err != nil {
		t.Fatalf("AddServer: %v", err)
	}
	meetings := newTestMeetingsStore(t)
	if err := meetings.Add(controlplane.MeetingRecord{Id: "meeting-only", Title: "Meeting"}); err != nil {
		t.Fatalf("meetings.Add: %v", err)
	}

	if err := checkNoStaticMeetingIDCollision(cfg, meetings); err != nil {
		t.Fatalf("checkNoStaticMeetingIDCollision: want nil, got %v", err)
	}
}
