// Package controlplane manages the meeting/enrollment control-plane state
// that ControlPlane.cs owns in the C# broker: persistent meeting records
// (control-plane.json), the single-use enrollment-token store, and the
// id/key generation helpers meetings need. It never returns anything to
// public endpoints unfiltered — MeetingRecord carries the meeting's
// password and signing/transport private key, which only the admin API's
// view conversion (internal/api) is allowed to see and must never echo back
// as-is (see MeetingView in research-sso-broker.md §2.2/§7-3).
package controlplane

import (
	"crypto/subtle"
	"encoding/json"
	"errors"
	"fmt"
	"log"
	"os"
	"path/filepath"
	"sort"
	"sync"
	"time"
)

// ErrDuplicateMeetingID is returned by Add when a meeting with the same id
// already exists, matching MeetingStore.Add's InvalidOperationException.
var ErrDuplicateMeetingID = errors.New("controlplane: meeting id already exists")

// MeetingRecord is the persistent state for one meeting/room. Field names
// are PascalCase and the JSON root is {"Meetings":[...]}, matching the C#
// broker's control-plane.json exactly (research-sso-broker.md §5.3/§7-12)
// so an existing deployment's file can be read without migration.
type MeetingRecord struct {
	Id                  string    `json:"Id"`
	Title               string    `json:"Title"`
	Status              string    `json:"Status"`
	StatusDetail        string    `json:"StatusDetail"`
	Host                string    `json:"Host"`
	Port                uint16    `json:"Port"`
	Password            string    `json:"Password"`
	InviteToken         string    `json:"InviteToken"`
	TicketSigningKey    string    `json:"TicketSigningKey"`
	TransportPrivateKey string    `json:"TransportPrivateKey"`
	TransportPublicKey  string    `json:"TransportPublicKey"`
	CreatedAt           time.Time `json:"CreatedAt"`
	UpdatedAt           time.Time `json:"UpdatedAt"`
}

type controlPlaneState struct {
	Meetings []MeetingRecord `json:"Meetings"`
}

// Store is the meeting control plane: a mutex-guarded in-memory list
// persisted to path as JSON, matching MeetingStore's single-lock design
// (research-sso-broker.md §3.1/§6).
type Store struct {
	mu    sync.Mutex
	path  string
	state controlPlaneState
}

// NewStore creates a Store backed by path, loading any existing state.
// Load failures are logged and swallowed (the store starts empty), matching
// MeetingStore's constructor.
func NewStore(path string) *Store {
	s := &Store{path: path, state: controlPlaneState{Meetings: []MeetingRecord{}}}
	s.load()
	return s
}

func (s *Store) load() {
	data, err := os.ReadFile(s.path)
	if errors.Is(err, os.ErrNotExist) {
		return
	}
	if err != nil {
		log.Printf("concierge control plane: failed to load %q: %v", s.path, err)
		return
	}
	var loaded controlPlaneState
	if err := json.Unmarshal(data, &loaded); err != nil {
		log.Printf("concierge control plane: failed to parse %q: %v", s.path, err)
		return
	}
	if loaded.Meetings == nil {
		loaded.Meetings = []MeetingRecord{}
	}
	s.state = loaded
}

// saveLocked persists s.state atomically (path+".tmp" then rename) and
// chmods it 0600, matching MeetingStore.SaveLocked. Must be called with
// s.mu held.
func (s *Store) saveLocked() error {
	if dir := filepath.Dir(s.path); dir != "." {
		if err := os.MkdirAll(dir, 0o755); err != nil {
			return err
		}
	}
	data, err := json.MarshalIndent(s.state, "", "  ")
	if err != nil {
		return err
	}
	tmp := s.path + ".tmp"
	if err := os.WriteFile(tmp, data, 0o600); err != nil {
		return err
	}
	if err := os.Rename(tmp, s.path); err != nil {
		return err
	}
	return os.Chmod(s.path, 0o600)
}

// List returns every meeting, newest-CreatedAt-first, as copies.
func (s *Store) List() []MeetingRecord {
	s.mu.Lock()
	defer s.mu.Unlock()
	out := append([]MeetingRecord(nil), s.state.Meetings...)
	sort.SliceStable(out, func(i, j int) bool { return out[i].CreatedAt.After(out[j].CreatedAt) })
	return out
}

// Find returns the meeting with the given id (ordinal exact match).
func (s *Store) Find(id string) (MeetingRecord, bool) {
	s.mu.Lock()
	defer s.mu.Unlock()
	return s.findLocked(id)
}

func (s *Store) findLocked(id string) (MeetingRecord, bool) {
	for _, m := range s.state.Meetings {
		if m.Id == id {
			return m, true
		}
	}
	return MeetingRecord{}, false
}

// FindInvite finds the meeting whose InviteToken equals token, using a
// constant-time comparison against every stored token to resist timing
// attacks on invite-token guessing — matching MeetingStore.FindInvite
// (research-sso-broker.md §3.1/§7-13). This is an O(n) scan by design, same
// as the C# original.
func (s *Store) FindInvite(token string) (MeetingRecord, bool) {
	s.mu.Lock()
	defer s.mu.Unlock()
	for _, m := range s.state.Meetings {
		if constantTimeEqual(m.InviteToken, token) {
			return m, true
		}
	}
	return MeetingRecord{}, false
}

func constantTimeEqual(expected, actual string) bool {
	if expected == "" || actual == "" {
		return false
	}
	if len(expected) != len(actual) {
		return false
	}
	return subtle.ConstantTimeCompare([]byte(expected), []byte(actual)) == 1
}

// Exists reports whether a meeting with id exists.
func (s *Store) Exists(id string) bool {
	s.mu.Lock()
	defer s.mu.Unlock()
	_, ok := s.findLocked(id)
	return ok
}

// EnsureSingleComposeMeeting mirrors MeetingStore.EnsureSingleComposeMeeting:
// invoked once at startup for the "local" bootstrap server. If a meeting
// with id already exists, refresh its connection info whenever anything
// differs; otherwise create it, but only if no meetings exist at all yet.
func (s *Store) EnsureSingleComposeMeeting(id, title, host string, port uint16, password, transportPublicKey string) {
	s.mu.Lock()
	defer s.mu.Unlock()

	status := "ready"
	detail := "Running through the single-room Docker Compose deployment."
	if host == "" {
		status = "provisioning"
		detail = "Set BASIS_MEETING_PUBLIC_HOST before sharing an invitation."
	}

	for i, existing := range s.state.Meetings {
		if existing.Id != id {
			continue
		}
		changed := existing.Title != title ||
			existing.Host != host ||
			existing.Port != port ||
			existing.Password != password ||
			existing.TransportPublicKey != transportPublicKey ||
			existing.Status != "ready"
		if changed {
			existing.Title = title
			existing.Host = host
			existing.Port = port
			existing.Password = password
			existing.TransportPublicKey = transportPublicKey
			existing.Status = status
			existing.StatusDetail = detail
			existing.UpdatedAt = time.Now().UTC()
			s.state.Meetings[i] = existing
			if err := s.saveLocked(); err != nil {
				log.Printf("concierge control plane: failed to save %q: %v", s.path, err)
			}
		}
		return
	}
	if len(s.state.Meetings) > 0 {
		return
	}
	now := time.Now().UTC()
	s.state.Meetings = append(s.state.Meetings, MeetingRecord{
		Id:                 id,
		Title:              title,
		Host:               host,
		Port:               port,
		Password:           password,
		TransportPublicKey: transportPublicKey,
		InviteToken:        RandomToken(24),
		Status:             status,
		StatusDetail:       detail,
		CreatedAt:          now,
		UpdatedAt:          now,
	})
	if err := s.saveLocked(); err != nil {
		log.Printf("concierge control plane: failed to save %q: %v", s.path, err)
	}
}

// Add inserts meeting, returning ErrDuplicateMeetingID if its id is already
// taken, and persists on success. CreatedAt/UpdatedAt default to now if
// zero.
func (s *Store) Add(meeting MeetingRecord) error {
	s.mu.Lock()
	defer s.mu.Unlock()
	if _, ok := s.findLocked(meeting.Id); ok {
		return fmt.Errorf("%w: %s", ErrDuplicateMeetingID, meeting.Id)
	}
	now := time.Now().UTC()
	if meeting.CreatedAt.IsZero() {
		meeting.CreatedAt = now
	}
	if meeting.UpdatedAt.IsZero() {
		meeting.UpdatedAt = now
	}
	s.state.Meetings = append(s.state.Meetings, meeting)
	if err := s.saveLocked(); err != nil {
		s.state.Meetings = s.state.Meetings[:len(s.state.Meetings)-1]
		return err
	}
	return nil
}

// UpdateStatus updates the meeting's status/detail, and optionally its
// host/port (only when host is non-empty / port is non-zero, matching
// MeetingStore.UpdateStatus's optional-parameter semantics). It returns
// false if the meeting does not exist.
func (s *Store) UpdateStatus(id, status, detail, host string, port uint16) bool {
	s.mu.Lock()
	defer s.mu.Unlock()
	for i, m := range s.state.Meetings {
		if m.Id != id {
			continue
		}
		m.Status = status
		m.StatusDetail = detail
		if host != "" {
			m.Host = host
		}
		if port > 0 {
			m.Port = port
		}
		m.UpdatedAt = time.Now().UTC()
		s.state.Meetings[i] = m
		if err := s.saveLocked(); err != nil {
			log.Printf("concierge control plane: failed to save %q: %v", s.path, err)
		}
		return true
	}
	return false
}

// Delete removes the meeting with id, returning it and true if found.
func (s *Store) Delete(id string) (MeetingRecord, bool) {
	s.mu.Lock()
	defer s.mu.Unlock()
	for i, m := range s.state.Meetings {
		if m.Id != id {
			continue
		}
		s.state.Meetings = append(s.state.Meetings[:i:i], s.state.Meetings[i+1:]...)
		if err := s.saveLocked(); err != nil {
			log.Printf("concierge control plane: failed to save %q: %v", s.path, err)
		}
		return m, true
	}
	return MeetingRecord{}, false
}
