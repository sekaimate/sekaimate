#!/usr/bin/env sh
# Start Concierge for local standalone development.
set -eu

script_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
env_file="$script_dir/concierge.env"
config_file="$script_dir/appsettings.json"
binary="$script_dir/concierge"

if [ ! -x "$binary" ]; then
    echo "Run ./publish-for-basis-server.sh first, or build concierge/concierge." >&2
    exit 65
fi
if [ ! -f "$config_file" ] || [ ! -f "$env_file" ]; then
    echo "Run ./prepare-concierge.sh /absolute/path/to/config.xml first." >&2
    exit 65
fi

set -a
. "$env_file"
set +a
: "${LISTEN_ADDR:=127.0.0.1:5080}"
export LISTEN_ADDR
export BASIS_SSO_BROKER_CONFIG_PATH="$config_file"

echo "Starting Go Concierge on $LISTEN_ADDR"
echo "Health check: http://${LISTEN_ADDR%:*}/health"
exec "$binary"
