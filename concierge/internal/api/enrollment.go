package api

import "net/http"

const enrollmentTTLSeconds = 600

// CreateEnrollment implements POST /admin/enrollment/{serverId}.
func (a *serverAPI) CreateEnrollment(w http.ResponseWriter, r *http.Request, serverId ServerId) {
	if !a.deps.Config.AdminAuthorized(r) {
		unauthorized(w)
		return
	}
	if _, ok := a.deps.Config.FindServer(serverId); !ok {
		w.WriteHeader(http.StatusNotFound)
		return
	}
	token := a.deps.Enrollments.Issue(serverId)
	origin := a.deps.Config.RequestOrigin(r)
	writeJSON(w, http.StatusOK, EnrollmentResponse{
		Url:              origin + "/enroll/" + token,
		ExpiresInSeconds: enrollmentTTLSeconds,
	})
}
