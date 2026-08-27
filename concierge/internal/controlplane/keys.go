package controlplane

import (
	"crypto/ecdh"
	"crypto/rand"
	"encoding/base64"
	"fmt"
)

// signingKeyBytes matches MeetingKeys.Generate's 48-byte random
// TicketSigningKey, kept consistent with BasisSsoTransportKeys.Ensure's
// server-side generator (research-sso-broker.md §7-9).
const signingKeyBytes = 48

// GenerateMeetingKeys creates a fresh per-meeting X25519 transport keypair
// and a 48-byte HMAC ticket-signing key, all base64url (no padding)
// encoded — matching MeetingKeys.Generate in ControlPlane.cs. Unlike the C#
// implementation, this uses crypto/ecdh's X25519 support directly rather
// than slicing raw key bytes out of a PKCS8/SubjectPublicKeyInfo DER
// encoding (research-sso-broker.md §3.1 notes the DER-slicing was only a
// workaround for .NET's API surface and does not need to be replicated).
func GenerateMeetingKeys() (privateKey, publicKey, signingKey string, err error) {
	curve := ecdh.X25519()
	key, err := curve.GenerateKey(rand.Reader)
	if err != nil {
		return "", "", "", fmt.Errorf("controlplane: generate X25519 key: %w", err)
	}
	signing := make([]byte, signingKeyBytes)
	if _, err := rand.Read(signing); err != nil {
		return "", "", "", fmt.Errorf("controlplane: generate signing key: %w", err)
	}
	return encode(key.Bytes()), encode(key.PublicKey().Bytes()), encode(signing), nil
}

func encode(b []byte) string {
	return base64.RawURLEncoding.EncodeToString(b)
}
