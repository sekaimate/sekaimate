package controlplane

import (
	"encoding/json"
	"os"
	"path/filepath"
	"testing"
)

func TestStore_AddFindListDelete(t *testing.T) {
	dir := t.TempDir()
	s := NewStore(filepath.Join(dir, "control-plane.json"))

	m := MeetingRecord{Id: "room-1", Title: "Room One", Status: "provisioning", InviteToken: "tok-1"}
	if err := s.Add(m); err != nil {
		t.Fatalf("Add: %v", err)
	}
	if err := s.Add(m); err == nil {
		t.Fatalf("Add duplicate id: want error, got nil")
	}

	found, ok := s.Find("room-1")
	if !ok || found.Title != "Room One" {
		t.Fatalf("Find(room-1) = %+v, %v", found, ok)
	}

	list := s.List()
	if len(list) != 1 || list[0].Id != "room-1" {
		t.Fatalf("List() = %+v", list)
	}

	deleted, ok := s.Delete("room-1")
	if !ok || deleted.Id != "room-1" {
		t.Fatalf("Delete(room-1) = %+v, %v", deleted, ok)
	}
	if _, ok := s.Find("room-1"); ok {
		t.Fatalf("Find after delete: still found")
	}
}

// TestStore_PersistenceRoundTrip writes a meeting, reloads the store from
// disk, and checks the record and the on-disk JSON shape (PascalCase
// fields, {"Meetings":[...]} root) match research-sso-broker.md §7-12.
func TestStore_PersistenceRoundTrip(t *testing.T) {
	dir := t.TempDir()
	path := filepath.Join(dir, "control-plane.json")

	s1 := NewStore(path)
	m := MeetingRecord{
		Id: "room-1", Title: "Room One", Status: "ready", StatusDetail: "ok",
		Host: "game.example.com", Port: 4296, Password: "secret", InviteToken: "tok-1",
		TicketSigningKey: "signing-key", TransportPrivateKey: "priv", TransportPublicKey: "pub",
	}
	if err := s1.Add(m); err != nil {
		t.Fatalf("Add: %v", err)
	}

	raw, err := os.ReadFile(path)
	if err != nil {
		t.Fatalf("read persisted file: %v", err)
	}
	var generic map[string]any
	if err := json.Unmarshal(raw, &generic); err != nil {
		t.Fatalf("unmarshal persisted file: %v", err)
	}
	meetingsField, ok := generic["Meetings"]
	if !ok {
		t.Fatalf("persisted JSON root missing \"Meetings\" key: %s", raw)
	}
	arr, ok := meetingsField.([]any)
	if !ok || len(arr) != 1 {
		t.Fatalf("Meetings is not a 1-element array: %v", meetingsField)
	}
	entry, ok := arr[0].(map[string]any)
	if !ok {
		t.Fatalf("Meetings[0] is not an object: %v", arr[0])
	}
	for _, field := range []string{"Id", "Title", "Status", "StatusDetail", "Host", "Port", "Password",
		"InviteToken", "TicketSigningKey", "TransportPrivateKey", "TransportPublicKey", "CreatedAt", "UpdatedAt"} {
		if _, ok := entry[field]; !ok {
			t.Errorf("persisted meeting missing PascalCase field %q: %v", field, entry)
		}
	}

	if info, err := os.Stat(path); err != nil {
		t.Fatalf("stat persisted file: %v", err)
	} else if perm := info.Mode().Perm(); perm != 0o600 {
		t.Errorf("persisted file mode = %o, want 0600", perm)
	}

	stored, ok := s1.Find("room-1")
	if !ok {
		t.Fatalf("Find after Add: room-1 not found")
	}

	s2 := NewStore(path)
	reloaded, ok := s2.Find("room-1")
	if !ok {
		t.Fatalf("reloaded store: room-1 not found")
	}
	if !reloaded.CreatedAt.Equal(stored.CreatedAt) || !reloaded.UpdatedAt.Equal(stored.UpdatedAt) {
		t.Errorf("reloaded timestamps = %v/%v, want %v/%v", reloaded.CreatedAt, reloaded.UpdatedAt, stored.CreatedAt, stored.UpdatedAt)
	}
	reloaded.CreatedAt, reloaded.UpdatedAt = stored.CreatedAt, stored.UpdatedAt
	if reloaded != stored {
		t.Errorf("reloaded record = %+v, want %+v", reloaded, stored)
	}
}

func TestStore_FindInvite(t *testing.T) {
	dir := t.TempDir()
	s := NewStore(filepath.Join(dir, "control-plane.json"))
	m := MeetingRecord{Id: "room-1", InviteToken: "correct-token"}
	if err := s.Add(m); err != nil {
		t.Fatalf("Add: %v", err)
	}

	if _, ok := s.FindInvite("correct-token"); !ok {
		t.Errorf("FindInvite(correct-token): not found")
	}
	if _, ok := s.FindInvite("wrong-token"); ok {
		t.Errorf("FindInvite(wrong-token): unexpectedly found")
	}
	if _, ok := s.FindInvite(""); ok {
		t.Errorf("FindInvite(\"\"): unexpectedly found (empty token must never match)")
	}
}

func TestStore_EnsureSingleComposeMeeting(t *testing.T) {
	dir := t.TempDir()
	s := NewStore(filepath.Join(dir, "control-plane.json"))

	s.EnsureSingleComposeMeeting("local", "Local meeting", "", 4296, "pw", "pubkey")
	m, ok := s.Find("local")
	if !ok {
		t.Fatalf("local meeting not created")
	}
	if m.Status != "provisioning" {
		t.Errorf("status = %q, want provisioning when host is empty", m.Status)
	}

	// Refresh with a host now set: should transition to ready.
	s.EnsureSingleComposeMeeting("local", "Local meeting", "game.example.com", 4296, "pw", "pubkey")
	m, _ = s.Find("local")
	if m.Status != "ready" || m.Host != "game.example.com" {
		t.Errorf("after host set: status=%q host=%q, want ready/game.example.com", m.Status, m.Host)
	}

	// A second distinct meeting must not be silently created once one
	// already exists (EnsureSingleComposeMeeting only creates when the
	// store is otherwise empty).
	s.EnsureSingleComposeMeeting("other", "Other", "host", 1, "pw", "pub")
	if _, ok := s.Find("other"); ok {
		t.Errorf("a second bootstrap meeting must not be created once any meeting exists")
	}
}

func TestStore_UpdateStatus(t *testing.T) {
	dir := t.TempDir()
	s := NewStore(filepath.Join(dir, "control-plane.json"))
	if err := s.Add(MeetingRecord{Id: "room-1", Status: "provisioning"}); err != nil {
		t.Fatalf("Add: %v", err)
	}

	if !s.UpdateStatus("room-1", "ready", "now ready", "game.example.com", 4300) {
		t.Fatalf("UpdateStatus: returned false")
	}
	m, _ := s.Find("room-1")
	if m.Status != "ready" || m.Host != "game.example.com" || m.Port != 4300 {
		t.Errorf("after UpdateStatus: %+v", m)
	}

	// host="" / port=0 must leave existing values untouched.
	if !s.UpdateStatus("room-1", "failed", "oops", "", 0) {
		t.Fatalf("UpdateStatus: returned false")
	}
	m, _ = s.Find("room-1")
	if m.Host != "game.example.com" || m.Port != 4300 {
		t.Errorf("host/port changed on empty update: %+v", m)
	}

	if s.UpdateStatus("missing", "ready", "", "", 0) {
		t.Errorf("UpdateStatus(missing): want false")
	}
}
