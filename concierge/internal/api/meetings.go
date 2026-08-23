package api

import (
	"log"
	"net/http"
	"os"
	"strings"
	"time"

	"github.com/sekaimate/sekaimate/concierge/internal/config"
	"github.com/sekaimate/sekaimate/concierge/internal/controlplane"
	"github.com/sekaimate/sekaimate/concierge/internal/kube"
)

const maxMeetingTitleLength = 120
const defaultMeetingPort = 4296

// ListMeetings implements GET /admin/meetings.
func (a *serverAPI) ListMeetings(w http.ResponseWriter, r *http.Request) {
	if !a.deps.Config.AdminAuthorized(r) {
		unauthorized(w)
		return
	}
	origin := a.deps.Config.RequestOrigin(r)
	meetings := a.deps.Meetings.List()
	out := make([]MeetingView, len(meetings))
	for i, m := range meetings {
		out[i] = meetingToView(m, origin, webJoinURL(a.deps.Config, origin, m.InviteToken))
	}
	writeJSON(w, http.StatusOK, out)
}

// manualMeetingsAllowed mirrors the case-insensitive
// BASIS_CONTROL_PLANE_ALLOW_MANUAL_MEETINGS=="true" gate on
// POST /admin/meetings.
func manualMeetingsAllowed() bool {
	return strings.EqualFold(os.Getenv("BASIS_CONTROL_PLANE_ALLOW_MANUAL_MEETINGS"), "true")
}

// CreateMeeting implements POST /admin/meetings, matching Program.cs's
// handler: validate -> allocate id -> generate per-meeting SSO keys ->
// register a matching static server entry -> persist the meeting record ->
// provision compute via the RoomProvisioner, rolling back on any failure
// along the way. Provisioning only happens when the request supplies no
// explicit host: an explicit host names an externally-run server (matching
// the C# broker, which never provisioned anything), so that case skips
// RoomProvisioner.Create entirely and the meeting is immediately "ready"
// (design.md §4.2).
func (a *serverAPI) CreateMeeting(w http.ResponseWriter, r *http.Request) {
	if !a.deps.Config.AdminAuthorized(r) {
		unauthorized(w)
		return
	}
	if !manualMeetingsAllowed() {
		writeProblem(w, http.StatusNotImplemented, "This deployment is configured for one Compose meeting. Kubernetes meeting provisioning is not enabled.")
		return
	}

	organization := a.deps.Config.GetOrganization()
	if ok, msg := organization.IsStructurallyValid(); !ok {
		writeProblem(w, http.StatusConflict, "Organization SSO settings are incomplete: "+msg)
		return
	}

	var req CreateMeetingRequest
	if err := decodeJSON(w, r, &req); err != nil {
		writeError(w, http.StatusBadRequest, "invalid request body")
		return
	}
	title := strings.TrimSpace(req.Title)
	if title == "" || len(title) > maxMeetingTitleLength {
		writeError(w, http.StatusBadRequest, "Meeting title is required and must be 120 characters or fewer.")
		return
	}
	requestedID := ""
	if req.Id != nil {
		requestedID = strings.TrimSpace(*req.Id)
		if requestedID != "" && !controlplane.IsValidID(requestedID) {
			writeError(w, http.StatusBadRequest, "Meeting ID may use up to 48 letters, numbers, '-' and '_'.")
			return
		}
	}
	host := ""
	if req.Host != nil {
		host = strings.TrimSpace(*req.Host)
		if host != "" && !controlplane.IsSafeHost(host) {
			writeError(w, http.StatusBadRequest, "Connection host is invalid.")
			return
		}
	}

	id := controlplane.NewID(requestedID, title, func(candidate string) bool {
		if a.deps.Meetings.Exists(candidate) {
			return true
		}
		_, exists := a.deps.Config.FindServer(candidate)
		return exists
	})

	privateKey, publicKey, signingKey, err := controlplane.GenerateMeetingKeys()
	if err != nil {
		writeError(w, http.StatusInternalServerError, "failed to generate meeting keys")
		return
	}

	port := uint16(defaultMeetingPort)
	if req.Port != nil && *req.Port > 0 && *req.Port <= 0xffff {
		port = uint16(*req.Port)
	}
	password := ""
	if req.Password != nil {
		password = strings.TrimSpace(*req.Password)
	}
	if password == "" {
		password = controlplane.RandomPassword()
	}
	webSocketURI := ""
	if req.WebSocketUri != nil {
		webSocketURI = strings.TrimSpace(*req.WebSocketUri)
	}
	serverInfoURI := ""
	if req.ServerInfoUri != nil {
		serverInfoURI = strings.TrimSpace(*req.ServerInfoUri)
	}
	if err := config.ValidateBrowserEndpoints(webSocketURI, serverInfoURI); err != nil {
		writeError(w, http.StatusBadRequest, "Browser endpoints are invalid: "+err.Error())
		return
	}

	managed := host == ""
	status, detail := "provisioning", "Waiting for Kubernetes provisioning."
	if !managed {
		status, detail = "ready", "External connection target configured."
	}

	// CreatedAt/UpdatedAt are set here, at construction, matching the C#
	// broker's MeetingRecord constructor (ControlPlane.cs), so the 201
	// response rendered from this local value below carries real timestamps
	// instead of the zero time. Store.Add only backfills them when zero, so
	// setting them here also makes it the value actually persisted.
	now := time.Now().UTC()
	meeting := controlplane.MeetingRecord{
		Id:                  id,
		Title:               title,
		Host:                host,
		Port:                port,
		Password:            password,
		InviteToken:         controlplane.RandomToken(24),
		TicketSigningKey:    signingKey,
		TransportPrivateKey: privateKey,
		TransportPublicKey:  publicKey,
		WebSocketUri:        webSocketURI,
		ServerInfoUri:       serverInfoURI,
		Status:              status,
		StatusDetail:        detail,
		Managed:             managed,
		CreatedAt:           now,
		UpdatedAt:           now,
	}

	admissionProviders := make([]config.ProviderConfig, len(organization.Providers))
	for i, p := range organization.Providers {
		admissionProviders[i] = p.Copy()
	}
	serverConfig := config.ServerConfig{
		Id:                 id,
		Providers:          admissionProviders,
		TicketSigningKey:   signingKey,
		TransportPublicKey: publicKey,
		WebSocketUri:       webSocketURI,
		ServerInfoUri:      serverInfoURI,
		FromMeeting:        true,
	}

	if err := a.deps.Config.AddServer(serverConfig); err != nil {
		writeError(w, http.StatusConflict, "A server with that ID already exists.")
		return
	}
	if err := a.deps.Meetings.Add(meeting); err != nil {
		_ = a.deps.Config.RemoveServer(id)
		writeError(w, http.StatusConflict, "A meeting with that ID already exists.")
		return
	}
	if managed {
		if err := a.deps.Provisioner.Create(r.Context(), id, kube.RoomKeys{
			TicketSigningKey:    signingKey,
			TransportPrivateKey: privateKey,
			TransportPublicKey:  publicKey,
			WebSocketUri:        webSocketURI,
			ServerInfoUri:       serverInfoURI,
		}); err != nil {
			log.Printf("api: provision meeting %s: %v", id, err)
			_, _ = a.deps.Meetings.Delete(id)
			_ = a.deps.Config.RemoveServer(id)
			writeError(w, http.StatusInternalServerError, "failed to provision meeting")
			return
		}
	}

	origin := a.deps.Config.RequestOrigin(r)
	w.Header().Set("Location", "/admin/meetings/"+id)
	writeJSON(w, http.StatusCreated, meetingToView(meeting, origin, webJoinURL(a.deps.Config, origin, meeting.InviteToken)))
}

// DeleteMeeting implements DELETE /admin/meetings/{meetingId}. As with
// DeleteAdminServer, the client-config file path is resolved before the
// matching server registry entry is removed (see the comment there for
// why this differs from the literal C# handler's ordering).
func (a *serverAPI) DeleteMeeting(w http.ResponseWriter, r *http.Request, meetingId MeetingId) {
	if !a.deps.Config.AdminAuthorized(r) {
		unauthorized(w)
		return
	}
	deleted, ok := a.deps.Meetings.Delete(meetingId)
	if !ok {
		w.WriteHeader(http.StatusNotFound)
		return
	}
	configPath, hasConfigPath := a.deps.Config.ClientConfigPath(meetingId)
	_ = a.deps.Config.RemoveServer(meetingId) // best-effort: static Servers[] entries have no matching meeting
	if hasConfigPath {
		_ = removeIfExists(configPath)
	}
	if deleted.Managed {
		_ = a.deps.Provisioner.Delete(r.Context(), meetingId)
	}
	w.WriteHeader(http.StatusNoContent)
}

// CreateInvitation implements POST /admin/meetings/{meetingId}/invitations.
// The invite token is stable per meeting and is never regenerated here.
func (a *serverAPI) CreateInvitation(w http.ResponseWriter, r *http.Request, meetingId MeetingId) {
	if !a.deps.Config.AdminAuthorized(r) {
		unauthorized(w)
		return
	}
	meeting, ok := a.deps.Meetings.Find(meetingId)
	if !ok {
		w.WriteHeader(http.StatusNotFound)
		return
	}
	origin := a.deps.Config.RequestOrigin(r)
	writeJSON(w, http.StatusOK, InvitationResponse{
		Url:       origin + "/join/" + meeting.InviteToken,
		MeetingId: meeting.Id,
	})
}
