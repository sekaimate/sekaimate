# syntax=docker/dockerfile:1
#
# Multi-stage build for concierge (cmd/server) and its Admin UI. The UI is
# built in this image so a production deployment cannot accidentally ship a
# Concierge binary with an empty /admin/ route. The runtime remains a
# distroless nonroot image and serves the generated assets via ADMIN_UI_DIR.

FROM node:24-bookworm-slim AS admin-ui
WORKDIR /src/adminui
# Vite+ uses a native HTTP client during its test/build steps. The slim Node
# image does not include the system CA bundle, so install it explicitly for a
# reproducible image build (the runtime image remains distroless).
RUN apt-get update \
    && apt-get install --no-install-recommends --yes ca-certificates \
    && rm -rf /var/lib/apt/lists/*
RUN npm install --global pnpm@11.7.0
COPY adminui/package.json adminui/pnpm-lock.yaml ./
RUN pnpm install --frozen-lockfile
COPY adminui/ ./
RUN pnpm run typecheck && pnpm run test && pnpm run build

FROM golang:1.26.5 AS builder
WORKDIR /src
COPY go.mod go.sum ./
RUN go mod download
COPY . .
RUN CGO_ENABLED=0 go build -o /out/concierge ./cmd/server

FROM gcr.io/distroless/static:nonroot
COPY --from=builder /out/concierge /concierge
COPY --from=admin-ui /src/adminui/dist /adminui
ENV ADMIN_UI_DIR=/adminui
EXPOSE 5080
ENTRYPOINT ["/concierge"]
