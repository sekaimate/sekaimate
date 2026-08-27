# Basis SSO admission broker

Run this service separately from the Basis UDP server. It is the only component that receives
OIDC ID tokens. It listens on loopback HTTP by default; put a TLS reverse proxy in front of it for
production, because clients require an HTTPS admission endpoint.

## Quick start (macOS / Linux)

Install the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0), then run:

```sh
cd Tools/BasisSsoBroker
chmod +x prepare-broker.sh run-broker.sh
./prepare-broker.sh /absolute/path/to/Basis/config/config.xml
# Edit appsettings.json: replace the Google / Okta placeholders.
./run-broker.sh
```

The preparation command creates two local files:

- `appsettings.json` — provider configuration; it has no secret.
- `broker.env` — the server/broker signing key, created with mode `0600`. Do not commit or copy it
  to a client machine.

Verify the process from the same machine:

```sh
curl http://127.0.0.1:5080/health
```

Expected response: `{"status":"ready",...}`. `run-broker.sh` refuses to start while sample
provider placeholders remain.

## Start automatically with the Basis server

For a standalone Basis server, publish the broker directly beside the server executable. The
server starts it as a child process whenever `RequireSso=true` and `AutoStartSsoBroker=true`
(both are defaults for a newly enabled SSO server), passes the signing key only through the child
environment, and stops it during server shutdown.

```sh
cd Tools/BasisSsoBroker
chmod +x publish-for-basis-server.sh
./publish-for-basis-server.sh /absolute/path/to/BasisServer
# Edit /absolute/path/to/BasisServer/sso-broker/appsettings.json.
# Start BasisServer normally.
```

The default loopback bind URL is `http://127.0.0.1:5080`; configure a public TLS proxy as below
and set its `https://.../admission/SERVER_ID` URL in the client config. To use a separately managed broker,
set `AutoStartSsoBroker=false` in the server configuration.

## Start the Basis server

### Docker Compose (recommended)

The repository's server deployment is under `Packages/com.basis.server/Docker`:

```sh
cd Packages/com.basis.server/Docker
docker compose up -d --build
docker compose logs -f basis-server
```

Stop it with `docker compose down`. The first start creates `config/config.xml`; set a non-default
`Password` and an admin identity before exposing the UDP port. Full Docker setup is in
`Packages/com.basis.server/Docker/README.md`.

The automatic child-broker option is for a **standalone server deployment** where the published
broker lives beside `BasisNetworkConsole.dll`. In Docker, run the broker as a separate Compose
stack behind Nginx; do not put its signing key in a client or source-controlled compose file.
The repository deployment is split into the game server stack and `Docker/sso`:

```sh
cd Packages/com.basis.server/Docker
docker compose up -d --build

cd sso
docker compose up -d --build
```

The broker reads the generated key directly from the mounted server config at startup. Only Nginx
publishes HTTPS on host loopback; port 5080 is internal to the Docker network.

### Standalone server build

From a published server directory, start the server with:

```sh
dotnet BasisNetworkConsole.dll
```

After `publish-for-basis-server.sh` has placed the broker in `./sso-broker`, setting
`RequireSso=true` makes this command start both processes. The server log prints the broker PID
and loopback URL; the child exits with the server.

## Production HTTPS

Place a TLS endpoint in front of the broker, configure it with a certificate trusted for your public
hostname, and set `https://sso.example.com/admission/SERVER_ID` as
`serverTransport.admissionEndpoint` in each client `basis-sso.json`.
For WebGL clients, add every exact HTTPS client origin to `Broker.AllowedWebOrigins`; the broker
uses that list for the admission POST CORS policy and does not allow wildcard origins.

For a system service, publish the app and use `basis-sso-broker.service.example` as the template:

```sh
dotnet publish -c Release -o /opt/basis-sso-broker
sudo cp basis-sso-broker.service.example /etc/systemd/system/basis-sso-broker.service
sudo systemctl daemon-reload
sudo systemctl enable --now basis-sso-broker
```

The service intentionally listens only on `127.0.0.1:5080`; Nginx is responsible for TLS and
public exposure.

1. `prepare-broker.sh` copies `appsettings.example.json` and reads
   `SsoAdmissionTicketSigningKey` from the SSO-enabled Basis server's `config.xml` into the local
   mode-600 `broker.env` file.
3. Host the broker behind HTTPS and set its `/admission/SERVER_ID` URL in the client `basis-sso.json`.
4. Copy `SsoTransportPublicKey` from `config.xml` into `serverTransport.serverPublicKey` in that
   same client config. This public value pins the server used to encrypt each UDP admission envelope.

The broker never logs or returns an OAuth token. It returns a one-minute ticket bound to the DID
provided by the client. The game server verifies its HMAC, consumes the ticket's unique ID once,
and then requires the existing DID challenge. The client encrypts this opaque ticket to the pinned
server X25519 key; an observed game-transport packet cannot reveal or replay an admission.

## One broker for multiple servers

Use one public HTTPS broker process, but configure a separate broker server entry for every Basis
server. Each entry has its own ticket-signing-key environment variable and provider policy. Start
from `appsettings.example.json` and expose each server's key only as an environment variable on
the broker host:

```json
{
  "Broker": {
    "Servers": [{
      "Id": "world-a",
      "TicketSigningKeyEnvironmentVariable": "BASIS_SSO_TICKET_SIGNING_KEY_WORLD_A",
      "Providers": ["...server-specific Google or Okta policy..."]
    }]
  }
}
```

Set each client configuration to that server's route, for example
`https://auth.example.com/admission/world-a`. The broker signs the ticket using only
`BASIS_SSO_TICKET_SIGNING_KEY_WORLD_A`, which must equal `world-a`'s
`SsoAdmissionTicketSigningKey`; do not reuse signing keys between servers. With multiple entries,
the unqualified `/admission` route does not exist, so a client cannot accidentally obtain a ticket
for the wrong server. `/health` lists configured server IDs and readiness without exposing keys or
tokens.

## Browser-managed client setup (no build-time SSO config)

The broker hosts the organization configuration and delivers it through each meeting invitation.
This keeps the IdP settings, admission endpoint, and transport public key out of the client build.

Add a long random value to the broker process environment (never to `appsettings.json`):

```sh
export BASIS_SSO_ADMIN_TOKEN="at-least-32-random-characters"
```

Then open `https://auth.example.com/admin`, enter that token, choose the server ID, and paste the
client JSON. The editor rejects every `clientSecret` field because this JSON is distributed to
clients. The broker stores the editable source at
`ClientConfigDirectory/<server-id>.json`; mount that directory as persistent storage.

When a participant opens a meeting invitation, the client downloads the meeting's short-lived
configuration and activates it **in memory for the current process**. It does not write
`basis-sso.json` to `persistentDataPath`; the meeting invitation is the only user-facing setup
entry point.

The public `GET /client-config/SERVER_ID` endpoint exists for inspection or managed deployment.
