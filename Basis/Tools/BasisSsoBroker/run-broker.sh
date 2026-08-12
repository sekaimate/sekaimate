#!/usr/bin/env sh
# Starts the broker for local development or behind a TLS reverse proxy.
set -eu

script_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
env_file="$script_dir/broker.env"
config_file="$script_dir/appsettings.json"

if ! command -v dotnet >/dev/null 2>&1; then
    echo "dotnet 8 SDK is required. Install it from https://dotnet.microsoft.com/download/dotnet/8.0" >&2
    exit 69
fi
if [ ! -f "$config_file" ] || [ ! -f "$env_file" ]; then
    echo "Run ./prepare-broker.sh /absolute/path/to/config.xml first." >&2
    exit 65
fi
if grep -Eq 'REPLACE_WITH_|YOUR_OKTA_DOMAIN|example\.okta\.com|example\.com' "$config_file"; then
    echo "appsettings.json still has placeholder provider values; edit it before starting." >&2
    exit 65
fi

# broker.env is produced by prepare-broker.sh and contains only a single base64url key.
set -a
. "$env_file"
set +a
: "${BASIS_SSO_BIND_URL:=http://127.0.0.1:5080}"
export ASPNETCORE_URLS="$BASIS_SSO_BIND_URL"

echo "Starting Basis SSO broker on $ASPNETCORE_URLS"
echo "Health check: ${ASPNETCORE_URLS%/}/health"
exec dotnet run --project "$script_dir/BasisSsoBroker.csproj" --configuration Release --no-launch-profile
