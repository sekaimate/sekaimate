# SSO stack

The game server stack must be running first so the shared `basis-sso` Docker network and SSO keys exist.

```sh
cp .env.example .env
cp broker/appsettings.example.json broker/appsettings.json
docker compose up -d --build
```

Keep `.env` and `broker/appsettings.json` on the server only. Publish the loopback gateway through the host Caddy configuration:

```caddyfile
server.example.com {
    reverse_proxy 127.0.0.1:5081
}
```

The public control plane is `https://server.example.com/admin/`. The broker API, Web OIDC token exchange, and admission endpoint use the same host. Only Caddy listens publicly on TCP 443; the Basis game server continues to use UDP 4296.
