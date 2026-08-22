package api

import (
	"net/http"
)

// EnrollLanding implements GET /enroll/{token}.
func (a *serverAPI) EnrollLanding(w http.ResponseWriter, r *http.Request, token Token) {
	if !a.deps.Enrollments.Exists(token) {
		writeText(w, http.StatusGone, "This Basis SSO setup link has expired.")
		return
	}
	origin := a.deps.Config.RequestOrigin(r)
	configURL := origin + "/enroll/" + escapeDataString(token) + "/config"
	callback := "http://127.0.0.1:56831/basis-sso-config?url=" + escapeDataString(configURL)
	page, err := renderEnrollLanding(callback)
	if err != nil {
		writeError(w, http.StatusInternalServerError, err.Error())
		return
	}
	writeHTML(w, http.StatusOK, page)
}

// EnrollConfig implements GET /enroll/{token}/config: single-use, consumes
// the token via Enrollments.Take regardless of the eventual response
// (matching enrollments.Take being called unconditionally in Program.cs).
func (a *serverAPI) EnrollConfig(w http.ResponseWriter, r *http.Request, token Token) {
	serverID, ok := a.deps.Enrollments.Take(token)
	if !ok {
		writeText(w, http.StatusGone, "This Basis SSO setup link has expired or was already used.")
		return
	}
	server, serverKnown := a.deps.Config.FindServer(serverID)
	meeting, meetingKnown := a.deps.Meetings.Find(serverID)
	organization := a.deps.Config.GetOrganization()
	providers := organization.Providers
	if len(providers) == 0 && serverKnown {
		providers = server.Providers
	}
	publicKey := ""
	webSocketURI, serverInfoURI := "", ""
	if meetingKnown && meeting.TransportPublicKey != "" {
		publicKey = meeting.TransportPublicKey
		webSocketURI, serverInfoURI = meeting.WebSocketUri, meeting.ServerInfoUri
		if serverKnown {
			if webSocketURI == "" {
				webSocketURI = server.WebSocketUri
			}
			if serverInfoURI == "" {
				serverInfoURI = server.ServerInfoUri
			}
		}
	} else if serverKnown {
		publicKey = server.EffectiveTransportPublicKey()
		webSocketURI, serverInfoURI = server.WebSocketUri, server.ServerInfoUri
	}
	if !serverKnown || len(providers) == 0 || publicKey == "" {
		w.WriteHeader(http.StatusNotFound)
		return
	}
	origin := a.deps.Config.RequestOrigin(r)
	writeJSON(w, http.StatusOK, clientConfiguration(origin, serverID, publicKey, webSocketURI, serverInfoURI, providers, organization.DefaultProviderId))
}
