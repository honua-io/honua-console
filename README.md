# Honua Console

[![CI](https://github.com/honua-io/honua-console/actions/workflows/ci.yml/badge.svg)](https://github.com/honua-io/honua-console/actions/workflows/ci.yml)
[![Console E2E](https://github.com/honua-io/honua-console/actions/workflows/console-e2e.yml/badge.svg)](https://github.com/honua-io/honua-console/actions/workflows/console-e2e.yml)
[![OpenSSF Scorecard](https://api.securityscorecards.dev/projects/github.com/honua-io/honua-console/badge)](https://scorecard.dev/viewer/?uri=github.com/honua-io/honua-console)
[![License: Apache-2.0](https://img.shields.io/badge/License-Apache_2.0-blue.svg)](LICENSE)

Honua Console is the unified web console — and the admin/UI home — for the
[Honua](https://honua.io) geospatial platform. It is one Blazor web app that gives a
[honua-server](https://github.com/honua-io/honua-server) deployment its authoring,
catalog, operations, and sharing surfaces from a single origin and a single
deployable artifact. If you run honua-server and want a UI on top of it — as an
evaluator, self-hoster, or contributor — this is the repo.

| Surface | Route | What it covers |
|---|---|---|
| **Studio** | `/studio` | AI-assisted authoring and publishing of spatial queries, analyses, maps, dashboards, reports, forms, apps, and workflows |
| **Catalog** | `/catalog` | Data, layers, services, saved maps/dashboards/reports/forms/workflows, generated apps, metadata, and provenance |
| **Operate** | `/operate` | Publishing, jobs, service configuration, identity, connectors, deployment health, observability, and runtime administration |
| **Share** | `/share` | Public links, embeds, open-data pages, exports, and external publishing |

All four resolve from the same Blazor Web host (`src/Honua.Console.Web`): one
build, one deploy, one origin. Decision source:
[ADR-0001: Unified Honua Console Runtime](docs/adr/0001-unified-honua-console-runtime.md).

## Status

Pre-1.0 and under active development. Console is the convergence target that
replaces the previous split Portal/Admin web path; all Honua admin and UI work
lands here. Honest signal on maturity:

- **Server-bound by design.** Routes that need live data bind to honua-server
  and render an explicit *missing-binding* state when no server is configured —
  never mock data. In-memory demo shells are opt-in and test/demo-only.
- **Studio editors are converging.** The shared `/studio` package shell, the
  workflow editor, and the `/studio/form` builder bind to the live honua-server
  package lifecycle. The per-family editors (`/studio/query`, `/analysis`,
  `/map`, `/dashboard`, `/report`, `/app`) still run a local
  `studio-package-mock/v1` lifecycle projection and bind to the server on their
  own tickets — see [Studio Package Editor Routes](docs/studio/package-editor-routes.md).
- **Realtime alerting is capability-gated preview.** Operate jobs and events
  are first-release floor; alert/geofence rule surfaces light up only when the
  bound server advertises the capability.
- **The optional native desktop host is preview/deferred.** A .NET MAUI Blazor
  Hybrid host ([docs](docs/native/MAUI_BLAZOR_HOST.md)) adds client-cert
  trust/mTLS workflows on Windows/macOS; it is not a first-release deliverable
  and degrades to a no-op library on plain Linux/CI.

## Quick start

Prerequisites:

- **.NET 10 SDK**
- **Node.js >= 20** (only for the smoke/e2e harnesses and build-metadata stamping)
- A GitHub token with `read:packages` — the `Honua.Sdk.*` packages resolve from
  the `github-honua` GitHub Packages feed declared in [NuGet.config](NuGet.config),
  and GitHub Packages requires authentication even for public packages.

```bash
git clone https://github.com/honua-io/honua-console.git
cd honua-console

# Authenticate the Honua SDK package feed (once). This stores the credential in
# your user-level NuGet config — never put the token in the repo's tracked
# NuGet.config. Credentials are matched to the feed by source name.
dotnet nuget add source https://nuget.pkg.github.com/honua-io/index.json \
  --name github-honua \
  --username <your-github-username> --password <token-with-read:packages> \
  --store-password-in-clear-text \
  --configfile "$HOME/.nuget/NuGet/NuGet.Config"

dotnet restore Honua.Console.slnx
dotnet run --project src/Honua.Console.Web/Honua.Console.Web.csproj --urls http://127.0.0.1:5174
```

Open <http://127.0.0.1:5174> — `/studio`, `/catalog`, `/operate`, and `/share`
all resolve from the same host. Without a server binding you will see the
console shell with explicit missing-binding states on live routes.

### Connect it to a honua-server

Set a server base URL (and, for admin reads, an API key) before starting the
web host:

```bash
HONUA_SERVER_BASE_URL=http://127.0.0.1:5000 \
HONUA_ADMIN_API_KEY=dev-admin-key \
dotnet run --project src/Honua.Console.Web/Honua.Console.Web.csproj --urls http://127.0.0.1:5174
```

Read-only and explicitly headless paths may send the key as `X-API-Key`;
interactive mutations (proposal decisions, deploy submit/rollback) require a
forwardable operator bearer via the built-in per-operator BFF and fail closed
without one. See [Console authentication](docs/console-authentication.md) and
[Local and staging startup](docs/deployment/LOCAL_AND_STAGING.md).

### Or run it from honua-server's compose stack

honua-server's `docker-compose.yml` ships a `console` profile that starts the
Console next to the server, pre-wired with the server URL and admin key:

```bash
# in a honua-server checkout
HONUA_CONSOLE_IMAGE=<compatible-console-image> docker compose --profile console up -d
```

Trunk publishes a multi-architecture Console image at
`ghcr.io/honua-io/honua-console:nightly`. Release automation resolves that tag
to an immutable digest; self-hosted deployments should pin the same way:

```bash
HONUA_CONSOLE_IMAGE=ghcr.io/honua-io/honua-console@sha256:<digest> \
  docker compose --profile console up -d
```

The directory packaged into that image remains the deployable artifact defined
in [BUILD_ARTIFACT.md](docs/deployment/BUILD_ARTIFACT.md).

## Development

| Task | Command |
|---|---|
| Run the browser host | `dotnet run --project src/Honua.Console.Web/Honua.Console.Web.csproj --urls http://127.0.0.1:5174` |
| Fast local check (unit tests + web build) | `./scripts/fast-local-check.sh` |
| .NET unit tests | `dotnet test tests/Honua.Console.Native.Core.Tests/Honua.Console.Native.Core.Tests.csproj` |
| Format gate (CI-enforced) | `dotnet format Honua.Console.slnx --verify-no-changes` |
| Node tests (smoke + metadata, no npm deps) | `npm ci && npm test` |
| Parity smoke (in-memory contract shapes) | `npm run smoke:parity` |
| Workflow-package contract smoke | `npm run smoke:workflow` |
| Live end-to-end (Docker: PostGIS + Redis + honua-server + Playwright) | `make e2e-live` (or `npm run e2e:live`) |
| Publish the deployable artifact | `dotnet publish src/Honua.Console.Web/Honua.Console.Web.csproj -c Release -o artifacts/honua-console-web` |
| Verify vendored browser assets | `node scripts/vendor-assets.mjs` (offline; also runs inside `npm test`) |
| Re-vendor after a version bump | `node scripts/vendor-assets.mjs --update` |

### Vendored browser assets

The Console has no bundler, so third-party browser libraries are committed under
`src/Honua.Console.Shell/wwwroot/vendor/` and served from the Console's own
origin — never fetched from a CDN at page load (honua-console#333, #334).
Executing third-party code pulled at runtime into this origin would give it the
Blazor session and the admin-keyed map proxy, an air-gapped deployment would get
a broken surface, and the CSP would have to admit a script origin nothing else
needs.

Vendored today: MapLibre GL JS (map preview) and Vega / Vega-Lite / Vega-Embed
(chart preview). Cesium (`scene-viewer.js`) is the one remaining runtime-CDN
consumer and is tracked by honua-console#334: its `Build/Cesium` tree is tens of
megabytes of workers, assets, and widgets resolved dynamically through
`window.CESIUM_BASE_URL`, so where those bytes should live is its own decision
rather than a mechanical port. It is why `https://cdn.jsdelivr.net` is still in
the CSP.

Versions are pinned exactly in [`scripts/vendored-assets.json`](scripts/vendored-assets.json).
To bump one: change `version` there, run `node scripts/vendor-assets.mjs --update`,
and commit the rewritten assets together with `scripts/vendored-assets.lock.json`.
The script re-fetches from the npm registry, checks the tarball against npm's own
`dist.integrity`, and records a sha384 digest of every byte it writes; `npm test`
fails if a committed asset ever stops matching its digest, if a wwwroot interop
script reaches an origin nobody declared, or if the CSP and those scripts disagree
about which external origins are still needed.

The parity smoke ([docs/smoke/parity.md](docs/smoke/parity.md)) drives the
cross-surface publish → catalog → Studio → share/embed chain against in-process
adapters and a contract-version registry; the real-server gate is the
Testcontainers `ConsoleEndToEndSmokeTests` suite documented in
[LOCAL_AND_STAGING.md](docs/deployment/LOCAL_AND_STAGING.md). The one-command
live e2e harness is documented in [e2e/README.md](e2e/README.md).

### Opt-in live-server test lanes

These boot real infrastructure via Docker/Testcontainers and skip unless
explicitly enabled:

```bash
# Operate transition binding against a honua-server source checkout
HONUA_CONSOLE_RUN_LIVE_SERVER_TESTS=true \
HONUA_SERVER_PROJECT=/path/to/honua-server/src/Honua.Server/Honua.Server.csproj \
dotnet test tests/Honua.Console.Native.Core.Tests/Honua.Console.Native.Core.Tests.csproj --filter OperateTransitionLiveServerTests

# Image-based lanes (Share access, catalog discovery, Studio collaboration) against a pinned image
HONUA_CONSOLE_RUN_LIVE_SERVER_TESTS=true \
HONUA_CONSOLE_SERVER_IMAGE=ghcr.io/honua-io/honua-server:nightly \
HONUA_CONSOLE_SERVER_SCHEME=http \
HONUA_CONSOLE_SERVER_HEALTH_PATH=/healthz/live \
HONUA_CONSOLE_ADMIN_API_KEY='Console-Live-Admin-Key-2026!' \
HONUA_CONSOLE_SERVER_ENV='HONUA_ADMIN_PASSWORD=Console-Live-Admin-Key-2026!;HostValidation__Enabled=false' \
dotnet test tests/Honua.Console.IntegrationTests/Honua.Console.IntegrationTests.csproj --filter StudioMapCollaborationLiveServerTests
```

The Operate observability lane (`OperateObservabilityTestcontainersTests`) runs
when `HONUA_CONSOLE_OPERATE_SERVER_IMAGE` points at a honua-server image with
the admin Operate endpoints — or set `HONUA_CONSOLE_OPERATE_SERVER_CONTEXT` (and
optionally `HONUA_CONSOLE_OPERATE_SERVER_DOCKERFILE`) to build one from a
checkout. It skips when neither is configured or Docker is unavailable.

## Project layout

- `src/Honua.Console.Shell` — shared Razor routes, layout, pages, and the
  service seams (in-memory demo / live-server / unsupported implementations)
  where most UI lives.
- `src/Honua.Console.Contracts` — temporary SDK shim boundary for Console-side
  wire contracts until shared [honua-sdk-dotnet](https://github.com/honua-io/honua-sdk-dotnet)
  projections replace them.
- `src/Honua.Console.Web` — default browser host; independently buildable and
  deployable without MAUI or native services.
- `src/Honua.Console.Native.Core` — testable native-host services: environment
  profiles, token sessions, certificate pinning, the server trust gate, and the
  telemetry streaming proof.
- `src/Honua.Console.Native` — optional MAUI Blazor Hybrid desktop host
  (Windows/macOS) rendering the shared shell in a `BlazorWebView`.
- `tests/` — host-independent unit tests plus opt-in Testcontainers
  integration suites; `smoke/parity/` — Node smoke harness; `e2e/` — live
  Playwright harness.

## Documentation

| Area | Start here |
|---|---|
| Runtime decision | [ADR-0001: Unified Honua Console Runtime](docs/adr/0001-unified-honua-console-runtime.md) |
| Routes, RBAC, IA source of truth | [Console Route Map](docs/console-route-map.md) |
| Binding patterns for every feature slice | [Console Patterns Charter](docs/migration/CONSOLE_PATTERNS_CHARTER.md) |
| Authentication (API key vs operator bearer/BFF) | [Console authentication](docs/console-authentication.md) |
| Running locally, staging, real-server smoke | [Local and staging startup](docs/deployment/LOCAL_AND_STAGING.md) |
| Deployable artifact contract | [Build artifact contract](docs/deployment/BUILD_ARTIFACT.md) |
| Studio information model | [Studio Information Model and Workflows](docs/architecture/studio-information-model-and-workflows.md) |
| Studio editor routes and backend boundaries | [Studio Package Editor Routes](docs/studio/package-editor-routes.md) |
| Operate observability model | [Operate Observability Information Model](docs/architecture/operate-observability-information-model.md) |
| Adding a new route/feature slice | [Route Implementation Checklist](docs/reference/ROUTE_IMPLEMENTATION_CHECKLIST.md) |
| Shared Razor component APIs | [Shared Component API Reference](docs/reference/SHARED_COMPONENT_API.md) |
| Parity smoke CLI, evidence format, triage | [Console Parity Smoke](docs/smoke/parity.md) |
| Driving the UI with agents (Playwright MCP) | [Agent Browser Testing](docs/testing/AGENT_BROWSER_TESTING.md) |
| Native desktop host (preview) | [Optional MAUI Blazor Hybrid Host](docs/native/MAUI_BLAZOR_HOST.md) |
| Migration backlog and freeze policy | [Migration Backlog](docs/roadmap/HONUA_CONSOLE_MIGRATION_BACKLOG.md) · [Portal Freeze Policy](docs/migration/PORTAL_FREEZE_POLICY.md) |

Platform-wide docs live at <https://honua.gitbook.io/honuaio/>.

## Notable route contracts

A few externally visible contracts worth knowing (full detail in the
[Console Route Map](docs/console-route-map.md)):

- `/catalog` accepts Portal-compatible query keys (`q`, `type`, `tag`, `owner`,
  `visibility`, `sort`, `cursor`); `visibility` maps to the SDK field `sharing`,
  which never appears in the public URL.
- `/catalog/{idOrSlug}` and `/maps/{mapId}` accept anonymous public reads and
  public-link reads with `?token=<value>`.
- `/embed/maps/{mapId}` uses a shellless embed layout with `chrome`, `legend`,
  `zoom`, and `extent=W,S,E,N` options; token-authorized embeds carry the bearer
  in the `#embedToken=` fragment only — query-string tokens are rejected.
- `/version.json` on any deployed origin returns the build-metadata block used
  by release promotion and by the parity smoke's artifact/contract-drift check.

## Related Honua repos

| Repo | What it is |
|---|---|
| [honua-server](https://github.com/honua-io/honua-server) | Flagship multi-protocol geospatial server this console administers (GeoServices REST, OGC API, WMS/WFS/WMTS/WCS, STAC, vector tiles, MCP, gRPC) |
| [honua-helm](https://github.com/honua-io/honua-helm) | Helm chart — the Kubernetes deploy path for server + console |
| [honua-sdk-dotnet](https://github.com/honua-io/honua-sdk-dotnet) | .NET SDKs; the shared contract packages Console consumes |
| [honua-sdk-js](https://github.com/honua-io/honua-sdk-js) | JavaScript/TypeScript SDKs + MCP server |
| [honua-collect](https://github.com/honua-io/honua-collect) | Offline-first mobile field data collection app |
| [honua-esri-assess](https://github.com/honua-io/honua-esri-assess) | Esri footprint assessment CLI for migration discovery |

## Contributing

Before pushing, run the same gates CI enforces
([`.github/workflows/ci.yml`](.github/workflows/ci.yml)):

```bash
dotnet format Honua.Console.slnx
./scripts/fast-local-check.sh
npm ci && npm test
npm run smoke:parity
```

Follow the [Console Patterns Charter](docs/migration/CONSOLE_PATTERNS_CHARTER.md)
for routing, RBAC gates, error/empty/loading surfaces, and file layout — the
route map and charter decide cross-cutting questions once, so they are not
re-decided per ticket. Do not duplicate server/SDK DTOs when a shared contract
exists.

## Security

Report vulnerabilities to <security@honua.io>. See the organization
[security policy](https://github.com/honua-io/.github/blob/main/SECURITY.md).

## License

[Apache License 2.0](LICENSE). See also [NOTICE](NOTICE).
