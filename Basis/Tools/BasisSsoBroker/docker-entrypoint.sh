#!/usr/bin/env sh
# Docker sidecar: waits for the Basis server to create its SSO signing key, then starts.
set -eu

config_path=${BASIS_SERVER_CONFIG:-/basis-server-config/config.xml}
wait_seconds=${BASIS_SSO_CONFIG_WAIT_SECONDS:-60}
allow_missing_server_keys=${BASIS_SSO_ALLOW_MISSING_SERVER_KEYS:-false}
elapsed=0
signing_key=""
transport_public_key=""

# The state directory is a normal Docker volume, unlike a single-file bind mount. Copy its
# persisted config into the image before ASP.NET loads appsettings.json so save operations can
# use atomic rename safely on macOS Docker Desktop as well as Linux.
if [ -f /state/appsettings.json ]; then
    cp /state/appsettings.json /app/appsettings.json
fi

if [ "$allow_missing_server_keys" != "true" ]; then
    while [ "$elapsed" -lt "$wait_seconds" ]; do
        if [ -f "$config_path" ]; then
            signing_key=$(sed -n 's:.*<SsoAdmissionTicketSigningKey>\([^<]*\)</SsoAdmissionTicketSigningKey>.*:\1:p' "$config_path" | head -n 1)
            transport_public_key=$(sed -n 's:.*<SsoTransportPublicKey>\([^<]*\)</SsoTransportPublicKey>.*:\1:p' "$config_path" | head -n 1)
            if [ -n "$signing_key" ] && [ -n "$transport_public_key" ]; then break; fi
        fi
        sleep 1
        elapsed=$((elapsed + 1))
    done
fi

if [ "$allow_missing_server_keys" = "true" ]; then
    echo "Basis SSO broker: starting in local Admin UI mode without server SSO keys."
elif [ -z "$signing_key" ] || [ -z "$transport_public_key" ]; then
    echo "Basis SSO broker: SSO ticket or transport public key missing in $config_path after ${wait_seconds}s." >&2
    echo "Set RequireSso=true for the Basis server and ensure its config volume is shared with this service." >&2
    exit 1
else
    export BASIS_SSO_TICKET_SIGNING_KEY="$signing_key"
    export BASIS_SSO_TRANSPORT_PUBLIC_KEY="$transport_public_key"
fi

exec dotnet BasisSsoBroker.dll
