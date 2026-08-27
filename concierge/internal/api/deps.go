package api

import (
	"net/http"

	"github.com/sekaimate/sekaimate/concierge/internal/admission"
	"github.com/sekaimate/sekaimate/concierge/internal/config"
	"github.com/sekaimate/sekaimate/concierge/internal/controlplane"
	"github.com/sekaimate/sekaimate/concierge/internal/kube"
)

// Deps are the collaborators the HTTP handlers need — one instance is
// shared by every request, matching how the C# broker resolves its
// singletons (IOptions<BrokerOptions>, MeetingStore, EnrollmentStore,
// TokenValidator) via DI.
type Deps struct {
	Config      *config.Store
	Meetings    *controlplane.Store
	Enrollments *controlplane.EnrollmentStore
	Validator   *admission.Validator
	Provisioner kube.RoomProvisioner
}

var _ ServerInterface = (*serverAPI)(nil)

// serverAPI implements the generated ServerInterface on top of Deps.
type serverAPI struct {
	deps Deps
}

// NewMux builds the *http.ServeMux serving every public/admin route
// described in api/openapi.yaml.
func NewMux(deps Deps) *http.ServeMux {
	impl := &serverAPI{deps: deps}
	mux := http.NewServeMux()
	HandlerFromMux(impl, mux)
	return mux
}
