#!/usr/bin/env sh
# Publishes the broker beside a standalone Basis server so it starts with that server.
# Usage: ./publish-for-basis-server.sh /absolute/path/to/BasisServer
set -eu

script_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
server_dir=${1:-}
if [ -z "$server_dir" ] || [ ! -d "$server_dir" ]; then
    echo "Usage: $0 /absolute/path/to/BasisServer" >&2
    exit 64
fi
if ! command -v dotnet >/dev/null 2>&1; then
    echo "dotnet 8 SDK is required. Install it from https://dotnet.microsoft.com/download/dotnet/8.0" >&2
    exit 69
fi

target_dir="$server_dir/sso-broker"
dotnet publish "$script_dir/BasisSsoBroker.csproj" --configuration Release --output "$target_dir"
if [ ! -f "$target_dir/appsettings.json" ]; then
    cp "$script_dir/appsettings.example.json" "$target_dir/appsettings.json"
    echo "Created $target_dir/appsettings.json — configure Google / Okta before starting the server." >&2
fi
echo "Published broker to $target_dir"
echo "The Basis server will start it automatically when RequireSso=true and AutoStartSsoBroker=true."
