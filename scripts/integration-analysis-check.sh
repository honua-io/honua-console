#!/usr/bin/env bash
set -euo pipefail

# Opt-in real-server saved-query (Analysis Content) integration lane (honua-console#52).
#
# This lane boots a real honua-server (with PostgreSQL/PostGIS) via Testcontainers and asserts the
# server-backed saved-query editor creates, reopens, and previews against the live Analysis Content API
# (honua-server#1182). It is intentionally NOT part of scripts/fast-local-check.sh, which stays
# Docker-free. Without the opt-in env the suite skips every fact with a clear reason, so this script is
# safe to run anywhere (Console Patterns Charter §11).
#
# Required to actually exercise the server:
#   HONUA_CONSOLE_INTEGRATION=true
#   HONUA_CONSOLE_SERVER_IMAGE=<honua-server image with honua-server#1182 Analysis Content API>   (or)
#   HONUA_CONSOLE_EXTERNAL_BASE_URL=https://<already-running-server>
#   HONUA_CONSOLE_ADMIN_API_KEY=<admin API key sent as X-API-Key to /api/v1/analysis/content>
#
# Optional:
#   HONUA_CONSOLE_ANALYSIS_LAYER_ID=<seeded queryable layer id for preview>   (default 0)
#   HONUA_CONSOLE_ANALYSIS_SERVICE=<seeded service name for preview>          (default test)
#   HONUA_CONSOLE_DB_CONNECTION_ENV=<server env key for the Postgres connection string>
#   HONUA_CONSOLE_SERVER_ENV="KEY=VALUE;KEY2=VALUE2"   (extra image-specific server env)
#   HONUA_CONSOLE_SERVER_PORT / HONUA_CONSOLE_SERVER_SCHEME / HONUA_CONSOLE_SERVER_HEALTH_PATH

dotnet test tests/Honua.Console.IntegrationTests/Honua.Console.IntegrationTests.csproj \
  --nologo --verbosity minimal \
  --filter "FullyQualifiedName~SavedQueryEditorIntegrationTests"
