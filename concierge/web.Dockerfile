# WebGL static server image.
#
# Build this Dockerfile from the repository root after ./tools/build-web.sh:
#
#   ./tools/build-web.sh --dev Build/Web
#   minikube image build -t concierge-web:dev -f concierge/web.Dockerfile .
#
# Build/Web is intentionally ignored by git; the development build step is
# the source of the assets copied into this image.
FROM node:24-bookworm-slim

WORKDIR /srv/web
COPY Build/Web/ ./
COPY tools/serve-web.mjs /srv/serve-web.mjs

ENV HOST=0.0.0.0
ENV PORT=4173
EXPOSE 4173

USER node
ENTRYPOINT ["node", "/srv/serve-web.mjs", "/srv/web", "4173"]
