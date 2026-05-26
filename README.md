# Honua Console

Honua Console is the unified web surface for Honua.

It brings Studio, Catalog, Operate, and Share into one product surface and one deployment runtime:

- **Studio**: AI-assisted spatial query, analysis, map, dashboard, report, form, app, and workflow authoring and publishing.
- **Catalog**: data, layers, services, saved maps, dashboards, reports, forms, workflows, generated apps, metadata, and provenance.
- **Operate**: publishing, jobs, service configuration, identity, connectors, deployment health, observability, licensing, and runtime administration.
- **Share**: public links, embeds, open-data pages, exports, and external publishing flows.

## Decision Source

- [ADR-0001: Unified Honua Console Runtime](docs/adr/0001-unified-honua-console-runtime.md)
- [Optional MAUI Blazor Hybrid Host](docs/native/MAUI_BLAZOR_HOST.md)
- [Honua Console Migration Backlog](docs/roadmap/HONUA_CONSOLE_MIGRATION_BACKLOG.md)
- [Honua Console Route Map, RBAC, and Navigation](docs/console-route-map.md) — IA source of truth for Studio, Catalog, Operate, Share routes, gates, and exception surfaces. Migration tickets cite this map for URL shapes, gates, empty states, and smoke evidence.
- [Honua Studio Information Model And Workflows](docs/architecture/studio-information-model-and-workflows.md)
- [Studio Package Editor Routes](docs/studio/package-editor-routes.md) — Console-native editor routes, package-family coverage, the temporary lifecycle mock contract for the remaining `honua-console#39` editors, and the server-bound `/studio/form` exception from `honua-console#57`.
- [GitOps Metadata Publishing Information Model](docs/architecture/gitops-metadata-publishing-information-model.md)
- [GitOps Metadata Publishing Visualization Design](docs/architecture/gitops-metadata-publishing-visualization-design.md)
- [Temporal Data Viewer Information Model](docs/architecture/temporal-data-viewer-information-model.md)
- [Operate Observability Information Model](docs/architecture/operate-observability-information-model.md)
- [Honua Console Design Handoff](docs/design-handoff/README.md)
- [Legacy Admin Route Disposition](docs/migration/legacy-admin-route-disposition.md)
- [Operate Embed Contract](docs/operate/embed-contract.md)
- [Native Operate Transition Surface](docs/operate/native-transition-surface.md)

## Migration Coordination

The Console migration spans the in-repo child-ticket backlog and external owner tickets. Cross-cutting decisions are made once and reused; do not re-decide them per ticket.

- [Console Patterns Charter](docs/migration/CONSOLE_PATTERNS_CHARTER.md) - binding patterns (routing, RBAC, error/empty/loading surfaces, perf budgets, smoke conventions, file layout) for every porting and integration ticket.
- [`honua-portal` Freeze And Retirement Policy](docs/migration/PORTAL_FREEZE_POLICY.md) - soft/hard freeze gates and retirement trigger for `honua-portal`.
- [SDK Shim Policy](docs/migration/SDK_SHIM_POLICY.md) - when and how temporary .NET and browser SDK shims are acceptable while `honua-sdk-dotnet#166` and `honua-sdk-js#225` land.

## Deployment

- [Build artifact contract](docs/deployment/BUILD_ARTIFACT.md) - what `honua-devops` consumes from the Blazor Web publish output.
- [Local and staging startup](docs/deployment/LOCAL_AND_STAGING.md) — how to run, preview, and promote.

## Quickstart

```bash
dotnet restore Honua.Console.slnx
dotnet run --project src/Honua.Console.Web/Honua.Console.Web.csproj --urls http://127.0.0.1:5174
```

`/studio`, `/catalog`, `/operate`, and `/share` all resolve from the same Blazor Web host. See [BUILD_ARTIFACT.md](docs/deployment/BUILD_ARTIFACT.md) for the same-origin proxy contract and the `version.json` schema used by release notes.

## Parity Smoke

The Console parity smoke ([honua-console#9](https://github.com/honua-io/honua-console/issues/9))
is the one automated command that proves Console replaces the split
Portal/Admin path for the core buyer journey:

```sh
npm run smoke:parity
npm run smoke:parity -- --origin https://console.staging.honua.example
npm run smoke:parity:test
npm run smoke:workflow
```

Local and loopback runs (`127.0.0.1`, `localhost`, `[::1]`, or
`0.0.0.0`) read `artifacts/honua-console-web/version.json` when present
and otherwise use the committed fixture. Deployed-origin runs normalize
`--origin` to its URL origin, verify `<origin>/version.json`, and fail the
`devops/build-artifact` step if the artifact metadata is missing or
invalid. Evidence records `buildArtifact.source` as `"origin"`,
`"artifact"`, or `"fixture"` so release promotion can distinguish a
deployed artifact check from a local harness run.

See [docs/smoke/parity.md](docs/smoke/parity.md) for the CLI options,
scenario, owning-layer triage taxonomy, and evidence format. The focused
`smoke:workflow` command covers the Studio workflow-package contract path
(`honua-console#40`; real-server retrofit `honua-console#62`). It stays an
in-memory contract-shape stand-in; the live-data evidence is the xUnit
Testcontainers suite (`StudioWorkflowPackageIntegrationTests`).

## Current Status

This repo is the target home for porting current `honua-portal` logic and converging the long-term web surface. The Console IA is fixed in [docs/console-route-map.md](docs/console-route-map.md) ([honua-console#3](https://github.com/honua-io/honua-console/issues/3)); the Blazor Web Console shell and shared Razor component library scaffold lands under [honua-console#2](https://github.com/honua-io/honua-console/issues/2). The scaffold now also includes an independently deployable Blazor web host and an optional .NET MAUI Blazor Hybrid native host ([honua-console#26](https://github.com/honua-io/honua-console/issues/26)) for operator/power-user workflows.

Native Operate transition routes for connections, resources, services, layers, and settings are documented in [Native Operate Transition Surface](docs/operate/native-transition-surface.md). They use bounded Console view models projected from live honua-server admin endpoints when `Honua:Server:BaseUrl` or `HONUA_SERVER_BASE_URL` is set to an absolute HTTP(S) URL. Without a valid server binding, the routes render an explicit missing-binding state; seeded Operate data is limited to tests or the explicit demo opt-in until `honua-sdk-dotnet` admin projections replace the temporary HTTP shim.

## Project Layout

- `src/Honua.Console.Shell`: shared Razor routes, layout, route map, environment profile models, account session interfaces, the host-capability/connection-manager seam, native Operate transition surfaces, catalog/share route-slice surfaces, and native streaming proof interface.
- `src/Honua.Console.Contracts`: temporary SDK shim boundary for Console-side contracts (including the honua-sdk-dotnet#166 environment-trust shapes and honua-server#1171 validate wire contracts) until the shared .NET SDK projections replace them.
- `src/Honua.Console.Web`: default browser Console host. It references the shared shell and stays independently buildable/deployable without MAUI or native services; native gRPC, mTLS, and trust validation render as unsupported.
- `src/Honua.Console.Native.Core`: testable native host services for persisted environment profiles, account-token sessions, certificate references, HTTP/gRPC connection creation that enforces pinned server fingerprints when present, the server-bound trust gate (cert-changed blocking, acknowledge/revalidate, unreachable-state preservation), and the deterministic telemetry streaming proof.
- `src/Honua.Console.Native`: optional MAUI Blazor Hybrid host for desktop operator workflows. It renders the shared shell in a `BlazorWebView` and backs profile/session storage with MAUI secure storage.
- `tests/Honua.Console.Native.Core.Tests`: host-independent coverage for route boundaries, profile persistence, native connection setup, the trust gate, and the streaming proof contract, plus opt-in Testcontainers coverage for the live honua-server Operate binding.
- `tests/Honua.Console.IntegrationTests`: opt-in Testcontainers suite asserting mTLS/trust behavior and that the server-bound Studio form builder (`/studio/form`, `honua-console#57`) renders from live honua-server form package data (`honua-server#1184`) against a real honua-server (Console Patterns Charter section 11); skips without Docker.

## Local Usage

Run the browser Console:

```bash
dotnet run --project src/Honua.Console.Web/Honua.Console.Web.csproj
```

Bind the Operate transition pages to a local honua-server by setting
`HONUA_SERVER_BASE_URL` and, when needed, `HONUA_ADMIN_API_KEY` before
starting the web host. The API key is sent to admin endpoints as
`X-API-Key`.

Run the live Operate integration evidence only when Docker and a honua-server
checkout are available:

```bash
HONUA_CONSOLE_RUN_LIVE_SERVER_TESTS=true \
HONUA_SERVER_PROJECT=/path/to/honua-server/src/Honua.Server/Honua.Server.csproj \
dotnet test tests/Honua.Console.Native.Core.Tests/Honua.Console.Native.Core.Tests.csproj --filter OperateTransitionLiveServerTests
```

Validate the shared shell and native-core behavior without a desktop MAUI toolchain:

```bash
./scripts/fast-local-check.sh
```

That script runs the same host-independent checks directly:

```bash
dotnet test tests/Honua.Console.Native.Core.Tests/Honua.Console.Native.Core.Tests.csproj
dotnet build src/Honua.Console.Web/Honua.Console.Web.csproj
```

## Catalog, Share, And Embed Route Slice

The Blazor shell includes the catalog/share parity route slice for
`honua-console#34` behind the temporary .NET SDK shim boundary in
`src/Honua.Console.Contracts`.

- `/catalog` requires a signed-in workspace session and accepts the
  Portal-compatible query keys `q`, `type`, `tag`, `owner`, `visibility`,
  `sort`, and `cursor`. `visibility` is mapped to the SDK request field
  `sharing`; do not add `sharing` to the public URL query.
- `/catalog/{idOrSlug}` and `/maps/{mapId}` accept anonymous public reads
  without a token and public-link reads with `?token=<value>`.
  Authenticated reads continue to expose Studio and Share actions according
  to item policy, and do not preserve stale public-link tokens in action
  URLs. Anonymous reads hide those actions. Catalog detail tabs use
  `?tab=overview|versions|lineage|bindings|publication|permissions|activity|usage`;
  unknown tabs fall back to overview.
- `/maps/new?from=<itemId>` requires a signed-in workspace session and
  hydrates an unsaved draft map from a supported service or layer catalog
  item. Its Studio continuation URL is
  `/studio?source=catalog&itemId=<itemId>`.
- `/share`, `/share/public`, and `/public` list public open-data service,
  layer, and document items. `/share/public/items/{idOrSlug}` and
  `/public/items/{idOrSlug}` serve the eligible item detail page.
- `/embed/maps/{mapId}` uses the shellless embed layout. It accepts
  Portal-compatible `chrome`, `legend`, `zoom`, and `extent=W,S,E,N`
  query options. Public embeddable maps may render without a token;
  token-authorized embeds must put the bearer in `#embedToken=<value>`.
  Query-string `token` or `embedToken` is rejected by the route contract.

The optional native host targets Windows and macOS desktop builds. See [Optional MAUI Blazor Hybrid Host](docs/native/MAUI_BLAZOR_HOST.md) for workload, publish, profile, mTLS, and streaming-proof details.

Until parity is accepted, source behavior remains in:

- `honua-portal` for current Portal, Catalog, Share, and Studio proof work.
- `honua-server-admin` for current legacy Admin and operator workflows.
- `honua-sdk-dotnet` for Console .NET clients, native gRPC paths, and shared contract packages.
- `honua-sdk-js` for browser-safe SDK contracts and generated app runtime.
- `honua-server` for server-owned metadata, content, RBAC, provenance, and package APIs.
- `honua-devops` for the single deployable artifact and release pipeline.

## Operate Observability Usage

The native Blazor Operate observability workspace is available at
`/operate` and `/operate/observability`, with deep links for
`/operate/events/{eventId}`, `/operate/alerts/{alertId}`, and
`/operate/jobs/{jobRunId}`.

Runtime data comes from the active environment profile's honua-server
admin APIs through `IConsoleOperateObservabilityClient` and the temporary
`src/Honua.Console.Contracts/OperateObservabilityContracts.cs` SDK shim.
The client uses `/api/v1/admin/version`, `/api/v1/admin/capabilities`,
`/api/v1/admin/observability/*`, `/api/v1/admin/alerts/*`,
`/api/v1/admin/jobs/*`, and `/api/v1/admin/investigations/*`;
`OperateObservabilityFixture` remains test/scaffolding data only.

The response contract preserves the Console state vocabulary:
`unknown`, `unsupported`, `missing`, `disabled`, `not configured`, and
`unconfigured` telemetry are neutral states; missing, forbidden,
unsupported, and unavailable admin reads render through the shared Operate
section status surface; AI advisory text appears beside raw evidence
links; invalid realtime/geofence rules cannot be enabled; and Studio,
publishing, GitOps, temporal, alert delivery, import, and maintenance jobs
share the `/operate/jobs/{jobRunId}` detail surface.

Event and alert deep links select the matching row from the loaded live
server page. If `/operate/events/{eventId}` or `/operate/alerts/{alertId}`
contains an id that is not in that live page, the detail panel renders the
shared missing state rather than unrelated data; only the route without an
id defaults to the first returned row. Job deep links read live job detail
plus logs and artifacts, while job action buttons render from the
server-declared descriptors on the detail response and remain non-mutating
in this slice. Rule-health, geofence-zone, investigation-detail, and job
log/artifact sub-resource failures are surfaced beside the surrounding live
data instead of being collapsed into empty states.

The live server integration test is
`OperateObservabilityTestcontainersTests`. Set
`HONUA_CONSOLE_OPERATE_SERVER_IMAGE` to a honua-server image containing the
admin Operate endpoints to run it. Alternatively set
`HONUA_CONSOLE_OPERATE_SERVER_CONTEXT` to a honua-server checkout and,
when needed, `HONUA_CONSOLE_OPERATE_SERVER_DOCKERFILE` to the Dockerfile
path relative to that context; the test builds an ephemeral image before
starting PostgreSQL and the server. It skips when neither an image nor a
build context is configured, or when Docker is unavailable.

## Studio Contract Notes

Studio authoring is modeled as shared package contracts, not separate Console-only schemas. The canonical model covers workspaces, content items, content versions, Studio projects, conversations/provenance, packages, data bindings, publications, and job runs.

The `/studio` route is the shared Razor package shell (real-server revisit `honua-console#61` of the merged `honua-console#38` slice); the same shell is also mounted for `/studio/proof`, `/studio/drafts?source=<kind>&id=<itemId>`, and `/studio/apps/:itemId/preview` route compatibility. It lets a builder choose a workflow family, submit a prompt, answer structured clarification questions, inspect the active package, run server validation and preview-planning, save a content version, and publish. Draft/validation/preview-plan/content-version/publish bind to the honua-server package lifecycle (`honua-server#1180`/`#1181`) through the `IStudioPackageLifecycleClient` shim in `Honua.Console.Contracts`; prompt and clarification stay Console-local UX. Draft, Saved version, and Published are the lifecycle states; Preview is a transient preview-plan action, and rendered preview output / generated-app preview remains a labeled missing-binding state. When no server base address is configured the shell renders a missing-binding surface instead of mock package data; the in-memory authoring shell is demo/test-only (`AddHonuaConsoleDemoStudioAuthoringShell`).

Package families include query, analysis, map, dashboard, report, form, app, workflow, and publication packages. The shared Studio package lifecycle creates or updates server-owned content item versions and publication records for the generated-package shell; `/studio/form` uses the dedicated honua-server form package lifecycle from `honua-server#1184` until the SDK projection lands. Analysis, GP, ETL, scheduled, batch, export, and heavy refresh work routes through Honua's job runner.

The shared Razor shell currently exposes the first Console-native package editor set at `/studio/query`, `/studio/analysis`, `/studio/map`, `/studio/dashboard`, `/studio/report`, and `/studio/app`. These per-editor families (`honua-console#52`–`#56`, `#58`) still use the local `studio-package-mock/v1` lifecycle projection documented in [Studio Package Editor Routes](docs/studio/package-editor-routes.md) and surface a missing-binding state for validate/preview rather than mock success; they bind to the honua-server package lifecycle on their own tickets. `/studio/form` (`honua-console#57`) is the exception: it is its own server-bound form builder wired to the honua-server form package lifecycle (`honua-server#1184`) through `Honua.Console.Contracts`, and renders a missing-binding state — never mock form data — when no server base address is configured. The shared `/studio` shell already binds the package lifecycle (`honua-server#1180`/`#1181`).

Console should consume server/SDK projections for validate, preview, publish, and run responses. Do not duplicate server or SDK DTOs in this repo when a shared contract exists.

The unified GP/ETL workflow editor lives at `/studio/workflows/new` and
`/studio/workflows/{draftId}`. It edits a `workflow.package/v1` draft graph
with source, transform, sink, success, and failure edges; parameters;
schedule; worker profile; retry behavior; output schemas; and publication
intent. The merged runtime binds the `IStudioWorkflowPackageClient` adapter to
honua-server (`honua-server#1185`) through the `IWorkflowPackageApiClient` HTTP
shim in `Honua.Console.Contracts` (`ServerStudioWorkflowPackageClient`): the
node palette, package drafts, immutable versions, dry-runs, and
publications/runs render from live `/api/v1/console/workflow-*` data
(`honua-console#62`). When no server base address is configured the editor
renders the shared missing-binding surface instead of seeded workflow data;
the in-memory seeded client (`AddHonuaConsoleDemoStudioWorkflowPackages`) is
demo/test-only.

Save persists the draft and creates an immutable server version with the
server-owned validation result. Dry-run is a synchronous server estimation
(logs, artifacts, output schemas, validation) and creates no Operate job.
Publish maps the publication intent to the server target (job, schedule, or
eligible process endpoint; the explicit endpoint toggle also requests a process
endpoint), publishes the version, and starts the first run. When the run returns
a `jobId`, the publish links to live Operate job/event evidence; workflow-run
ids are not routed as job ids. Server validation or publication-eligibility
failures surface as `status=blocked` with the server rules rather than a
fabricated success or a standing mock.
