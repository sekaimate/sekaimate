#!/bin/sh
set -eu

cert_dir=/etc/nginx/certs
ca_cert="$cert_dir/basis-local-ca.crt"
ca_key="$cert_dir/basis-local-ca.key"
leaf_cert="$cert_dir/localhost.crt"
leaf_key="$cert_dir/localhost.key"

mkdir -p "$cert_dir"

if [ ! -f "$ca_cert" ] || [ ! -f "$ca_key" ]; then
    openssl req -x509 -newkey rsa:4096 -sha256 -nodes -days 3650 \
        -subj '/CN=Basis Local Development CA' \
        -addext 'basicConstraints=critical,CA:TRUE' \
        -addext 'keyUsage=critical,keyCertSign,cRLSign' \
        -addext 'subjectKeyIdentifier=hash' \
        -keyout "$ca_key" -out "$ca_cert"
    chmod 600 "$ca_key"
fi

if [ ! -f "$leaf_cert" ] || [ ! -f "$leaf_key" ]; then
    config=$(mktemp)
    trap 'rm -f "$config"' EXIT
    cat > "$config" <<'EOF'
[req]
distinguished_name=req_distinguished_name
req_extensions=req_extensions
prompt=no
[req_distinguished_name]
CN=localhost
[req_extensions]
subjectAltName=@alt_names
basicConstraints=critical,CA:FALSE
keyUsage=critical,digitalSignature,keyEncipherment
extendedKeyUsage=serverAuth
[alt_names]
DNS.1=localhost
IP.1=127.0.0.1
IP.2=::1
EOF
    openssl req -new -newkey rsa:2048 -sha256 -nodes \
        -keyout "$leaf_key" -out "$cert_dir/localhost.csr" -config "$config"
    openssl x509 -req -sha256 -days 825 \
        -in "$cert_dir/localhost.csr" -CA "$ca_cert" -CAkey "$ca_key" -CAcreateserial \
        -out "$leaf_cert" -extfile "$config" -extensions req_extensions
    rm -f "$cert_dir/localhost.csr" "$cert_dir/basis-local-ca.srl"
    chmod 600 "$leaf_key"
fi

exec nginx -g 'daemon off;'
