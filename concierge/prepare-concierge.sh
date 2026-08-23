#!/usr/bin/env sh
# Prepare standalone Concierge configuration from a Basis config.xml.
# Usage: ./prepare-concierge.sh /absolute/path/to/Basis/config/config.xml
set -eu

script_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
config_path=${1:-}
if [ -z "$config_path" ] || [ ! -f "$config_path" ]; then
    echo "Usage: $0 /absolute/path/to/config.xml" >&2
    exit 64
fi

signing_key=$(sed -n 's:.*<SsoAdmissionTicketSigningKey>\([^<]*\)</SsoAdmissionTicketSigningKey>.*:\1:p' "$config_path" | head -n 1)
transport_public_key=$(sed -n 's:.*<SsoTransportPublicKey>\([^<]*\)</SsoTransportPublicKey>.*:\1:p' "$config_path" | head -n 1)
if [ -z "$signing_key" ] || [ -z "$transport_public_key" ]; then
    echo "SSO signing and transport public keys were not found in $config_path." >&2
    echo "Enable SSO once in the Basis server settings, then run this command again." >&2
    exit 65
fi

if [ ! -f "$script_dir/appsettings.json" ]; then
    cp "$script_dir/appsettings.example.json" "$script_dir/appsettings.json"
    echo "Created $script_dir/appsettings.json — configure the OIDC providers before starting." >&2
fi

umask 077
printf '%s\n' "BASIS_SSO_TICKET_SIGNING_KEY=$signing_key" "BASIS_SSO_TRANSPORT_PUBLIC_KEY=$transport_public_key" > "$script_dir/concierge.env"
chmod 600 "$script_dir/concierge.env"
echo "Created $script_dir/concierge.env (owner-readable only)."
