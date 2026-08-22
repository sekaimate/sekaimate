package controlplane

import "testing"

func TestIsValidID(t *testing.T) {
	cases := map[string]bool{
		"":          false,
		"room-1":    true,
		"Room_1-2":  true,
		"has space": false,
		"has/slash": false,
	}
	for input, want := range cases {
		if got := IsValidID(input); got != want {
			t.Errorf("IsValidID(%q) = %v, want %v", input, got, want)
		}
	}
	// A 48-char valid id must pass, 49 must not.
	valid48 := ""
	for i := 0; i < 48; i++ {
		valid48 += "a"
	}
	if !IsValidID(valid48) {
		t.Errorf("IsValidID(48 chars) = false, want true")
	}
	if IsValidID(valid48 + "a") {
		t.Errorf("IsValidID(49 chars) = true, want false")
	}
}

func TestNewID_Uniqueness(t *testing.T) {
	taken := map[string]bool{}
	exists := func(candidate string) bool { return taken[candidate] }

	first := NewID("", "My Room!!", exists)
	if first == "" {
		t.Fatalf("NewID returned empty string")
	}
	if len(first) > 48 {
		t.Errorf("NewID result too long: %q (%d chars)", first, len(first))
	}
	taken[first] = true

	// Requesting the exact same id again must produce a different id
	// (the exists predicate now reports it taken).
	second := NewID(first, "My Room!!", exists)
	if second == first {
		t.Errorf("NewID did not avoid the already-taken id: %q", second)
	}
}

// TestNewID_AlwaysSuffixesRequestedID matches MeetingIdentity.NewId: even a
// perfectly valid, already-unique requestedId is never returned unchanged —
// a random suffix is always appended on the first attempt.
func TestNewID_AlwaysSuffixesRequestedID(t *testing.T) {
	exists := func(string) bool { return false }
	id := NewID("my-explicit-id", "ignored title", exists)
	if id == "my-explicit-id" {
		t.Errorf("NewID = %q, want a suffixed id (never the bare requested id)", id)
	}
	const prefix = "my-explicit-id-"
	if len(id) <= len(prefix) || id[:len(prefix)] != prefix {
		t.Errorf("NewID = %q, want it to start with %q", id, prefix)
	}
}

func TestIsSafeHost(t *testing.T) {
	cases := map[string]bool{
		"game.example.com": true,
		"127.0.0.1":        true,
		"::1":              true,
		"[::1]":            true,
		"":                 false,
		"has space":        false,
		"host/path":        false,
		"host?query":       false,
		"host#frag":        false,
		"-leading-hyphen":  false,
	}
	for input, want := range cases {
		if got := IsSafeHost(input); got != want {
			t.Errorf("IsSafeHost(%q) = %v, want %v", input, got, want)
		}
	}
}
