package controlplane

import "testing"

func TestEnrollmentStore_IssueExistsTake(t *testing.T) {
	s := NewEnrollmentStore()
	token := s.Issue("server-1")

	if !s.Exists(token) {
		t.Fatalf("Exists(token) = false right after Issue")
	}
	serverID, ok := s.Take(token)
	if !ok || serverID != "server-1" {
		t.Fatalf("Take(token) = %q, %v, want server-1, true", serverID, ok)
	}

	// Single-use: a second Take must fail.
	if _, ok := s.Take(token); ok {
		t.Errorf("second Take(token) succeeded, want single-use failure")
	}
	if s.Exists(token) {
		t.Errorf("Exists(token) = true after Take, want false")
	}
}

func TestEnrollmentStore_UnknownToken(t *testing.T) {
	s := NewEnrollmentStore()
	if s.Exists("nope") {
		t.Errorf("Exists(unknown) = true")
	}
	if _, ok := s.Take("nope"); ok {
		t.Errorf("Take(unknown) succeeded")
	}
}
