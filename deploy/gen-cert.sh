#!/usr/bin/env sh
# Generates a self-signed TLS certificate for the web admin into deploy/certs/ (server.crt, server.key).
# Uses an openssl container, so nothing needs to be installed on the host besides Docker.
#
#   ./deploy/gen-cert.sh [host-or-ip]      # default: SERVER_HOST from deploy/.env, else 192.168.1.28
set -eu
cd "$(dirname "$0")"

HOST="${1:-}"
if [ -z "$HOST" ] && [ -f .env ]; then HOST="$(grep -E '^SERVER_HOST=' .env | cut -d= -f2- | tr -d '\r')"; fi
HOST="${HOST:-192.168.1.28}"

case "$HOST" in
  *[!0-9.]*) SAN="DNS:$HOST" ;;   # contains letters -> DNS name
  *)         SAN="IP:$HOST"  ;;   # digits and dots only -> IP address
esac

mkdir -p certs
# Run as the calling user so the files are owned by them (a root-owned key breaks the chmod below).
docker run --rm -u "$(id -u):$(id -g)" -v "$(pwd)/certs:/certs" alpine/openssl req -x509 -newkey rsa:2048 -nodes -days 825 \
  -keyout /certs/server.key -out /certs/server.crt \
  -subj "/CN=$HOST/O=Lab Monitoring" -addext "subjectAltName=$SAN"
chmod 644 certs/server.crt
chmod 600 certs/server.key   # nginx reads it as root inside the container, so 600 is enough
echo "Created deploy/certs/server.crt for $HOST ($SAN)."
echo "Import server.crt as a trusted root on every admin PC that opens https://$HOST/ (see docs/DEPLOY.md)."
