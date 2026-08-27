package security

import (
	"context"
	"testing"
)

func TestValidateHTTPSURLRejectsNonPublicLiterals(t *testing.T) {
	for _, raw := range []string{
		"http://example.com/token",
		"https://127.0.0.1/token",
		"https://10.0.0.1/token",
		"https://169.254.169.254/token",
		"https://[::1]/token",
		"https://user:pass@example.com/token",
		"https://example.com/token#fragment",
	} {
		if err := ValidateHTTPSURL(context.Background(), raw); err == nil {
			t.Errorf("ValidateHTTPSURL(%q) succeeded, want rejection", raw)
		}
	}
}

func TestValidateHTTPSURLAcceptsPublicLiteral(t *testing.T) {
	if err := ValidateHTTPSURL(context.Background(), "https://203.0.113.10/token"); err != nil {
		t.Fatalf("ValidateHTTPSURL(public literal): %v", err)
	}
}
