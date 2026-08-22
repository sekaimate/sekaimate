package api

import (
	"encoding/json"
	"net/http"
)

// writeJSON writes v as compact JSON with the given status, matching
// Results.Ok/Results.Json's default (non-indented) output.
func writeJSON(w http.ResponseWriter, status int, v any) {
	w.Header().Set("Content-Type", "application/json")
	w.WriteHeader(status)
	_ = json.NewEncoder(w).Encode(v)
}

// writeJSONIndent writes v as pretty-printed JSON, matching the one
// endpoint that opts into WriteIndented (GET /admin/client-config-template).
func writeJSONIndent(w http.ResponseWriter, status int, v any) {
	data, err := json.MarshalIndent(v, "", "  ")
	if err != nil {
		writeError(w, http.StatusInternalServerError, err.Error())
		return
	}
	w.Header().Set("Content-Type", "application/json")
	w.WriteHeader(status)
	_, _ = w.Write(data)
}

// writeError writes the hand-rolled {"error":"..."} envelope Results.BadRequest
// uses in the C# broker (research-sso-broker.md §1.3/§7: distinct from the
// RFC 7807 envelope written by writeProblem).
func writeError(w http.ResponseWriter, status int, message string) {
	writeJSON(w, status, ErrorResponse{Error: message})
}

// writeProblem writes an RFC 7807 application/problem+json body, matching
// Results.Problem's envelope (used for 409/501/503 in the C# broker).
func writeProblem(w http.ResponseWriter, status int, detail string) {
	title := http.StatusText(status)
	w.Header().Set("Content-Type", "application/problem+json")
	w.WriteHeader(status)
	_ = json.NewEncoder(w).Encode(ProblemDetails{
		Title:  &title,
		Status: &status,
		Detail: &detail,
	})
}

// writeText writes a plain-text body with the given status, matching
// Results.Content(..., "text/plain; charset=utf-8", statusCode: ...).
func writeText(w http.ResponseWriter, status int, body string) {
	w.Header().Set("Content-Type", "text/plain; charset=utf-8")
	w.WriteHeader(status)
	_, _ = w.Write([]byte(body))
}

// writeHTML writes an HTML body, matching
// Results.Content(..., "text/html; charset=utf-8").
func writeHTML(w http.ResponseWriter, status int, body string) {
	w.Header().Set("Content-Type", "text/html; charset=utf-8")
	w.WriteHeader(status)
	_, _ = w.Write([]byte(body))
}

// unauthorized writes an empty 401 body, matching Results.Unauthorized().
func unauthorized(w http.ResponseWriter) {
	w.WriteHeader(http.StatusUnauthorized)
}

func strPtr(s string) *string { return &s }

func derefStr(p *string) string {
	if p == nil {
		return ""
	}
	return *p
}

func derefStrSlice(p *[]string) []string {
	if p == nil {
		return nil
	}
	return *p
}

func strSlicePtr(s []string) *[]string {
	if len(s) == 0 {
		return nil
	}
	return &s
}
