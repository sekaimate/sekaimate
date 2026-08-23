// Package adminui serves the Cloudscape AdminUi single-page app and
// internally dispatches its "/api/*" calls to the concierge admin API,
// replacing the role the C# deployment's bundled Nginx gateway plays
// (design.md §9): static file serving with SPA client-side-routing
// fallback under "/admin/", and a prefix-stripped passthrough from
// "/api/*" to the same handler that already serves "/admin/..." directly.
//
// The AdminUi source (concierge/adminui) is built into the Concierge image.
// ADMIN_UI_DIR remains configurable for local development and custom builds;
// if unset, "/admin/" answers 404 with a clear message.
package adminui

import (
	"net/http"
	"os"
	"path/filepath"
)

// Mount registers the AdminUi static handler under "/admin/" (serving
// files from dir, or a 404 explainer if dir is empty) and the "/api/"
// prefix-stripped passthrough to api on mux.
func Mount(mux *http.ServeMux, api http.Handler, dir string) {
	mux.Handle("/api/", http.StripPrefix("/api", api))

	if dir == "" {
		mux.HandleFunc("/admin/", func(w http.ResponseWriter, r *http.Request) {
			http.Error(w, "AdminUi static assets are not configured; set ADMIN_UI_DIR to a built AdminUi dist/ directory.", http.StatusNotFound)
		})
		return
	}
	mux.Handle("/admin/", http.StripPrefix("/admin/", spaFileServer(dir)))
}

// spaFileServer serves static files from dir, falling back to
// dir/index.html for any path that does not exist on disk (or is a
// directory) — the "try_files ... /index.html" pattern client-side routers
// need, matching design.md §9's "未知のパスは index.html にフォールバック
// する" requirement.
func spaFileServer(dir string) http.Handler {
	fileServer := http.FileServer(http.Dir(dir))
	indexPath := filepath.Join(dir, "index.html")
	return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		requested := filepath.Join(dir, filepath.Clean("/"+r.URL.Path))
		if info, err := os.Stat(requested); err == nil && !info.IsDir() {
			fileServer.ServeHTTP(w, r)
			return
		}
		http.ServeFile(w, r, indexPath)
	})
}
