# syntax=docker/dockerfile:1
#
# Multi-stage build for concierge (cmd/server), mirroring basis-k8s's
# Dockerfile: a golang builder stage with CGO disabled, and a distroless
# nonroot runtime image with nothing but the built binary. This image is
# concierge itself (the admission/control-plane/Kubernetes-provisioning
# HTTP service) — it is unrelated to the separate basis-server image that
# Agones GameServers run (see docs/concierge/design.md §5 and
# docs/concierge/implementation.md's phase-2 section for BASIS_SERVER_IMAGE).

FROM golang:1.26.5 AS builder
WORKDIR /src
COPY go.mod go.sum ./
RUN go mod download
COPY . .
RUN CGO_ENABLED=0 go build -o /out/concierge ./cmd/server

FROM gcr.io/distroless/static:nonroot
COPY --from=builder /out/concierge /concierge
EXPOSE 5080
ENTRYPOINT ["/concierge"]
