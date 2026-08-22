package api

import (
	"net/http"
	"strconv"
	"strings"
)

// JoinConfig implements GET /join/{token}/config: the meeting-pinned client
// configuration (server transport fixed to that meeting's own key).
func (a *serverAPI) JoinConfig(w http.ResponseWriter, r *http.Request, token Token) {
	meeting, ok := a.deps.Meetings.FindInvite(token)
	if !ok {
		w.WriteHeader(http.StatusNotFound)
		return
	}
	server, serverKnown := a.deps.Config.FindServer(meeting.Id)
	organization := a.deps.Config.GetOrganization()
	providers := organization.Providers
	if len(providers) == 0 && serverKnown {
		providers = server.Providers
	}
	if !serverKnown || len(providers) == 0 || meeting.TransportPublicKey == "" {
		writeProblem(w, http.StatusServiceUnavailable, "Meeting organization configuration is incomplete.")
		return
	}
	origin := a.deps.Config.RequestOrigin(r)
	writeJSON(w, http.StatusOK, clientConfiguration(origin, meeting.Id, meeting.TransportPublicKey, providers, organization.DefaultProviderId))
}

// bracketHost wraps a literal IPv6 host in [...] for use in a
// "host:port"-shaped URL, matching Program.cs's ad hoc bracketing.
func bracketHost(host string) string {
	if strings.Contains(host, ":") && !strings.HasPrefix(host, "[") {
		return "[" + host + "]"
	}
	return host
}

func deepLink(meetingID, host string, port uint16, password string) string {
	return "basisdemo://" + bracketHost(host) + ":" + strconv.Itoa(int(port)) +
		"?password=" + escapeDataString(password) + "&meeting=" + escapeDataString(meetingID)
}

// JoinPage implements GET /join/{token}: an HTML page that tries the
// loopback bridge first and falls back to the basisdemo:// deep link.
func (a *serverAPI) JoinPage(w http.ResponseWriter, r *http.Request, token Token) {
	meeting, ok := a.deps.Meetings.FindInvite(token)
	if !ok {
		writeText(w, http.StatusNotFound, "This meeting invitation is invalid or has been revoked.")
		return
	}
	if !strings.EqualFold(meeting.Status, "ready") || strings.TrimSpace(meeting.Host) == "" {
		writeText(w, http.StatusConflict, "This meeting is not ready yet.")
		return
	}
	link := deepLink(meeting.Id, meeting.Host, meeting.Port, meeting.Password)
	origin := a.deps.Config.RequestOrigin(r)
	configurationURL := origin + "/join/" + escapeDataString(token) + "/config"
	loopbackURL := "http://127.0.0.1:56831/basis-join?config=" + escapeDataString(configurationURL) + "&link=" + escapeDataString(link)
	page, err := renderJoinPage(meeting.Title, link, loopbackURL)
	if err != nil {
		writeError(w, http.StatusInternalServerError, err.Error())
		return
	}
	writeHTML(w, http.StatusOK, page)
}

// JoinOpenPage implements GET /join/{token}/open: the same deep-link page
// without the loopback iframe (browser fallback).
func (a *serverAPI) JoinOpenPage(w http.ResponseWriter, r *http.Request, token Token) {
	meeting, ok := a.deps.Meetings.FindInvite(token)
	if !ok {
		writeText(w, http.StatusNotFound, "This meeting invitation is invalid or has been revoked.")
		return
	}
	if !strings.EqualFold(meeting.Status, "ready") || strings.TrimSpace(meeting.Host) == "" {
		writeText(w, http.StatusConflict, "This meeting is not ready yet.")
		return
	}
	link := deepLink(meeting.Id, meeting.Host, meeting.Port, meeting.Password)
	page, err := renderJoinOpenPage(meeting.Title, link)
	if err != nil {
		writeError(w, http.StatusInternalServerError, err.Error())
		return
	}
	writeHTML(w, http.StatusOK, page)
}

// JoinManifest implements GET /join/{token}/manifest: the only endpoint
// that returns the plaintext join password.
func (a *serverAPI) JoinManifest(w http.ResponseWriter, r *http.Request, token Token) {
	meeting, ok := a.deps.Meetings.FindInvite(token)
	if !ok {
		w.WriteHeader(http.StatusNotFound)
		return
	}
	origin := a.deps.Config.RequestOrigin(r)
	transport := transportConfig(origin, meeting.Id, meeting.TransportPublicKey)
	port := int(meeting.Port)
	writeJSON(w, http.StatusOK, JoinManifest{
		Meeting: &JoinManifestMeeting{
			Id:    &meeting.Id,
			Title: &meeting.Title,
		},
		Connection: &JoinManifestConnection{
			Host:     &meeting.Host,
			Port:     &port,
			Password: &meeting.Password,
		},
		ServerTransport: &transport,
	})
}
