#!/bin/sh
# Makes the certificates this profile's settings name, which are not committed: a root CA, a server
# certificate for localhost, and a client certificate. What is already there is kept, so this can be
# run again safely. To make one again, delete its folder under certificates/joktan first.
set -eu
cd "$(dirname "$0")"

out=certificates/joktan
broker=containers/mqtt/certs/joktan/localhost
subject="/OU=Development/O=Joktan"
root=$out/joktan-root-ca

new_key() {
    openssl ecparam -name prime256v1 -genkey -noout | openssl pkcs8 -topk8 -nocrypt -out "$1"
    chmod 600 "$1"
}

# Signs a certificate for $1 in its own folder, with the extensions in $2.
issue() {
    folder=$out/$1
    [ -f "$folder/cert.pem" ] && return 0
    mkdir -p "$folder"
    new_key "$folder/key.pem"
    extensions=$(mktemp)
    printf '%b' "$2" > "$extensions"
    openssl req -new -key "$folder/key.pem" -subj "/CN=$1$subject" \
        | openssl x509 -req -days 825 -sha256 -CA "$root/cert.pem" -CAkey "$root/key.pem" -set_serial "0x$(openssl rand -hex 16)" \
            -extfile "$extensions" -out "$folder/cert.pem" 2>/dev/null
    rm -f "$extensions"
    cp "$folder/cert.pem" "$folder/chain.pem"
    cp "$root/cert.pem" "$folder/ca.pem"
    echo "Made $folder"
}

if [ ! -f "$root/cert.pem" ]; then
    # A new CA is not trusted by what the old one signed, so those must not be mixed.
    if [ -f "$out/localhost/cert.pem" ] || [ -f "$out/joktan-client/cert.pem" ]; then
        echo "$root is missing but certificates it signed are there. Delete them or restore it." >&2
        exit 1
    fi
    mkdir -p "$root"
    new_key "$root/key.pem"
    extensions=$(mktemp)
    printf 'basicConstraints=critical,CA:TRUE\nkeyUsage=critical,digitalSignature,keyCertSign,cRLSign\n' > "$extensions"
    openssl req -new -key "$root/key.pem" -subj "/CN=Joktan Root CA$subject" \
        | openssl x509 -req -days 3650 -sha256 -signkey "$root/key.pem" -set_serial "0x$(openssl rand -hex 16)" \
            -extfile "$extensions" -out "$root/cert.pem" 2>/dev/null
    rm -f "$extensions"
    cp "$root/cert.pem" "$root/chain.pem"
    cp "$root/cert.pem" "$root/ca.pem"
    echo "Made $root"
fi

issue localhost 'basicConstraints=CA:FALSE\nkeyUsage=critical,digitalSignature\nextendedKeyUsage=serverAuth\nsubjectAltName=DNS:localhost,IP:127.0.0.1,IP:::1,DNS:joktan\n'
issue joktan-client 'basicConstraints=CA:FALSE\nkeyUsage=critical,digitalSignature\nextendedKeyUsage=clientAuth\n'

# The files the MQTT clients in mqtt/mqtt.json use.
for client in main secondary; do
    folder=$out/client/$client
    [ -f "$folder/client.crt" ] && continue
    mkdir -p "$folder"
    cp "$root/cert.pem" "$folder/ca.crt"
    cp "$out/joktan-client/cert.pem" "$folder/client.crt"
    cp -p "$out/joktan-client/key.pem" "$folder/client.key"
    echo "Made $folder"
done

# The brokers' image is built with its own copy.
if [ ! -f "$broker/cert.pem" ]; then
    mkdir -p "$broker"
    cp -p "$out/localhost/"*.pem "$broker/"
    echo "Made $broker"
fi
