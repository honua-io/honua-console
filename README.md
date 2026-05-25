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
- [Studio Package Editor Routes](docs/studio/package-editor-routes.md) — Console-native editor routes, package-family coverage, and the temporary lifecycle mock contract for `honua-console#39`.
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
`smoke:workflow` command covers the Studio workflow-package path added
for `honua-console#40`.

## Current Status

This repo is the target home for porting current `honua-portal` logic and converging the long-term web surface. The Console IA is fixed in [docs/console-route-map.md](docs/console-route-map.md) ([honua-console#3](https://github.com/honua-io/honua-console/issues/3)); the Blazor Web Console shell and shared Razor component library scaffold lands under [honua-console#2](https://github.com/honua-io/honua-console/issues/2). The scaffold now also includes an independently deployable Blazor web host and an optional .NET MAUI Blazor Hybrid native host ([honua-console#26](https://github.com/honua-io/honua-console/issues/26)) for operator/power-user workflows.

Native Operate transition routes for connections, resources, services, layers, and settings are documented in [Native Operate Transition Surface](docs/operate/native-transition-surface.md). They use bounded Console view models until `honua-sdk-dotnet` admin projections replace the in-memory transition data source.

## Project Layout

- `src/Honua.Console.Shell`: shared Razor routes, layout, route map, environment profile models, account session interfaces, native Operate transition surfaces, catalog/share route-slice surfaces, and native streaming proof interface.
- `src/Honua.Console.Contracts`: temporary SDK shim boundary for Console-side contracts until the shared .NET SDK projections replace them.
- `src/Honua.Console.Web`: default browser Console host. It references the shared shell and stays independently buildable/deployable without MAUI or native services.
- `src/Honua.Console.Native.Core`: testable native host services for persisted environment profiles, account-token sessions, certificate references, HTTP/gRPC connection creation, and the deterministic telemetry streaming proof.
- `src/Honua.Console.Native`: optional MAUI Blazor Hybrid host for desktop operator workflows. It renders the shared shell in a `BlazorWebView` and backs profile/session storage with MAUI secure storage.
- `tests/Honua.Console.Native.Core.Tests`: host-independent coverage for route boundaries, profile persistence, native connection setup, and the streaming proof contract.

## Local Usage

Run the browser Console:

```bash
dotnet run --project src/Honua.Console.Web/Honua.Console.Web.csproj
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

The native Blazor Operate observability checkpoint is available at
`/operate` and `/operate/observability`, with deep links for
`/operate/events/{eventId}`, `/operate/alerts/{alertId}`, and
`/operate/jobs/{jobRunId}`.

The current checkpoint is fixture-backed by `OperateObservabilityFixture`
while the server and SDK Operate contracts land. It documents the
response behavior Console must preserve: `unknown`, `unsupported`,
`missing`, `disabled`, `not configured`, and `unconfigured` telemetry are
neutral states; AI advisory text appears beside raw evidence links;
invalid realtime/geofence rules cannot be enabled; and Studio,
publishing, GitOps, temporal, alert delivery, import, and maintenance
jobs share the `/operate/jobs/{jobRunId}` detail surface.

## Studio Contract Notes

Studio authoring is modeled as shared package contracts, not separate Console-only schemas. The canonical model covers workspaces, content items, content versions, Studio projects, conversations/provenance, packages, data bindings, publications, and job runs.

The current `/studio` route includes the first shared Razor package shell slice for `honua-console#38`; the same in-memory shell is also mounted for `/studio/proof`, `/studio/drafts?source=<kind>&id=<itemId>`, and `/studio/apps/:itemId/preview` route compatibility. It lets a builder choose a workflow, submit a prompt, answer structured clarification questions, inspect the active package, and move that package through Draft, Preview, Saved version, and Published UI states. Route parameters are accepted for the source-scoped and generated-app paths, but server-backed source hydration, content-version persistence, and publish records remain follow-up work. The shell records the temporary Console-owned `studio-authoring-shell/v1` projection with `package-shell/v1` package snapshots until the server package lifecycle API and SDK helpers are wired in.

Package families include query, analysis, map, dashboard, report, form, app, workflow, and publication packages. Publishing always creates or updates server-owned content item versions and publication records; analysis, GP, ETL, scheduled, batch, export, and heavy refresh work routes through Honua's job runner.

The shared Razor shell currently exposes the first Console-native package editor set at `/studio/query`, `/studio/analysis`, `/studio/map`, `/studio/dashboard`, `/studio/report`, `/studio/form`, and `/studio/app`. These routes use the `studio-package-mock/v1` lifecycle projection documented in [Studio Package Editor Routes](docs/studio/package-editor-routes.md) until honua-server and honua-sdk-dotnet expose the content-version, publication, share, embed, and rollback APIs.

Console should consume server/SDK projections for validate, preview, publish, and run responses. Do not duplicate server or SDK DTOs in this repo when a shared contract exists.

The first unified GP/ETL workflow editor lives at
`/studio/workflows/new` and `/studio/workflows/{draftId}`. It edits a
`workflow.package/v1` draft graph with source, transform, sink, success,
and failure edges; parameters; schedule; worker profile; retry behavior;
output schemas; and publication intent. Until the server and
`honua-sdk-dotnet` workflow projections are available, Console uses the
replaceable `IStudioWorkflowPackageClient` adapter in
`src/Honua.Console.Shell/Services`.

The adapter response contract is intentionally shaped like the future
server boundary: dry-run returns `jobId`, `jobKind`, `status`, sample row
count, logs, artifacts, output schemas, and Operate job/event URLs; save
returns a versioned `workflow` content item. Queued publish responses
return a publication id, content item/version ids, job id, publication
mode, optional invocation endpoint, parameter validation, and Operate
evidence links. Publish selects the current saved version when unchanged
and saves unsaved package edits as a new version before queuing
publication. Invalid process-endpoint parameter contracts return
`status=blocked` with parameter validation and no job, Operate evidence
URLs, or invocation endpoint.
