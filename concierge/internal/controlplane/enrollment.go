package controlplane

import (
	"sync"
	"time"
)

// enrollmentTTL matches EnrollmentStore's 10-minute token lifetime.
const enrollmentTTL = 10 * time.Minute

type enrollment struct {
	serverID  string
	expiresAt time.Time
}

// EnrollmentStore issues single-use, time-limited enrollment tokens,
// in-memory only (never persisted — a restart invalidates outstanding
// links, matching EnrollmentStore in ControlPlane.cs).
type EnrollmentStore struct {
	mu      sync.Mutex
	entries map[string]enrollment
}

// NewEnrollmentStore creates an empty EnrollmentStore.
func NewEnrollmentStore() *EnrollmentStore {
	return &EnrollmentStore{entries: make(map[string]enrollment)}
}

// Issue mints a new 32-byte random token for serverID with a 10-minute TTL.
func (s *EnrollmentStore) Issue(serverID string) string {
	token := RandomToken(32)
	s.mu.Lock()
	defer s.mu.Unlock()
	s.removeExpiredLocked()
	s.entries[token] = enrollment{serverID: serverID, expiresAt: time.Now().Add(enrollmentTTL)}
	return token
}

// Exists reports whether token is currently valid.
func (s *EnrollmentStore) Exists(token string) bool {
	s.mu.Lock()
	defer s.mu.Unlock()
	s.removeExpiredLocked()
	_, ok := s.entries[token]
	return ok
}

// Take consumes token (single-use), returning its associated serverID and
// true if it was valid.
func (s *EnrollmentStore) Take(token string) (string, bool) {
	s.mu.Lock()
	defer s.mu.Unlock()
	s.removeExpiredLocked()
	entry, ok := s.entries[token]
	if !ok {
		return "", false
	}
	delete(s.entries, token)
	return entry.serverID, true
}

func (s *EnrollmentStore) removeExpiredLocked() {
	now := time.Now()
	for token, entry := range s.entries {
		if !entry.expiresAt.After(now) {
			delete(s.entries, token)
		}
	}
}
