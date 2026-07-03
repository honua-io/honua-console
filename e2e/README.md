# Console live e2e harness

## One command

```sh
make e2e-live          # or: npm run e2e:live
```

### Prerequisites

- Docker (with Compose v2 — `docker compose` as a sub-command)
- Node.js >= 20 with `npx` on `PATH`
- .NET 10 SDK (the Console host starts via `dotnet run`)
- Outbound HTTPS (service-import specs contact public ArcGIS sample servers)

## What it does

1. Pulls `postgis/postgis:16-3.4`, `redis:7-alpine`, and
   `ghcr.io/honua-io/honua-server:nightly-aot`.
2. Starts the stack via `docker compose up -d --wait`; all services must be
   healthy before Playwright runs.
3. Boots the Console (`src/Honua.Console.Web`) via `dotnet run` (managed by
   Playwright's `webServer` hook) bound to `http://127.0.0.1:5176`.
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

## What the live specs prove

| Spec file | What it validates |
|-----------|-------------------|
| `connections.live.spec.ts` | PostGIS connection create/test, secret-reference connections, duplicate name guard |
| `file-import.live.spec.ts` | Format gating (unsupported rejected), GeoJSON upload flow + progress |
| `service-import.live.spec.ts` | URL validation, auth gating, ArcGIS FeatureServer discovery, select-all/deselect-all, import job |
| `services-layers.live.spec.ts` | Full publish workflow: connection → table → service; verified via catalog + FeatureServer metadata + live query |
| `service-settings.live.spec.ts` | Protocol enable/disable via UI takes effect on live endpoints; access policy gates anonymous reads |
| `service-types.live.spec.ts` | Coverage matrix for all service creation paths |
| `studio-results.live.spec.ts` | Studio Query/Map/Form/Analysis/Workflow builders each render a live, data-bound final output |

## Environment overrides

| Variable | Default | Purpose |
|----------|---------|---------|
| `HONUA_CONSOLE_E2E_SERVER_URL` | `http://127.0.0.1:8088` | honua-server base URL (for Playwright + Console) |
| `HONUA_CONSOLE_E2E_ADMIN_KEY` | `honua-console-dev-key` | `X-API-Key` sent to admin endpoints |
| `HONUA_CONSOLE_E2E_LIVE_PORT` | `5176` | Console port (Playwright webServer) |
