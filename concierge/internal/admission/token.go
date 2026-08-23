package admission

import (
	"context"
	"crypto"
	"crypto/rsa"
	"crypto/sha256"
	"encoding/base64"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"math/big"
	"net/http"
	"strings"
	"time"
)

// Provider is one OIDC identity provider's admission policy — the fields
// TokenValidator.ValidateCoreAsync needs from ProviderOptions.
type Provider struct {
	ID                   string
	Issuer               string
	Audience             string
	WebClientID          string
	JwksURI              string
	AllowedHostedDomains []string
	AllowedGroups        []string
}

// Identity is everything that survives token validation — matching
// ValidatedIdentity: no other claim is ever retained, and the raw token is
// never logged (research-sso-broker.md §2.2 "no detail leaked").
type Identity struct {
	Issuer  string
	Subject string
}

// Sentinel validation errors. The HTTP handler must not expose these to
// clients (every failure collapses to a bare 401, matching
// TokenValidator.ValidateAsync's try/catch-to-null) — they exist so unit
// tests can assert *why* validation failed without parsing strings.
var (
	ErrMalformedToken        = errors.New("admission: malformed token")
	ErrUnsupportedAlg        = errors.New("admission: unsupported alg")
	ErrUnknownIssuer         = errors.New("admission: no provider configured for issuer")
	ErrProviderMisconfigured = errors.New("admission: provider missing audience or https jwks uri")
	ErrAudienceMismatch      = errors.New("admission: audience mismatch")
	ErrExpired               = errors.New("admission: token expired or missing exp")
	ErrEmptySubject          = errors.New("admission: empty subject")
	ErrPolicyDenied          = errors.New("admission: policy check failed")
	ErrJWKSFetch             = errors.New("admission: jwks fetch failed")
	ErrKeyNotFound           = errors.New("admission: signing key not found in jwks")
	ErrSignatureInvalid      = errors.New("admission: signature invalid")
)

// Validator validates OIDC ID tokens against a per-request provider list,
// matching TokenValidator. JWKS are fetched fresh on every call (no cache),
// exactly like the C# broker (research-sso-broker.md §2.2 step 10).
type Validator struct {
	http *http.Client
}

// NewValidator returns a Validator using a 10-second HTTP timeout for JWKS
// fetches, matching TokenValidator's HttpClient.
func NewValidator() *Validator {
	return &Validator{http: &http.Client{Timeout: 10 * time.Second}}
}

type jwtHeader struct {
	Alg string `json:"alg"`
	Kid string `json:"kid"`
}

type jwks struct {
	Keys []jwksKey `json:"keys"`
}

type jwksKey struct {
	Kty string `json:"kty"`
	Kid string `json:"kid"`
	N   string `json:"n"`
	E   string `json:"e"`
}

// Validate runs the full admission check described in
// research-sso-broker.md §2.2: split/decode the JWT, require alg=RS256,
// find the provider whose Issuer exactly matches iss, check audience/exp/
// hosted-domain/group policy, fetch JWKS fresh, and verify the RS256
// signature over the literal "header.payload" substring. Any failure
// returns a non-nil error; callers must treat every error identically (401)
// and must not surface err.Error() to the client.
func (v *Validator) Validate(ctx context.Context, idToken string, providers []Provider) (*Identity, error) {
	parts := strings.Split(idToken, ".")
	if len(parts) != 3 {
		return nil, ErrMalformedToken
	}

	headerBytes, err := decodeSegment(parts[0])
	if err != nil {
		return nil, fmt.Errorf("%w: %v", ErrMalformedToken, err)
	}
	payloadBytes, err := decodeSegment(parts[1])
	if err != nil {
		return nil, fmt.Errorf("%w: %v", ErrMalformedToken, err)
	}

	var header jwtHeader
	if err := json.Unmarshal(headerBytes, &header); err != nil {
		return nil, fmt.Errorf("%w: %v", ErrMalformedToken, err)
	}
	if header.Alg != "RS256" {
		return nil, ErrUnsupportedAlg
	}

	var payload map[string]any
	if err := json.Unmarshal(payloadBytes, &payload); err != nil {
		return nil, fmt.Errorf("%w: %v", ErrMalformedToken, err)
	}

	issuer, _ := payload["iss"].(string)
	provider, ok := findProviderByIssuer(providers, issuer)
	if !ok {
		return nil, ErrUnknownIssuer
	}
	if (strings.TrimSpace(provider.Audience) == "" && strings.TrimSpace(provider.WebClientID) == "") || !strings.HasPrefix(provider.JwksURI, "https://") {
		return nil, ErrProviderMisconfigured
	}
	if !audienceMatchesAny(payload, provider.Audience, provider.WebClientID) {
		return nil, ErrAudienceMismatch
	}
	if isExpired(payload) {
		return nil, ErrExpired
	}
	subject, _ := payload["sub"].(string)
	if strings.TrimSpace(subject) == "" {
		return nil, ErrEmptySubject
	}
	if !policyAllows(payload, "hd", provider.AllowedHostedDomains) || !policyAllows(payload, "groups", provider.AllowedGroups) {
		return nil, ErrPolicyDenied
	}

	pub, err := v.findSigningKey(ctx, provider.JwksURI, header.Kid)
	if err != nil {
		return nil, err
	}

	signature, err := decodeSegment(parts[2])
	if err != nil {
		return nil, fmt.Errorf("%w: %v", ErrMalformedToken, err)
	}
	hashed := sha256.Sum256([]byte(parts[0] + "." + parts[1]))
	if err := rsa.VerifyPKCS1v15(pub, crypto.SHA256, hashed[:], signature); err != nil {
		return nil, ErrSignatureInvalid
	}

	return &Identity{Issuer: issuer, Subject: subject}, nil
}

func (v *Validator) findSigningKey(ctx context.Context, jwksURI, kid string) (*rsa.PublicKey, error) {
	req, err := http.NewRequestWithContext(ctx, http.MethodGet, jwksURI, nil)
	if err != nil {
		return nil, fmt.Errorf("%w: %v", ErrJWKSFetch, err)
	}
	resp, err := v.http.Do(req)
	if err != nil {
		return nil, fmt.Errorf("%w: %v", ErrJWKSFetch, err)
	}
	defer resp.Body.Close()
	if resp.StatusCode != http.StatusOK {
		return nil, fmt.Errorf("%w: status %d", ErrJWKSFetch, resp.StatusCode)
	}
	body, err := io.ReadAll(resp.Body)
	if err != nil {
		return nil, fmt.Errorf("%w: %v", ErrJWKSFetch, err)
	}
	var set jwks
	if err := json.Unmarshal(body, &set); err != nil {
		return nil, fmt.Errorf("%w: %v", ErrJWKSFetch, err)
	}
	for _, key := range set.Keys {
		if key.Kty == "RSA" && key.Kid == kid {
			nBytes, err := decodeSegment(key.N)
			if err != nil {
				return nil, fmt.Errorf("%w: %v", ErrKeyNotFound, err)
			}
			eBytes, err := decodeSegment(key.E)
			if err != nil {
				return nil, fmt.Errorf("%w: %v", ErrKeyNotFound, err)
			}
			e := 0
			for _, b := range eBytes {
				e = e<<8 | int(b)
			}
			return &rsa.PublicKey{N: new(big.Int).SetBytes(nBytes), E: e}, nil
		}
	}
	return nil, ErrKeyNotFound
}

func findProviderByIssuer(providers []Provider, issuer string) (Provider, bool) {
	for _, p := range providers {
		if p.Issuer == issuer {
			return p, true
		}
	}
	return Provider{}, false
}

// decodeSegment base64url-decodes a JWT segment. Using
// base64.RawURLEncoding directly (rather than the C# broker's manual
// pad-by-length-mod-4 approach) also fixes the C# implementation's silent
// mis-decode of a length%4==1 segment: RawURLEncoding correctly rejects it
// as invalid input instead (research-sso-broker.md §2.2 step 2 flags this
// as something a Go port should do).
func decodeSegment(s string) ([]byte, error) {
	return base64.RawURLEncoding.DecodeString(s)
}

func audienceMatches(payload map[string]any, expected string) bool {
	switch aud := payload["aud"].(type) {
	case string:
		return aud == expected
	case []any:
		for _, v := range aud {
			if s, ok := v.(string); ok && s == expected {
				return true
			}
		}
	}
	return false
}

func audienceMatchesAny(payload map[string]any, expected ...string) bool {
	for _, candidate := range expected {
		if strings.TrimSpace(candidate) != "" && audienceMatches(payload, candidate) {
			return true
		}
	}
	return false
}

func isExpired(payload map[string]any) bool {
	expValue, ok := payload["exp"]
	if !ok {
		return true
	}
	expFloat, ok := expValue.(float64)
	if !ok {
		return true
	}
	return int64(expFloat) <= time.Now().UTC().Unix()
}

// policyAllows mirrors the Any() helper: an empty allow-list always passes;
// otherwise the claim (string or string array) must intersect it.
func policyAllows(payload map[string]any, claim string, allowed []string) bool {
	if len(allowed) == 0 {
		return true
	}
	value, ok := payload[claim]
	if !ok {
		return false
	}
	contains := func(s string) bool {
		for _, a := range allowed {
			if a == s {
				return true
			}
		}
		return false
	}
	switch v := value.(type) {
	case string:
		return contains(v)
	case []any:
		for _, item := range v {
			if s, ok := item.(string); ok && contains(s) {
				return true
			}
		}
	}
	return false
}
