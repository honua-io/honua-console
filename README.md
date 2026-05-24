# Honua Console

Honua Console is the unified web surface for Honua.

It brings Studio, Catalog, Operate, and Share into one product surface and one deployment runtime:

- **Studio**: AI-assisted spatial query, analysis, map, dashboard, report, and app creation.
- **Catalog**: data, layers, services, saved maps, dashboards, reports, generated apps, metadata, and provenance.
- **Operate**: publishing, jobs, service configuration, identity, connectors, deployment health, observability, licensing, and runtime administration.
- **Share**: public links, embeds, open-data pages, exports, and external publishing flows.

## Decision Source

- [ADR-0001: Unified Honua Console Runtime](docs/adr/0001-unified-honua-console-runtime.md)
- [Honua Console Migration Backlog](docs/roadmap/HONUA_CONSOLE_MIGRATION_BACKLOG.md)
- [Honua Studio Information Model And Workflows](docs/architecture/studio-information-model-and-workflows.md)
- [GitOps Metadata Publishing Information Model](docs/architecture/gitops-metadata-publishing-information-model.md)
- [GitOps Metadata Publishing Visualization Design](docs/architecture/gitops-metadata-publishing-visualization-design.md)
- [Temporal Data Viewer Information Model](docs/architecture/temporal-data-viewer-information-model.md)
- [Operate Observability Information Model](docs/architecture/operate-observability-information-model.md)
- [Honua Console Design Handoff](docs/design-handoff/README.md)

## Migration Coordination

The Console migration spans the in-repo child-ticket backlog and external owner tickets. Cross-cutting decisions are made once and reused; do not re-decide them per ticket.

- [Console Patterns Charter](docs/migration/CONSOLE_PATTERNS_CHARTER.md) - binding patterns (routing, RBAC, error/empty/loading surfaces, perf budgets, smoke conventions, file layout) for every porting and integration ticket.
- [`honua-portal` Freeze And Retirement Policy](docs/migration/PORTAL_FREEZE_POLICY.md) - soft/hard freeze gates and retirement trigger for `honua-portal`.
- [SDK Shim Policy](docs/migration/SDK_SHIM_POLICY.md) - when and how temporary .NET and browser SDK shims are acceptable while `honua-sdk-dotnet#166` and `honua-sdk-js#225` land.

## Deployment

- [Build artifact contract](docs/deployment/BUILD_ARTIFACT.md) — what `honua-devops` consumes from `npm run build`.
- [Local and staging startup](docs/deployment/LOCAL_AND_STAGING.md) — how to run, preview, and promote.

## Quickstart

```
npm ci
npm run dev        # Vite dev server on 127.0.0.1:5174
npm run build      # Produces dist/ and dist/version.json
npm run preview    # Serves the built bundle on 127.0.0.1:4174
```

`/studio`, `/catalog`, `/operate`, and `/share` all resolve from the same origin via the SPA fallback. See [BUILD_ARTIFACT.md](docs/deployment/BUILD_ARTIFACT.md) for the same-origin proxy contract and the `version.json` schema used by release notes.

## Current Status

This repo is the target home for porting current `honua-portal` logic and converging the long-term web surface. The first implementation issue is [honua-console#2](https://github.com/honua-io/honua-console/issues/2), which scaffolds the Blazor Web Console shell and shared Razor component library.

Until parity is accepted, source behavior remains in:

- `honua-portal` for current Portal, Catalog, Share, and Studio proof work.
- `honua-server-admin` for current legacy Admin and operator workflows.
- `honua-sdk-dotnet` for Console .NET clients, native gRPC paths, and shared contract packages.
- `honua-sdk-js` for browser-safe SDK contracts and generated app runtime.
- `honua-server` for server-owned metadata, content, RBAC, provenance, and package APIs.
- `honua-devops` for the single deployable artifact and release pipeline.
