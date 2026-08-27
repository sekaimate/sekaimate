FROM node:24-bookworm-slim AS build
WORKDIR /src
RUN apt-get update && apt-get install --yes --no-install-recommends ca-certificates && rm -rf /var/lib/apt/lists/*
RUN npm install --global pnpm@11.7.0
COPY package.json pnpm-lock.yaml ./
RUN pnpm install --frozen-lockfile
COPY index.html join.html tsconfig.json vite.config.ts ./
COPY src ./src
RUN pnpm run build

FROM nginx:1.27-alpine
RUN apk add --no-cache openssl
COPY nginx.conf /etc/nginx/nginx.conf
COPY nginx-entrypoint.sh /usr/local/bin/concierge-gateway
RUN chmod 755 /usr/local/bin/concierge-gateway
COPY --from=build /src/dist /usr/share/nginx/html/admin
COPY --from=build /src/dist/join.html /usr/share/nginx/html/join.html
EXPOSE 443
ENTRYPOINT ["/usr/local/bin/concierge-gateway"]
