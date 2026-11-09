# Console live e2e harness

## One command

```sh
export HONUA_CONSOLE_E2E_SERVER_IMAGE='ghcr.io/honua-io/honua-server@sha256:<digest>'
make e2e-live          # or: npm run e2e:live
```

Replace `<digest>` with the selected immutable server image digest. For local
source-build diagnostics, an explicit local image tag is also accepted; record
its source revision and image ID separately from release evidence.

### Chiseled server diagnostics

For an image that includes `/app/honua-healthcheck`, enable the compiled
readiness probe in the live runner:

```sh
HONUA_CONSOLE_E2E_NATIVE_PROBE=true make e2e-live
```

The runner applies `e2e/docker-compose.native-probe.yml` during startup and
teardown. The older published images need the existing Bash probe; enable this
option only for images containing the native probe. The live LLM runner does
not support this option; using that stack requires adding the overlay's `-f`
argument to `composeArgs()` in `e2e/run-live-llm.mjs`.

### Prerequisites

- Docker (with Compose v2 — `docker compose` as a sub-command)
- Node.js >= 20 with `npx` on `PATH`
- .NET 10 SDK (the Console host starts via `dotnet run`)
- `openssl` and `bash` (the runner generates a throwaway TLS cert for the test IdP)
- Outbound HTTPS (service-import specs contact public source GIS platform sample servers)

## What it does

1. Pulls `postgis/postgis:16-3.4`, `redis:7-alpine`, and the explicitly selected
   `HONUA_CONSOLE_E2E_SERVER_IMAGE`.
2. Starts the stack (`docker-compose.yml` plus the `docker-compose.auth.yml`
   Keycloak overlay) via `docker compose up -d --wait`; all services must be
   healthy before Playwright runs.
3. Boots the Console (`src/Honua.Console.Web`) via `dotnet run` (managed by
   Playwright's `webServer` hook) bound to `http://127.0.0.1:5274`.
4. Runs `playwright test --config playwright.live.config.ts` against the live
   stack (specs under `e2e/playwright/live/specs/`).
5. Tears down the stack with `docker compose down -v` and exits with
   Playwright's exit code — a failing spec fails the command.

## Stack topology

```
┌─────────────────────────────────────────────┐
│  shared network namespace (postgis)          │
│                                             │
│  postgis  :5544  ← PGPORT=5544             │
│  redis    :6379                             │
│  honua-server :8080  (exposed as :8088)    │
└─────────────────────────────────────────────┘
```

Redis and honua-server join PostGIS's network namespace (`network_mode:
"service:postgis"`). Inside honua-server, `localhost:5544` reaches PostGIS and
`localhost:6379` reaches Redis. This matches the hardcoded host/port the
connection specs fill in.

## Seed data

`e2e/initdb/01-seed.sql` runs on first container start and creates
`public.e2e_layer_src` — three polygon features in EPSG:3857. This table is the
source for the `services-layers` publish workflow and the `studio-results` specs.
`e2e/initdb/03-dedicated-sources.sql` creates `public.e2e_golive_src`,
`public.e2e_settings_src` and `public.e2e_types_src` (same shape) for `data-flow-go-live`,
`service-settings` and `service-types`. It also creates `public.e2e_folder_src`
for `folder-management`.
honua-server backs a source table with one layer only, so a spec that seeds its own layer
needs a table no other spec publishes. `data-flow-go-live`, `service-settings` and
`service-types` seed through `live/seeded-layer.ts`, which keeps the seed connection
and reuses the layer on a retry. `services-layers` publishes `e2e_layer_src` and
`folder-management` publishes `e2e_folder_src` through the UI; both expect a fresh testbed.

## What the live specs prove

| Spec file | What it validates |
|-----------|-------------------|
| `connections.live.spec.ts` | PostGIS connection create/test, secret-reference connections, duplicate name guard |
| `file-import.live.spec.ts` | Format gating (unsupported rejected), GeoJSON upload flow + progress |
| `service-import.live.spec.ts` | URL validation, auth gating, source GIS platform FeatureServer discovery, select-all/deselect-all, import job |
| `services-layers.live.spec.ts` | Full publish workflow: connection → table → service; verified via catalog + FeatureServer metadata + live query |
| `data-flow-go-live.live.spec.ts` | `/operate/data/new` Go live on an existing layer: sets exactly the chosen protocols and Public visibility, then the layer is queried anonymously |
| `service-settings.live.spec.ts` | On its own seeded service: protocol enable/disable via UI takes effect on live endpoints; access policy gates anonymous reads |
| `service-types.live.spec.ts` | Coverage matrix for all service creation paths; the PostGIS connection path runs on its own seeded service, the others are documented skips |
| `studio-results.live.spec.ts` | Studio Query/Map/Form/Analysis/Workflow builders each render a live, data-bound final output |

## Environment overrides

| Variable | Default | Purpose |
|----------|---------|---------|
| `HONUA_CONSOLE_E2E_SERVER_URL` | `http://127.0.0.1:8088` | honua-server base URL (for Playwright + Console) |
| `HONUA_CONSOLE_E2E_ADMIN_KEY` | `honua-console-dev-key` | `X-API-Key` sent to admin endpoints |
| `HONUA_CONSOLE_E2E_NATIVE_PROBE` | unset | Set to `true` to use the compiled server readiness probe |
| `HONUA_CONSOLE_E2E_LIVE_PORT` | `5274` | Console port (Playwright webServer); must match the realm's registered callback |
