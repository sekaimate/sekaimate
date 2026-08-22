package controlplane

import (
	"encoding/base64"
	"testing"
)

func TestGenerateMeetingKeys(t *testing.T) {
	priv, pub, signing, err := GenerateMeetingKeys()
	if err != nil {
		t.Fatalf("GenerateMeetingKeys: %v", err)
	}

	privBytes, err := base64.RawURLEncoding.DecodeString(priv)
	if err != nil {
		t.Fatalf("private key is not base64url-nopad: %v", err)
	}
	if len(privBytes) != 32 {
		t.Errorf("private key length = %d, want 32", len(privBytes))
	}

	pubBytes, err := base64.RawURLEncoding.DecodeString(pub)
	if err != nil {
		t.Fatalf("public key is not base64url-nopad: %v", err)
	}
	if len(pubBytes) != 32 {
		t.Errorf("public key length = %d, want 32", len(pubBytes))
	}

	signingBytes, err := base64.RawURLEncoding.DecodeString(signing)
	if err != nil {
		t.Fatalf("signing key is not base64url-nopad: %v", err)
	}
	if len(signingBytes) != signingKeyBytes {
		t.Errorf("signing key length = %d, want %d", len(signingBytes), signingKeyBytes)
	}
	if len(signing) < 32 {
		t.Errorf("signing key string length = %d, want >=32 (HasTicketSigningKey threshold)", len(signing))
	}

	// Two calls must not produce the same keys.
	priv2, pub2, signing2, err := GenerateMeetingKeys()
	if err != nil {
		t.Fatalf("GenerateMeetingKeys (2nd): %v", err)
	}
	if priv == priv2 || pub == pub2 || signing == signing2 {
		t.Errorf("GenerateMeetingKeys produced identical output across calls")
	}
}
