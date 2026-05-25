#!/usr/bin/env bash
set -euo pipefail

# Opt-in real-server mTLS/trust integration lane (honua-console#44, AC#4).
#
# This lane boots a real honua-server (with PostgreSQL) via Testcontainers and asserts client
# certificate validation and cert-changed blocking against live data. It is intentionally NOT part of
# scripts/fast-local-check.sh, which stays Docker-free. Without the opt-in env the suite skips every
# fact with a clear reason, so this script is safe to run anywhere.
#
# Required to actually exercise the server:
#   HONUA_CONSOLE_INTEGRATION=true
#   HONUA_CONSOLE_SERVER_IMAGE=<honua-server image with honua-server#1171 mTLS API>   (or)
#   HONUA_CONSOLE_EXTERNAL_BASE_URL=https://<already-running-server>
#
# Optional:
#   HONUA_CONSOLE_ADMIN_TOKEN=<bearer token for the admin validate endpoint>
#   HONUA_CONSOLE_TRUST_PROFILE_ID=<expected server trust profile id>
#   HONUA_CONSOLE_TRUSTED_CERT_PFX=<path to a server-trusted client certificate PFX>
#   HONUA_CONSOLE_TRUSTED_CERT_PASSWORD=<PFX password>
#   HONUA_CONSOLE_DB_CONNECTION_ENV=<server env key for the Postgres connection string>
#   HONUA_CONSOLE_SERVER_ENV="KEY=VALUE;KEY2=VALUE2"   (extra image-specific server env)
#   HONUA_CONSOLE_SERVER_PORT / HONUA_CONSOLE_SERVER_SCHEME / HONUA_CONSOLE_SERVER_HEALTH_PATH

dotnet test tests/Honua.Console.IntegrationTests/Honua.Console.IntegrationTests.csproj --nologo --verbosity minimal
