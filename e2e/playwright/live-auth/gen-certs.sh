#!/usr/bin/env bash
# Regenerates the self-signed TLS certificate the live-auth proof stack uses for
# Keycloak (docker-compose.yml). The IdP must be HTTPS because honua-server's OIDC
# options validator enforces RequireHttps + an https:// Generic.Authority.
#
# host.docker.internal is in the SAN set so ONE hostname works from all three
# vantage points: the honua-server container (backchannel discovery/token calls),
# the host browser (authorize redirect), and the host-run Console.
set -euo pipefail
cd "$(dirname "$0")/certs"

# MSYS_NO_PATHCONV stops Git Bash on Windows from rewriting the -subj path.
MSYS_NO_PATHCONV=1 openssl req -x509 -newkey rsa:2048 -sha256 -days 30 -nodes \
  -keyout kc.key -out kc.crt \
  -subj "/CN=host.docker.internal" \
  -addext "subjectAltName=DNS:host.docker.internal,DNS:localhost,IP:127.0.0.1"

openssl x509 -in kc.crt -noout -subject -ext subjectAltName
