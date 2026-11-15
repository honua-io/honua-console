#!/usr/bin/env bash
# Exercise the published bundle through the Server quickstart, never LegacyHost.
# SERVER_SOURCE is an immutable honua-server checkout containing the static edge.
set -euo pipefail
: "${SERVER_SOURCE:?Set SERVER_SOURCE to the pinned server source checkout}"
: "${SERVER_IMAGE:?Set SERVER_IMAGE to the immutable serving image}"
: "${EXECUTOR_IMAGE:?Set EXECUTOR_IMAGE to its matching immutable executor image}"
: "${CONSOLE_BUNDLE:?Set CONSOLE_BUNDLE to the static release zip}"
: "${CONSOLE_BUNDLE_SHA256:?Set the independently selected bundle checksum}"
for image in "$SERVER_IMAGE" "$EXECUTOR_IMAGE"; do
  [[ "$image" =~ @sha256:[a-f0-9]{64}$ ]] || { echo 'Images must be digest pinned' >&2; exit 1; }
done
[[ "$CONSOLE_BUNDLE_SHA256" =~ ^[a-f0-9]{64}$ ]] || { echo 'Invalid bundle SHA-256' >&2; exit 1; }
root=$(pwd)
out="$root/artifacts/shipping-live"
mkdir -p "$out"
work=$(mktemp -d)
unset COMPOSE_PROJECT_NAME
cleanup() {
  if [[ -f "$work/package/.env" ]]; then
    (cd "$work/package" && docker compose ps -q honua | xargs -r docker inspect \
      --format '{{json .State.Health}}' > "$out/health.json" 2>&1 || true)
    (cd "$work/package" && docker compose logs --no-color > "$out/compose.log" 2>&1 || true)
    (cd "$work/package" && docker compose down -v --remove-orphans > /dev/null 2>&1 || true)
  fi
  rm -rf "${work:?}"
}
trap cleanup EXIT
# Package validation verifies the checksum and rejects unsafe archive entries.
python3 "$SERVER_SOURCE/scripts/docker/package_quickstart.py" \
  --server-image "$SERVER_IMAGE" --executor-image "$EXECUTOR_IMAGE" \
  --worker-image "$SERVER_IMAGE" --version shipping-live --output "$work/package" \
  --console-bundle "$CONSOLE_BUNDLE" --console-bundle-sha256 "$CONSOLE_BUNDLE_SHA256"
# Only the disposable replay may resolve the fixture's existing environment secret.
# Persist the override in quickstart's explicit file list for every Compose command.
cat > "$work/package/compose.override.yaml" <<'YAML'
services:
  honua:
    environment:
      Security__RequestSecretReferences__AllowedEnvironmentVariables__0: ConnectionStrings__DefaultConnection
YAML
(
  cd "$work/package"
  python3 quickstart.py --profile console --init-only > "$out/quickstart-init.log" 2>&1
  python3 - <<'PY'
from pathlib import Path
import quickstart

env_file = Path('.env')
values = quickstart.parse_environment(env_file.read_text().splitlines())
separator = values.get('COMPOSE_PATH_SEPARATOR', ':')
quickstart.update_environment(env_file, {
    'COMPOSE_FILE': values['COMPOSE_FILE'] + separator + 'compose.override.yaml',
})
PY
  python3 quickstart.py --profile console > "$out/quickstart.log" 2>&1
)
# The stack is disposable: compile a real service fixture without exercising publication approval.
python3 "$root/e2e/seed-shipping-service.py" "$SERVER_SOURCE" > "$work/seed.sql"
(
  cd "$work/package"
  docker compose exec -T postgres sh -c 'exec psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -v ON_ERROR_STOP=1' \
    < "$work/seed.sql" > "$out/seed.log"
  docker compose exec -T redis redis-cli FLUSHDB
  docker compose restart honua
) > "$out/restart.log" 2>&1
for attempt in {1..60}; do
  if curl -kfsS https://localhost:18083/healthz/ready > /dev/null 2>&1; then break; fi
  sleep 2
done
curl -kfsS https://localhost:18083/healthz/ready > /dev/null
export HONUA_SHIPPING_PASSWORD_FILE="$work/package/secrets/honua_admin_password"
cd "$root/e2e/playwright"
npx --no-install playwright test --config playwright.shipping.config.ts
