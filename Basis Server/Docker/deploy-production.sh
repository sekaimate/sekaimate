#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 3 ]]; then
  echo "Usage: $0 <hostname> <letsencrypt-email> <web-client-origin>" >&2
  exit 2
fi

hostname="$1"
email="$2"
web_client_origin="$3"

if [[ ! "$hostname" =~ ^[a-zA-Z0-9.-]+$ ]]; then
  echo "Invalid hostname: $hostname" >&2
  exit 2
fi
if [[ "$email" != *@* ]]; then
  echo "Invalid email: $email" >&2
  exit 2
fi
if [[ ! "$web_client_origin" =~ ^https://[a-zA-Z0-9.:-]+$ ]]; then
  echo "Web client origin must be an HTTPS origin without a path." >&2
  exit 2
fi

script_directory="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
certificate_directory="$script_directory/certificates"
letsencrypt_directory="$certificate_directory/letsencrypt"
certificate_path="$certificate_directory/basis-server.pfx"
secret_path="$certificate_directory/pfx-password"

mkdir -p "$letsencrypt_directory"
if [[ ! -f "$secret_path" ]]; then
  docker run --rm alpine:3.22 sh -c 'head -c 32 /dev/urandom | od -An -tx1 | tr -d " \n"' > "$secret_path"
  chmod 600 "$secret_path"
fi
certificate_password="$(<"$secret_path")"

docker run --rm \
  -p 80:80/tcp \
  -v "$letsencrypt_directory:/etc/letsencrypt" \
  certbot/certbot:latest certonly \
  --standalone \
  --non-interactive \
  --agree-tos \
  --keep-until-expiring \
  --email "$email" \
  --domain "$hostname"

docker run --rm \
  -e CERTIFICATE_HOSTNAME="$hostname" \
  -e CERTIFICATE_PASSWORD="$certificate_password" \
  -v "$letsencrypt_directory:/letsencrypt:ro" \
  -v "$certificate_directory:/output" \
  alpine:3.22 sh -c \
  'apk add --no-cache openssl >/dev/null && openssl pkcs12 -export -out /output/basis-server.pfx -inkey "/letsencrypt/live/$CERTIFICATE_HOSTNAME/privkey.pem" -in "/letsencrypt/live/$CERTIFICATE_HOSTNAME/fullchain.pem" -passout env:CERTIFICATE_PASSWORD'

export BASIS_SERVER_CERTIFICATE_PATH="$certificate_path"
export BASIS_SERVER_CERTIFICATE_PASSWORD="$certificate_password"
export BASIS_WEBSOCKET_ALLOWED_ORIGINS="$web_client_origin"

docker compose \
  --project-directory "$script_directory" \
  -f "$script_directory/docker-compose.yml" \
  -f "$script_directory/docker-compose.production.yml" \
  up -d --force-recreate
