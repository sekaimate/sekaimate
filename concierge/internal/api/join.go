package api

import (
	"net/http"
	"strconv"
	"strings"

	"github.com/sekaimate/sekaimate/concierge/internal/config"
	"github.com/sekaimate/sekaimate/concierge/internal/controlplane"
)

// JoinConfig implements GET /join/{token}/config: the meeting-pinned client
// configuration (server transport fixed to that meeting's own key), with
// static-server browser endpoint fallback for legacy meeting records.
func (a *serverAPI) JoinConfig(w http.ResponseWriter, r *http.Request, token Token) {
	meeting, ok := a.deps.Meetings.FindInvite(token)
	if !ok {
		w.WriteHeader(http.StatusNotFound)
		return
	}
	if !applyWebCors(w, r, a.deps.Config) {
		w.WriteHeader(http.StatusForbidden)
		return
	}
	w.Header().Set("Cache-Control", "no-store")
	server, serverKnown := a.deps.Config.FindServer(meeting.Id)
	webSocketURI, serverInfoURI := meetingBrowserEndpoints(meeting, server, serverKnown)
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
	writeJSON(w, http.StatusOK, clientConfiguration(origin, meeting.Id, meeting.TransportPublicKey, webSocketURI, serverInfoURI, providers, organization.DefaultProviderId))
}

// meetingBrowserEndpoints returns the meeting-pinned values first, falling
// back to the matching static registry entry for control-plane records that
// predate WebGL endpoint persistence. This keeps join URLs/manifests and
// client configuration consistent for both static and managed servers.
func meetingBrowserEndpoints(meeting controlplane.MeetingRecord, server config.ServerConfig, serverKnown bool) (webSocketURI, serverInfoURI string) {
	webSocketURI, serverInfoURI = meeting.WebSocketUri, meeting.ServerInfoUri
	if !serverKnown {
		return webSocketURI, serverInfoURI
	}
	if webSocketURI == "" {
		webSocketURI = server.WebSocketUri
	}
	if serverInfoURI == "" {
		serverInfoURI = server.ServerInfoUri
	}
	return webSocketURI, serverInfoURI
}

// bracketHost wraps a literal IPv6 host in [...] for use in a
// "host:port"-shaped URL, matching Program.cs's ad hoc bracketing.
func bracketHost(host string) string {
	if strings.Contains(host, ":") && !strings.HasPrefix(host, "[") {
		return "[" + host + "]"
	}
	return host
}

func deepLink(meetingID, host string, port uint16, password, webSocketURI string) string {
	link := "basisdemo://" + bracketHost(host) + ":" + strconv.Itoa(int(port)) +
		"?password=" + escapeDataString(password) + "&meeting=" + escapeDataString(meetingID)
	if webSocketURI != "" {
		link += "&websocketUri=" + escapeDataString(webSocketURI)
	}
	return link
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
	server, serverKnown := a.deps.Config.FindServer(meeting.Id)
	webSocketURI, _ := meetingBrowserEndpoints(meeting, server, serverKnown)
	link := deepLink(meeting.Id, meeting.Host, meeting.Port, meeting.Password, webSocketURI)
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
	server, serverKnown := a.deps.Config.FindServer(meeting.Id)
	webSocketURI, _ := meetingBrowserEndpoints(meeting, server, serverKnown)
	link := deepLink(meeting.Id, meeting.Host, meeting.Port, meeting.Password, webSocketURI)
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
	w.Header().Set("Cache-Control", "no-store, private")
	meeting, ok := a.deps.Meetings.FindInvite(token)
	if !ok {
		w.WriteHeader(http.StatusNotFound)
		return
	}
	origin := a.deps.Config.RequestOrigin(r)
	server, serverKnown := a.deps.Config.FindServer(meeting.Id)
	webSocketURI, serverInfoURI := meetingBrowserEndpoints(meeting, server, serverKnown)
	transport := transportConfig(origin, meeting.Id, meeting.TransportPublicKey, webSocketURI, serverInfoURI)
	port := int(meeting.Port)
	writeJSON(w, http.StatusOK, JoinManifest{
		Meeting: &JoinManifestMeeting{
			Id:    &meeting.Id,
			Title: &meeting.Title,
		},
		Connection: &JoinManifestConnection{
			Host:          &meeting.Host,
			Port:          &port,
			Password:      &meeting.Password,
			WebSocketUri:  strPtrIfNonEmpty(webSocketURI),
			ServerInfoUri: strPtrIfNonEmpty(serverInfoURI),
		},
		ServerTransport: &transport,
	})
}
