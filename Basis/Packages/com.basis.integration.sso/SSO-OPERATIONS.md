# Google + Okta SSO client operations

This integration uses an HTTPS admission broker as the server authorization boundary. The Basis
UDP connection setup is cleartext, so an ID/access token is **never** sent in its initial packet.
The client sends the ID token only to the HTTPS broker. The broker returns a one-minute, opaque
ticket bound to the joining DID; that ticket and the join password are then encrypted to the
server's pinned X25519 public key using an ephemeral X25519 key and ChaCha20-Poly1305.

## Client configuration

The normal deployment does not embed organization OAuth values in
`Assets/StreamingAssets/basis-sso.json`. Configure Google / Okta in the organization control
plane, then either use **Basis に組織設定を送る** or open a meeting invitation while Basis is
running. The broker applies the current organization configuration only to the running client;
it is not written back into the build. Create public/native applications in both identity
providers and use Authorization Code with PKCE. If a provider requires a native-client
`clientSecret`, it is used only during the HTTPS token exchange with that provider; it is never
sent to the Basis server or broker.

The current UI intentionally supports exactly two providers. The shipped sample names them
`google` and `okta`; it uses a fixed loopback callback on port 42800 so that the same redirect
URI can be registered with both providers.

Local identity data is namespaced using a SHA-256 hash of `issuer + sub`. Changing provider or
signing in as a different subject therefore selects a different DID and restored display name.

## Server enforcement

Set `RequireSso` on the server after configuring the broker and distributing its public key. Old
clients and plaintext/legacy SSO envelopes are rejected. The server decrypts the admission
envelope, verifies the ticket HMAC, binds it to the DID, marks its unique ticket ID consumed, and
then requires the normal fresh DID challenge. The admission ticket is therefore neither an OAuth
bearer credential nor replayable.

The server Settings → Admin → **SSO admission** section can also hold a comma-separated issuer
allow-list. Leave it empty to rely on the broker policy, or set the Google and Okta issuer URLs to
require agreement between the broker and the UDP server.

The broker enforces Google Workspace `hd` and Okta `groups` policies. Use `hd` rather than an
email suffix for Google, and configure the Okta authorization server to emit `groups` in its ID
token.

## Web meeting invitations

Web participant URLs contain only the opaque meeting invitation token. They do not contain the
server password, generated username, OAuth client secret, or WebSocket URL. The WebGL client
fetches a short response from `/join/{token}/web-manifest` over HTTPS, obtains the organization
configuration from the returned `configUrl`, and only then starts the SSO-gated connection.

The invitation token is still a bearer capability: anyone who obtains the invitation URL can
attempt to join until the invitation is revoked or expires. Use HTTPS in production, keep the
broker's allowed Web origins narrow, and do not put invitation URLs in public logs or analytics.

For the local Docker deployment, the admin gateway terminates HTTPS at
`https://127.0.0.1:5081`. It generates a development CA and leaf certificate under
`Basis Server/Docker/sso/certs/`; that directory is ignored by git. Trust
`basis-local-ca.crt` in the development machine's keychain before opening the WebGL client, or
the browser will reject the manifest and OIDC configuration requests. The broker itself remains
HTTP-only on the private Compose network; only the browser-facing gateway is TLS-terminated.

## Accessing server settings

Server settings are not exposed as a web page. The generic **Settings → Admin** tab is available
to a Basis client whose DID has the `basis.permissions.view` permission; members of the built-in
`admin` group have that permission (and all other permissions). The **SSO admission** section is
currently deliberately local-host only, because it writes the server's `config.xml` and must not
expose broker secrets over the game protocol.

On a new Docker/headless server, set `BasisFirstAdmin` to your Basis DID before the first start.
In the Basis client, open **Settings → Developer → Identity (DID)**, tap the eye icon to reveal
the read-only value, and copy the complete `did:key:...` string. With SSO enabled, sign in to the
same Google/Okta account first: each SSO subject has its own locally stored DID.

For Docker, copy and edit the provided environment file:

```sh
cd Packages/com.basis.server/Docker
cp .env.example .env
# Edit .env: BasisFirstAdmin=did:key:... and RequireSso=true
docker compose --profile sso up -d --build
```

Do not generate a DID yourself or use another user's DID: it represents an Ed25519 keypair held
only by that client, and a different value will not authorize your client.

For an already running console-enabled server, add an administrator from the server console:

```text
/perm user group add did:key:YOUR_DID admin
```

The permission store persists this change in `config/permissions.xml`. Reconnect the client, then
open the Admin tab for moderation and other server controls. For a dedicated server, configure SSO
through `config/config.xml` or the Docker Compose SSO profile; the local-host Admin section
contains the SSO requirement switch, local-broker startup switch, pinned-public-key copy action,
and issuer allow-list. Broker provider secrets and signing keys are never shown in any UI.

## Broker contract

The broker is the only HTTPS component that receives an OAuth token. It validates the selected
provider, evaluates `hd`/groups policy, and issues a ticket with a maximum lifetime of sixty
seconds. The ticket contains the issuer, subject, expiry and the joining DID, and is HMAC-signed
with a key shared only by the broker and the Basis server. It must never contain an OAuth token.

The game server validates the ticket before accepting the DID challenge. A captured envelope is
encrypted to the pinned server key; even a decrypted ticket cannot authenticate another DID, and
its unique ticket ID is accepted only once.
