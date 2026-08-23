// Package config loads and persists the concierge equivalent of the C#
// broker's appsettings.json "Broker" section (research-sso-broker.md §5.1).
// It owns the static server registry (BrokerServerOptions[]) and the
// organization-wide OIDC settings, both of which admin endpoints mutate at
// runtime and persist back to disk — matching the C# broker's
// SaveBrokerConfigurationAsync, but (unlike the C# original) with a mutex
// around every read-modify-write-save sequence, since research-sso-broker.md
// §8 flags the C# broker's total absence of locking around BrokerOptions
// mutation as a real bug, not a behavior to reproduce silently.
package config

import (
	"encoding/json"
	"errors"
	"fmt"
	"net"
	"net/http"
	"net/url"
	"os"
	"path/filepath"
	"strings"
	"sync"
)

// ErrDuplicateServerID is returned by AddServer when a server with the same
// Id is already registered.
var ErrDuplicateServerID = errors.New("config: server id already exists")

// ErrServerNotFound is returned by RemoveServer when no server with the
// given id is registered.
var ErrServerNotFound = errors.New("config: server not found")

// ProviderConfig is one OIDC identity provider, matching the C# broker's
// ProviderOptions (research-sso-broker.md §5.1). Field names are PascalCase
// to stay compatible with existing appsettings.json files written by the C#
// broker.
type ProviderConfig struct {
	Id                   string   `json:"Id"`
	Label                string   `json:"Label,omitempty"`
	Issuer               string   `json:"Issuer"`
	Audience             string   `json:"Audience"`
	ClientSecret         string   `json:"ClientSecret,omitempty"`
	WebClientId          string   `json:"WebClientId,omitempty"`
	WebClientSecret      string   `json:"WebClientSecret,omitempty"`
	TokenEndpoint        string   `json:"TokenEndpoint,omitempty"`
	JwksUri              string   `json:"JwksUri"`
	AllowedHostedDomains []string `json:"AllowedHostedDomains,omitempty"`
	AllowedGroups        []string `json:"AllowedGroups,omitempty"`
}

// Copy returns a deep copy of p, matching ProviderOptions.Copy in
// ControlPlane.cs (used whenever a provider list needs to be duplicated
// rather than aliased, e.g. organization -> local bootstrap server).
func (p ProviderConfig) Copy() ProviderConfig {
	out := p
	if p.AllowedHostedDomains != nil {
		out.AllowedHostedDomains = append([]string(nil), p.AllowedHostedDomains...)
	}
	if p.AllowedGroups != nil {
		out.AllowedGroups = append([]string(nil), p.AllowedGroups...)
	}
	return out
}

// IsWebConfigured mirrors the C# broker's web credential check. Web secrets
// remain broker-side and are never emitted in client configuration.
func (p ProviderConfig) IsWebConfigured() bool {
	return p.WebClientId != "" && p.WebClientSecret != "" && isAbsoluteHTTPS(p.TokenEndpoint)
}

// IsStructurallyValid mirrors ProviderOptions.IsStructurallyValid: non-blank
// id, absolute-HTTPS issuer/JWKS URI, and either a native audience or a
// complete browser provider credential.
func (p ProviderConfig) IsStructurallyValid() bool {
	return p.Id != "" && isAbsoluteHTTPS(p.Issuer) && (p.Audience != "" || p.IsWebConfigured()) && isAbsoluteHTTPS(p.JwksUri)
}

func isAbsoluteHTTPS(raw string) bool {
	u, err := url.ParseRequestURI(raw)
	return err == nil && u.IsAbs() && strings.EqualFold(u.Scheme, "https") && u.Host != ""
}

// ServerConfig is one Basis game server's admission configuration, matching
// BrokerServerOptions.
type ServerConfig struct {
	Id                                    string `json:"Id"`
	TicketSigningKeyEnvironmentVariable   string `json:"TicketSigningKeyEnvironmentVariable,omitempty"`
	TransportPublicKeyEnvironmentVariable string `json:"TransportPublicKeyEnvironmentVariable,omitempty"`
	TicketSigningKey                      string `json:"TicketSigningKey,omitempty"`
	TransportPublicKey                    string `json:"TransportPublicKey,omitempty"`
	// WebSocketUri and ServerInfoUri are explicit browser endpoints. They are
	// intentionally not inferred from the UDP host/port: TLS termination and
	// ingress paths are deployment-specific (web-support contract).
	WebSocketUri  string           `json:"WebSocketUri,omitempty"`
	ServerInfoUri string           `json:"ServerInfoUri,omitempty"`
	Providers     []ProviderConfig `json:"Providers,omitempty"`
	// FromMeeting is true when this entry was created by
	// internal/api.CreateMeeting (i.e. it exists only because a
	// controlplane.MeetingRecord with the same id does) rather than
	// hand-authored by an operator in appsettings.json ahead of time. Every
	// meeting concierge creates registers matching entries in both the
	// server registry (so /admission/{serverId} can find its keys) and the
	// meeting control plane, by design (design.md §4.1/§4.2) — that
	// same-id-in-both-stores pairing is therefore expected and must not
	// trip cmd/server's checkNoStaticMeetingIDCollision startup guard, which
	// exists to catch a genuinely ambiguous case: an operator's static
	// Servers[] entry independently colliding with an unrelated meeting id.
	FromMeeting bool `json:"FromMeeting,omitempty"`
}

// EffectiveTicketSigningKey returns the literal TicketSigningKey if set,
// otherwise the value of the environment variable named by
// TicketSigningKeyEnvironmentVariable (re-read on every call, never
// cached — matching EffectiveTicketSigningKey in BrokerServerOptions).
func (s ServerConfig) EffectiveTicketSigningKey() string {
	if strings.TrimSpace(s.TicketSigningKey) != "" {
		return s.TicketSigningKey
	}
	return os.Getenv(s.TicketSigningKeyEnvironmentVariable)
}

// EffectiveTransportPublicKey is the TransportPublicKey analogue of
// EffectiveTicketSigningKey.
func (s ServerConfig) EffectiveTransportPublicKey() string {
	if strings.TrimSpace(s.TransportPublicKey) != "" {
		return s.TransportPublicKey
	}
	return os.Getenv(s.TransportPublicKeyEnvironmentVariable)
}

// HasTicketSigningKey mirrors HasTicketSigningKey: a 32+ character
// effective key counts as configured.
func (s ServerConfig) HasTicketSigningKey() bool {
	return len(s.EffectiveTicketSigningKey()) >= 32
}

// HasTransportPublicKey mirrors HasTransportPublicKey.
func (s ServerConfig) HasTransportPublicKey() bool {
	return strings.TrimSpace(s.EffectiveTransportPublicKey()) != ""
}

// IsReady mirrors BrokerServerOptions.IsReady: drives /health and admission
// 503s.
func (s ServerConfig) IsReady() bool {
	return s.Id != "" && len(s.Providers) > 0 && s.HasTicketSigningKey() && s.HasTransportPublicKey()
}

// IsStructurallyValid mirrors BrokerServerOptions.IsStructurallyValid, used
// only on admin PUT /admin/servers/{id}.
func (s ServerConfig) IsStructurallyValid() (bool, string) {
	if s.Id == "" || !isValidServerID(s.Id) {
		return false, "Server ID must use letters, numbers, '-' or '_'."
	}
	if strings.TrimSpace(s.TicketSigningKey) == "" && strings.TrimSpace(s.TicketSigningKeyEnvironmentVariable) == "" {
		return false, "A ticket-signing key or key environment variable is required."
	}
	if strings.TrimSpace(s.TransportPublicKey) == "" && strings.TrimSpace(s.TransportPublicKeyEnvironmentVariable) == "" {
		return false, "A transport public key or key environment variable is required."
	}
	if len(s.Providers) == 0 {
		return false, "Every provider needs an ID, issuer, client ID, and HTTPS JWKS URL."
	}
	for _, p := range s.Providers {
		if !p.IsStructurallyValid() {
			return false, "Every provider needs an ID, issuer, client ID, and HTTPS JWKS URL."
		}
	}
	if err := ValidateBrowserEndpoints(strings.TrimSpace(s.WebSocketUri), strings.TrimSpace(s.ServerInfoUri)); err != nil {
		return false, "browser endpoints are invalid: " + err.Error()
	}
	return true, ""
}

// ValidateBrowserEndpoints validates optional browser endpoints without
// requiring them for native/UDP-only servers. The rules mirror the WebGL
// client: ws is loopback-only, while remote browser connections require wss;
// server-info follows the same rule with http/https.
func ValidateBrowserEndpoints(webSocketURI, serverInfoURI string) error {
	if webSocketURI == "" && serverInfoURI == "" {
		return nil
	}
	if webSocketURI == "" || serverInfoURI == "" {
		return errors.New("both WebSocketUri and ServerInfoUri are required")
	}
	if err := validateWebSocketURI(webSocketURI); err != nil {
		return err
	}
	return validateServerInfoURI(serverInfoURI)
}

// ValidateBrowserEndpointTemplates validates the explicit URI templates used
// for Agones-managed rooms. Templates are deliberately required to carry both
// placeholders: the UDP address is not a browser endpoint, and concierge must
// never guess a scheme, ingress host, or path from it.
func ValidateBrowserEndpointTemplates(webSocketTemplate, serverInfoTemplate string) error {
	webSocketTemplate = strings.TrimSpace(webSocketTemplate)
	serverInfoTemplate = strings.TrimSpace(serverInfoTemplate)
	if webSocketTemplate == "" && serverInfoTemplate == "" {
		return nil
	}
	if webSocketTemplate == "" || serverInfoTemplate == "" {
		return errors.New("both managed WebSocket and server-info URI templates are required")
	}
	for name, template := range map[string]string{"WebSocket": webSocketTemplate, "server-info": serverInfoTemplate} {
		if !strings.Contains(template, "{host}") || !strings.Contains(template, "{port}") {
			return fmt.Errorf("managed %s URI template must contain {host} and {port}", name)
		}
	}
	// Validate the URI shape with safe stand-in values. The placeholders are
	// not decoded or otherwise interpreted; only their eventual URI position
	// is checked.
	webSocketURI := strings.NewReplacer("{host}", "room.example.invalid", "{port}", "4297").Replace(webSocketTemplate)
	serverInfoURI := strings.NewReplacer("{host}", "room.example.invalid", "{port}", "4297").Replace(serverInfoTemplate)
	if err := validateWebSocketURI(webSocketURI); err != nil {
		return fmt.Errorf("managed WebSocket URI template: %w", err)
	}
	if err := validateServerInfoURI(serverInfoURI); err != nil {
		return fmt.Errorf("managed server-info URI template: %w", err)
	}
	return nil
}

func validateWebSocketURI(raw string) error {
	u, err := url.Parse(raw)
	if err != nil || u.IsAbs() == false || u.Host == "" || u.User != nil || u.Fragment != "" || (u.Scheme != "ws" && u.Scheme != "wss") {
		return errors.New("must be an absolute ws:// or wss:// URI without user info or fragment")
	}
	if u.Scheme == "ws" && !isLoopbackHost(u.Hostname()) {
		return errors.New("ws:// is only allowed for loopback endpoints")
	}
	return nil
}

func validateServerInfoURI(raw string) error {
	u, err := url.Parse(raw)
	if err != nil || !u.IsAbs() || u.Host == "" || u.User != nil || u.Fragment != "" || (u.Scheme != "http" && u.Scheme != "https") {
		return errors.New("must be an absolute http:// or https:// URI without user info or fragment")
	}
	if u.Scheme == "http" && !isLoopbackHost(u.Hostname()) {
		return errors.New("http:// is only allowed for loopback endpoints")
	}
	return nil
}

func isLoopbackHost(host string) bool {
	if strings.EqualFold(host, "localhost") {
		return true
	}
	ip := net.ParseIP(host)
	return ip != nil && ip.IsLoopback()
}

func isValidServerID(id string) bool {
	for _, c := range id {
		if !(c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z' || c >= '0' && c <= '9' || c == '-' || c == '_') {
			return false
		}
	}
	return id != ""
}

// OrganizationConfig is the organization-wide OIDC settings, matching
// OrganizationOptions.
type OrganizationConfig struct {
	DisplayName       string           `json:"DisplayName,omitempty"`
	DefaultProviderId string           `json:"DefaultProviderId,omitempty"`
	Providers         []ProviderConfig `json:"Providers,omitempty"`
}

// IsStructurallyValid mirrors OrganizationOptions.IsStructurallyValid.
func (o OrganizationConfig) IsStructurallyValid() (bool, string) {
	if len(o.Providers) == 0 {
		return false, "Configure at least one valid identity provider."
	}
	for _, p := range o.Providers {
		if !p.IsStructurallyValid() {
			return false, "Configure at least one valid identity provider."
		}
	}
	if o.DefaultProviderId != "" {
		found := false
		for _, p := range o.Providers {
			if strings.EqualFold(p.Id, o.DefaultProviderId) {
				found = true
				break
			}
		}
		if !found {
			return false, "The default provider must be enabled."
		}
	}
	return true, ""
}

// BrokerConfig is the top-level "Broker" section of appsettings.json.
type BrokerConfig struct {
	PublicBaseUrl                 string              `json:"PublicBaseUrl,omitempty"`
	AllowedWebOrigins             []string            `json:"AllowedWebOrigins,omitempty"`
	TrustedProxyCIDRs             []string            `json:"TrustedProxyCIDRs,omitempty"`
	ClientConfigDirectory         string              `json:"ClientConfigDirectory,omitempty"`
	AdminTokenEnvironmentVariable string              `json:"AdminTokenEnvironmentVariable,omitempty"`
	AllowUnauthenticatedAdmin     bool                `json:"AllowUnauthenticatedAdmin,omitempty"`
	Servers                       []ServerConfig      `json:"Servers,omitempty"`
	Organization                  *OrganizationConfig `json:"Organization,omitempty"`
	// These templates are used only for concierge-managed rooms when their
	// Agones TCP port becomes known. Both must be explicit; {host} and {port}
	// are replaced, and no scheme/ingress path is guessed.
	ManagedWebSocketUriTemplate  string `json:"ManagedWebSocketUriTemplate,omitempty"`
	ManagedServerInfoUriTemplate string `json:"ManagedServerInfoUriTemplate,omitempty"`
}

type fileWrapper struct {
	Broker BrokerConfig `json:"Broker"`
}

// Store holds the live BrokerConfig, guarding every mutation with a mutex
// and persisting each change back to disk.
type Store struct {
	mu   sync.Mutex
	path string
	cfg  BrokerConfig
}

// Load reads path (the appsettings.json-equivalent file) if it exists, or
// starts from a zero-value BrokerConfig if it does not — matching the C#
// broker's IOptions<T> behavior when the "Broker" section is absent.
func Load(path string) (*Store, error) {
	s := &Store{path: path}
	data, err := os.ReadFile(path)
	if errors.Is(err, os.ErrNotExist) {
		return s, nil
	}
	if err != nil {
		return nil, fmt.Errorf("config: read %s: %w", path, err)
	}
	var wrapper fileWrapper
	if err := json.Unmarshal(data, &wrapper); err != nil {
		return nil, fmt.Errorf("config: parse %s: %w", path, err)
	}
	for _, server := range wrapper.Broker.Servers {
		if !isValidServerID(server.Id) {
			return nil, fmt.Errorf("config: server %q has an invalid ID", server.Id)
		}
		if err := ValidateBrowserEndpoints(strings.TrimSpace(server.WebSocketUri), strings.TrimSpace(server.ServerInfoUri)); err != nil {
			return nil, fmt.Errorf("config: server %q browser endpoints: %w", server.Id, err)
		}
	}
	if err := ValidateBrowserEndpointTemplates(wrapper.Broker.ManagedWebSocketUriTemplate, wrapper.Broker.ManagedServerInfoUriTemplate); err != nil {
		return nil, fmt.Errorf("config: managed browser endpoint templates: %w", err)
	}
	for _, raw := range wrapper.Broker.TrustedProxyCIDRs {
		if _, _, err := net.ParseCIDR(strings.TrimSpace(raw)); err != nil {
			return nil, fmt.Errorf("config: invalid trusted proxy CIDR %q: %w", raw, err)
		}
	}
	s.cfg = wrapper.Broker
	return s, nil
}

// save writes the current config atomically (path+".tmp" then rename) and
// chmods it 0600. research-sso-broker.md §5.3/§8 explicitly flags that the
// C# broker never chmods appsettings.json even though it can contain
// plaintext signing/private keys once any control-plane meeting exists —
// concierge fixes that rather than reproducing it, matching
// control-plane.json's existing 0600 treatment.
func (s *Store) save() error {
	if s.path == "" {
		return nil
	}
	data, err := json.MarshalIndent(fileWrapper{Broker: s.cfg}, "", "  ")
	if err != nil {
		return err
	}
	if dir := filepath.Dir(s.path); dir != "." {
		if err := os.MkdirAll(dir, 0o755); err != nil {
			return err
		}
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

// GetServers returns a snapshot copy of the configured servers.
func (s *Store) GetServers() []ServerConfig {
	s.mu.Lock()
	defer s.mu.Unlock()
	return append([]ServerConfig(nil), s.cfg.Servers...)
}

// FindServer returns the server with the given id (ordinal exact match),
// and whether it was found.
func (s *Store) FindServer(id string) (ServerConfig, bool) {
	s.mu.Lock()
	defer s.mu.Unlock()
	return s.findServerLocked(id)
}

func (s *Store) findServerLocked(id string) (ServerConfig, bool) {
	for _, server := range s.cfg.Servers {
		if server.Id == id {
			return server, true
		}
	}
	return ServerConfig{}, false
}

// GetOrganization mirrors BrokerOptions.GetOrganization: if Organization has
// no providers configured, synthesize one from the first Servers[] entry
// that has any providers (back-compat with pre-organization deployments).
func (s *Store) GetOrganization() OrganizationConfig {
	s.mu.Lock()
	defer s.mu.Unlock()
	if s.cfg.Organization != nil && len(s.cfg.Organization.Providers) > 0 {
		return *s.cfg.Organization
	}
	for _, server := range s.cfg.Servers {
		if len(server.Providers) > 0 {
			providers := make([]ProviderConfig, len(server.Providers))
			defaultID := ""
			for i, p := range server.Providers {
				providers[i] = p.Copy()
				if i == 0 {
					defaultID = p.Id
				}
			}
			return OrganizationConfig{DefaultProviderId: defaultID, Providers: providers}
		}
	}
	return OrganizationConfig{Providers: []ProviderConfig{}}
}

// SetOrganization validates and stores organization, mirrors the C#
// PUT /admin/organization side effect of overwriting the "local" bootstrap
// server's Providers, and persists.
func (s *Store) SetOrganization(organization OrganizationConfig) (bool, string) {
	if ok, msg := organization.IsStructurallyValid(); !ok {
		return false, msg
	}
	s.mu.Lock()
	defer s.mu.Unlock()
	s.cfg.Organization = &organization
	for i := range s.cfg.Servers {
		if s.cfg.Servers[i].Id == "local" {
			copied := make([]ProviderConfig, len(organization.Providers))
			for j, p := range organization.Providers {
				copied[j] = p.Copy()
			}
			s.cfg.Servers[i].Providers = copied
			break
		}
	}
	if err := s.save(); err != nil {
		return false, err.Error()
	}
	return true, ""
}

// AddServer appends server, returning ErrDuplicateServerID if the id is
// already registered, and persists on success.
func (s *Store) AddServer(server ServerConfig) error {
	s.mu.Lock()
	defer s.mu.Unlock()
	if _, ok := s.findServerLocked(server.Id); ok {
		return ErrDuplicateServerID
	}
	s.cfg.Servers = append(s.cfg.Servers, server)
	if err := s.save(); err != nil {
		s.cfg.Servers = s.cfg.Servers[:len(s.cfg.Servers)-1]
		return err
	}
	return nil
}

// RemoveServer deletes the server with id, returning ErrServerNotFound if
// none exists, and persists on success. It does not persist if save fails;
// the removal is rolled back.
func (s *Store) RemoveServer(id string) error {
	s.mu.Lock()
	defer s.mu.Unlock()
	idx := -1
	for i, server := range s.cfg.Servers {
		if server.Id == id {
			idx = i
			break
		}
	}
	if idx < 0 {
		return ErrServerNotFound
	}
	removed := s.cfg.Servers[idx]
	s.cfg.Servers = append(s.cfg.Servers[:idx:idx], s.cfg.Servers[idx+1:]...)
	if err := s.save(); err != nil {
		// Roll back the in-memory removal.
		s.cfg.Servers = append(s.cfg.Servers, ServerConfig{})
		copy(s.cfg.Servers[idx+1:], s.cfg.Servers[idx:])
		s.cfg.Servers[idx] = removed
		return err
	}
	return nil
}

// UpsertServer inserts or replaces the server with the given id (by id,
// matching the C# broker's "path id wins" semantics — callers should set
// server.Id before calling), returning a validation message if
// structurally invalid.
func (s *Store) UpsertServer(server ServerConfig) (bool, string) {
	if ok, msg := server.IsStructurallyValid(); !ok {
		return false, msg
	}
	s.mu.Lock()
	defer s.mu.Unlock()
	for i, existing := range s.cfg.Servers {
		if existing.Id == server.Id {
			s.cfg.Servers[i] = server
			if err := s.save(); err != nil {
				return false, err.Error()
			}
			return true, ""
		}
	}
	s.cfg.Servers = append(s.cfg.Servers, server)
	if err := s.save(); err != nil {
		return false, err.Error()
	}
	return true, ""
}

// UpdateBrowserEndpoints updates only the explicit browser endpoint pair for
// a registered server. It is used when Agones assigns a managed room's TCP
// port; the rest of the admission configuration (especially its keys) stays
// untouched. The pair is validated before persistence.
func (s *Store) UpdateBrowserEndpoints(id, webSocketURI, serverInfoURI string) bool {
	webSocketURI = strings.TrimSpace(webSocketURI)
	serverInfoURI = strings.TrimSpace(serverInfoURI)
	if ValidateBrowserEndpoints(webSocketURI, serverInfoURI) != nil {
		return false
	}
	s.mu.Lock()
	defer s.mu.Unlock()
	for i := range s.cfg.Servers {
		if s.cfg.Servers[i].Id != id {
			continue
		}
		previous := s.cfg.Servers[i]
		s.cfg.Servers[i].WebSocketUri = webSocketURI
		s.cfg.Servers[i].ServerInfoUri = serverInfoURI
		if err := s.save(); err != nil {
			s.cfg.Servers[i] = previous
			return false
		}
		return true
	}
	return false
}

// ClientConfigDirectory returns the configured client-config base
// directory, or "" if unset.
func (s *Store) ClientConfigDirectory() string {
	s.mu.Lock()
	defer s.mu.Unlock()
	return s.cfg.ClientConfigDirectory
}

// ClientConfigPath returns the on-disk path for serverId's client-config
// file, and whether the directory is configured and serverId is a known
// server.
func (s *Store) ClientConfigPath(serverId string) (string, bool) {
	if !isValidServerID(serverId) {
		return "", false
	}
	s.mu.Lock()
	dir := s.cfg.ClientConfigDirectory
	_, known := s.findServerLocked(serverId)
	s.mu.Unlock()
	if strings.TrimSpace(dir) == "" || !known {
		return "", false
	}
	base, err := filepath.Abs(dir)
	if err != nil {
		return "", false
	}
	candidate := filepath.Join(base, serverId+".json")
	rel, err := filepath.Rel(base, candidate)
	if err != nil || rel == ".." || strings.HasPrefix(rel, ".."+string(os.PathSeparator)) {
		return "", false
	}
	return candidate, true
}

// PublicBaseUrl returns the configured public base URL, or "".
func (s *Store) PublicBaseUrl() string {
	s.mu.Lock()
	defer s.mu.Unlock()
	return s.cfg.PublicBaseUrl
}

// TrustedProxyCIDRs returns the configured networks whose forwarded headers
// may be used when generating public links. An empty list deliberately means
// that client-supplied X-Forwarded-* headers are ignored.
func (s *Store) TrustedProxyCIDRs() []string {
	s.mu.Lock()
	defer s.mu.Unlock()
	return append([]string(nil), s.cfg.TrustedProxyCIDRs...)
}

// AllowedWebOrigins returns a copy of the exact origins permitted for browser
// CORS and redirect callbacks.
func (s *Store) AllowedWebOrigins() []string {
	s.mu.Lock()
	defer s.mu.Unlock()
	return append([]string(nil), s.cfg.AllowedWebOrigins...)
}

// ManagedBrowserEndpointTemplates returns the explicit endpoint templates
// used for Agones-managed rooms. Empty values are intentional: a meeting may
// provide concrete endpoints itself, and concierge never infers an origin
// from the UDP address.
func (s *Store) ManagedBrowserEndpointTemplates() (webSocketURI, serverInfoURI string) {
	s.mu.Lock()
	defer s.mu.Unlock()
	return s.cfg.ManagedWebSocketUriTemplate, s.cfg.ManagedServerInfoUriTemplate
}

// AllowUnauthenticatedAdmin reports the configured dev-only admin bypass.
func (s *Store) AllowUnauthenticatedAdmin() bool {
	s.mu.Lock()
	defer s.mu.Unlock()
	return s.cfg.AllowUnauthenticatedAdmin
}

// AdminTokenEnvironmentVariable returns the configured env var name holding
// the admin bearer token.
func (s *Store) AdminTokenEnvironmentVariable() string {
	s.mu.Lock()
	defer s.mu.Unlock()
	return s.cfg.AdminTokenEnvironmentVariable
}

// AdminAuthorized mirrors AdminAuthorized in Program.cs exactly: dev-only
// bypass, minimum 32-char configured token (re-read from the environment on
// every call), case-insensitive "Bearer " prefix, constant-time comparison.
func (s *Store) AdminAuthorized(r *http.Request) bool {
	if s.AllowUnauthenticatedAdmin() {
		return true
	}
	configured := os.Getenv(s.AdminTokenEnvironmentVariable())
	if len(configured) < 32 {
		return false
	}
	authorization := r.Header.Get("Authorization")
	const prefix = "Bearer "
	if len(authorization) < len(prefix) || !strings.EqualFold(authorization[:len(prefix)], prefix) {
		return false
	}
	supplied := authorization[len(prefix):]
	return constantTimeStringsEqual(configured, supplied)
}
