package admission

import (
	"context"
	"crypto"
	"crypto/rand"
	"crypto/rsa"
	"crypto/sha256"
	"encoding/base64"
	"encoding/json"
	"errors"
	"net/http"
	"net/http/httptest"
	"testing"
	"time"
)

// testKeySet bundles an RSA keypair with a JWKS handler serving its public
// half, for building signed test JWTs against a real (httptest) JWKS
// endpoint — matching how the C# broker always fetches JWKS live rather
// than trusting a pre-parsed key (research-sso-broker.md §2.2 step 10).
type testKeySet struct {
	key *rsa.PrivateKey
	kid string
	srv *httptest.Server
}

func newTestKeySet(t *testing.T) *testKeySet {
	t.Helper()
	key, err := rsa.GenerateKey(rand.Reader, 2048)
	if err != nil {
		t.Fatalf("generate rsa key: %v", err)
	}
	ks := &testKeySet{key: key, kid: "test-kid"}
	// A real HTTPS server (self-signed cert from httptest.NewTLSServer) is
	// used deliberately: TokenValidator/Validator require an https:// JWKS
	// URI (research-sso-broker.md §2.2 step 5), so an http:// test server
	// would fail differently than a genuine misconfiguration should.
	ks.srv = httptest.NewTLSServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		w.Header().Set("Content-Type", "application/json")
		_ = json.NewEncoder(w).Encode(map[string]any{
			"keys": []map[string]string{{
				"kty": "RSA",
				"kid": ks.kid,
				"n":   base64.RawURLEncoding.EncodeToString(key.PublicKey.N.Bytes()),
				"e":   base64.RawURLEncoding.EncodeToString(bigEndianExponent(key.PublicKey.E)),
			}},
		})
	}))
	t.Cleanup(ks.srv.Close)
	return ks
}

// validator returns a Validator whose HTTP client trusts ks's self-signed
// TLS test certificate.
func (ks *testKeySet) validator() *Validator {
	v := NewValidator()
	v.http = ks.srv.Client()
	v.allowUnsafeEndpoints = true
	return v
}

func bigEndianExponent(e int) []byte {
	if e == 65537 {
		return []byte{0x01, 0x00, 0x01}
	}
	var b []byte
	for e > 0 {
		b = append([]byte{byte(e & 0xff)}, b...)
		e >>= 8
	}
	return b
}

// signToken builds "header.payload.signature" for the given claims,
// RS256-signed with ks's private key, in the exact literal-substring form
// TokenValidator verifies (research-sso-broker.md §2.2 step 10).
func signToken(t *testing.T, ks *testKeySet, header, payload map[string]any) string {
	t.Helper()
	headerJSON, err := json.Marshal(header)
	if err != nil {
		t.Fatal(err)
	}
	payloadJSON, err := json.Marshal(payload)
	if err != nil {
		t.Fatal(err)
	}
	signingInput := base64.RawURLEncoding.EncodeToString(headerJSON) + "." + base64.RawURLEncoding.EncodeToString(payloadJSON)
	hashed := sha256.Sum256([]byte(signingInput))
	sig, err := rsa.SignPKCS1v15(rand.Reader, ks.key, crypto.SHA256, hashed[:])
	if err != nil {
		t.Fatal(err)
	}
	return signingInput + "." + base64.RawURLEncoding.EncodeToString(sig)
}

func TestValidator_Validate_Success(t *testing.T) {
	ks := newTestKeySet(t)
	exp := time.Now().Add(time.Hour).Unix()
	token := signToken(t, ks,
		map[string]any{"alg": "RS256", "kid": ks.kid},
		map[string]any{"iss": "https://issuer.example", "aud": "client-1", "sub": "user-1", "exp": exp},
	)
	providers := []Provider{{Issuer: "https://issuer.example", Audience: "client-1", JwksURI: ks.srv.URL}}

	v := ks.validator()
	identity, err := v.Validate(context.Background(), token, providers)
	if err != nil {
		t.Fatalf("Validate: %v", err)
	}
	if identity.Issuer != "https://issuer.example" || identity.Subject != "user-1" {
		t.Errorf("identity = %+v, want issuer/subject from token", identity)
	}
}

func TestValidator_Validate_AudienceArray(t *testing.T) {
	ks := newTestKeySet(t)
	exp := time.Now().Add(time.Hour).Unix()
	token := signToken(t, ks,
		map[string]any{"alg": "RS256", "kid": ks.kid},
		map[string]any{"iss": "https://issuer.example", "aud": []string{"other", "client-1"}, "sub": "user-1", "exp": exp},
	)
	providers := []Provider{{Issuer: "https://issuer.example", Audience: "client-1", JwksURI: ks.srv.URL}}

	v := ks.validator()
	if _, err := v.Validate(context.Background(), token, providers); err != nil {
		t.Fatalf("Validate: %v", err)
	}
}

func TestValidator_Validate_WebClientAudience(t *testing.T) {
	ks := newTestKeySet(t)
	token := signToken(t, ks,
		map[string]any{"alg": "RS256", "kid": ks.kid},
		map[string]any{"iss": "https://issuer.example", "aud": "web-client", "sub": "user-1", "exp": time.Now().Add(time.Hour).Unix()},
	)
	providers := []Provider{{Issuer: "https://issuer.example", WebClientID: "web-client", JwksURI: ks.srv.URL}}
	if _, err := ks.validator().Validate(context.Background(), token, providers); err != nil {
		t.Fatalf("Validate web audience: %v", err)
	}
}

func TestValidator_Validate_Failures(t *testing.T) {
	ks := newTestKeySet(t)
	validExp := time.Now().Add(time.Hour).Unix()
	expiredExp := time.Now().Add(-time.Hour).Unix()
	baseProviders := []Provider{{Issuer: "https://issuer.example", Audience: "client-1", JwksURI: ks.srv.URL}}

	cases := []struct {
		name      string
		header    map[string]any
		payload   map[string]any
		providers []Provider
		wantErr   error
	}{
		{
			name:      "wrong alg",
			header:    map[string]any{"alg": "HS256", "kid": ks.kid},
			payload:   map[string]any{"iss": "https://issuer.example", "aud": "client-1", "sub": "u", "exp": validExp},
			providers: baseProviders,
			wantErr:   ErrUnsupportedAlg,
		},
		{
			name:      "unknown issuer",
			header:    map[string]any{"alg": "RS256", "kid": ks.kid},
			payload:   map[string]any{"iss": "https://other.example", "aud": "client-1", "sub": "u", "exp": validExp},
			providers: baseProviders,
			wantErr:   ErrUnknownIssuer,
		},
		{
			name:      "audience mismatch",
			header:    map[string]any{"alg": "RS256", "kid": ks.kid},
			payload:   map[string]any{"iss": "https://issuer.example", "aud": "someone-else", "sub": "u", "exp": validExp},
			providers: baseProviders,
			wantErr:   ErrAudienceMismatch,
		},
		{
			name:      "expired",
			header:    map[string]any{"alg": "RS256", "kid": ks.kid},
			payload:   map[string]any{"iss": "https://issuer.example", "aud": "client-1", "sub": "u", "exp": expiredExp},
			providers: baseProviders,
			wantErr:   ErrExpired,
		},
		{
			name:      "missing exp",
			header:    map[string]any{"alg": "RS256", "kid": ks.kid},
			payload:   map[string]any{"iss": "https://issuer.example", "aud": "client-1", "sub": "u"},
			providers: baseProviders,
			wantErr:   ErrExpired,
		},
		{
			name:      "empty subject",
			header:    map[string]any{"alg": "RS256", "kid": ks.kid},
			payload:   map[string]any{"iss": "https://issuer.example", "aud": "client-1", "sub": "", "exp": validExp},
			providers: baseProviders,
			wantErr:   ErrEmptySubject,
		},
		{
			name:    "hosted domain denied",
			header:  map[string]any{"alg": "RS256", "kid": ks.kid},
			payload: map[string]any{"iss": "https://issuer.example", "aud": "client-1", "sub": "u", "exp": validExp, "hd": "not-allowed.example"},
			providers: []Provider{{Issuer: "https://issuer.example", Audience: "client-1", JwksURI: ks.srv.URL,
				AllowedHostedDomains: []string{"allowed.example"}}},
			wantErr: ErrPolicyDenied,
		},
		{
			name:      "unknown kid",
			header:    map[string]any{"alg": "RS256", "kid": "not-the-real-kid"},
			payload:   map[string]any{"iss": "https://issuer.example", "aud": "client-1", "sub": "u", "exp": validExp},
			providers: baseProviders,
			wantErr:   ErrKeyNotFound,
		},
	}

	for _, tc := range cases {
		t.Run(tc.name, func(t *testing.T) {
			token := signToken(t, ks, tc.header, tc.payload)
			v := ks.validator()
			_, err := v.Validate(context.Background(), token, tc.providers)
			if err == nil {
				t.Fatalf("Validate() succeeded, want error %v", tc.wantErr)
			}
			if !errors.Is(err, tc.wantErr) {
				t.Fatalf("Validate() error = %v, want %v", err, tc.wantErr)
			}
		})
	}
}

func TestValidator_Validate_HostedDomainAllowed(t *testing.T) {
	ks := newTestKeySet(t)
	exp := time.Now().Add(time.Hour).Unix()
	token := signToken(t, ks,
		map[string]any{"alg": "RS256", "kid": ks.kid},
		map[string]any{"iss": "https://issuer.example", "aud": "client-1", "sub": "u", "exp": exp, "hd": "allowed.example"},
	)
	providers := []Provider{{Issuer: "https://issuer.example", Audience: "client-1", JwksURI: ks.srv.URL,
		AllowedHostedDomains: []string{"allowed.example"}}}

	v := ks.validator()
	if _, err := v.Validate(context.Background(), token, providers); err != nil {
		t.Fatalf("Validate: %v", err)
	}
}

func TestValidator_Validate_TamperedSignature(t *testing.T) {
	ks := newTestKeySet(t)
	exp := time.Now().Add(time.Hour).Unix()
	token := signToken(t, ks,
		map[string]any{"alg": "RS256", "kid": ks.kid},
		map[string]any{"iss": "https://issuer.example", "aud": "client-1", "sub": "u", "exp": exp},
	)
	tampered := token[:len(token)-4] + "abcd"
	providers := []Provider{{Issuer: "https://issuer.example", Audience: "client-1", JwksURI: ks.srv.URL}}

	v := ks.validator()
	if _, err := v.Validate(context.Background(), tampered, providers); !errors.Is(err, ErrSignatureInvalid) && !errors.Is(err, ErrMalformedToken) {
		t.Fatalf("Validate(tampered) error = %v, want signature/malformed error", err)
	}
}

func TestValidator_Validate_MalformedToken(t *testing.T) {
	v := NewValidator() // no network call is made; no TLS trust needed
	_, err := v.Validate(context.Background(), "not-a-jwt", nil)
	if !errors.Is(err, ErrMalformedToken) {
		t.Fatalf("error = %v, want ErrMalformedToken", err)
	}
}
