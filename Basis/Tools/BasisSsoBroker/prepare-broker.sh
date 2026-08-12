#!/usr/bin/env sh
# Creates local broker configuration and a mode-600 signing-key environment file.
# Usage: ./prepare-broker.sh /absolute/path/to/Basis/config/config.xml
set -eu

script_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
config_path=${1:-}
if [ -z "$config_path" ] || [ ! -f "$config_path" ]; then
    echo "Usage: $0 /absolute/path/to/config.xml" >&2
    exit 64
fi

signing_key=$(sed -n 's:.*<SsoAdmissionTicketSigningKey>\([^<]*\)</SsoAdmissionTicketSigningKey>.*:\1:p' "$config_path" | head -n 1)
if [ -z "$signing_key" ]; then
    echo "No SsoAdmissionTicketSigningKey was found in $config_path." >&2
    echo "Enable SSO once in the Basis server Admin settings, then run this command again." >&2
    exit 65
fi

if [ ! -f "$script_dir/appsettings.json" ]; then
    cp "$script_dir/appsettings.example.json" "$script_dir/appsettings.json"
    echo "Created $script_dir/appsettings.json — replace the Google / Okta placeholders before starting." >&2
fi

umask 077
printf '%s\n' "BASIS_SSO_TICKET_SIGNING_KEY=$signing_key" > "$script_dir/broker.env"
chmod 600 "$script_dir/broker.env"
echo "Created $script_dir/broker.env (owner-readable only)."
echo "Next: edit appsettings.json, then run ./run-broker.sh"
