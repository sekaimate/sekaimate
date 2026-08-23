#!/usr/bin/env sh
# Build Concierge beside a standalone Basis server.
# Usage: ./publish-for-basis-server.sh /absolute/path/to/BasisServer
set -eu

script_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
server_dir=${1:-}
if [ -z "$server_dir" ] || [ ! -d "$server_dir" ]; then
    echo "Usage: $0 /absolute/path/to/BasisServer" >&2
    exit 64
fi
if ! command -v go >/dev/null 2>&1; then
    echo "Go is required to build Concierge." >&2
    exit 69
fi

target_dir="$server_dir/concierge"
mkdir -p "$target_dir"
(cd "$script_dir" && CGO_ENABLED=0 go build -o "$target_dir/concierge" ./cmd/server)
chmod 755 "$target_dir/concierge"
if [ ! -f "$target_dir/appsettings.json" ]; then
    cp "$script_dir/appsettings.example.json" "$target_dir/appsettings.json"
    echo "Created $target_dir/appsettings.json — configure the OIDC providers before starting the server." >&2
fi
echo "Published Concierge to $target_dir"
echo "The Basis server starts it when RequireSso=true and AutoStartSsoBroker=true."
