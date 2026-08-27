// Package api implements the concierge public/admin HTTP surface. Routing
// and request models are generated from api/openapi.yaml into
// server.gen.go (oapi-codegen std-http-server + models, config in cfg.yaml);
// this package's other files implement the handlers against the generated
// ServerInterface.
package api

//go:generate go tool oapi-codegen -config cfg.yaml ../../api/openapi.yaml
