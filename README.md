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

## Current Status

This repo is the target home for porting current `honua-portal` logic and converging the long-term web surface. The first implementation issue is [honua-console#2](https://github.com/honua-io/honua-console/issues/2), which scaffolds the Blazor Web Console shell and shared Razor component library.

Until parity is accepted, source behavior remains in:

- `honua-portal` for current Portal, Catalog, Share, and Studio proof work.
- `honua-server-admin` for current legacy Admin and operator workflows.
- `honua-sdk-dotnet` for Console .NET clients, native gRPC paths, and shared contract packages.
- `honua-sdk-js` for browser-safe SDK contracts and generated app runtime.
- `honua-server` for server-owned metadata, content, RBAC, provenance, and package APIs.
- `honua-devops` for the single deployable artifact and release pipeline.

## Local Development

Install dependencies with `npm install`, then run `npm run dev`. Vite binds to `127.0.0.1:5173` by default.

The default auth driver is `fixture`, so protected routes redirect to `/auth/signin` and let local users choose a builder, operator, or admin fixture session. The fixture flow stores session state in `sessionStorage` only.

### Scripts

- `npm run typecheck` runs TypeScript with `--noEmit`.
- `npm run lint` runs Biome checks for `src` and `tests`.
- `npm run test` runs Vitest.
- `npm run build` runs typecheck and produces the Vite production build.
- `npm run smoke` runs the Playwright shell smoke suite once wired by follow-on work.

### Environment

All client environment reads live in `src/env.ts` and use Vite `VITE_*` variables:

- `VITE_API_BASE_URL`: Honua Server REST base URL. Empty means same-origin proxy.
- `VITE_ADMIN_BASE_URL`: transitional legacy Admin base URL for Operate link-back.
- `VITE_AUTH_DRIVER`: `fixture` or `whoami`; defaults to `fixture`.
- `VITE_WHOAMI_URL`: whoami endpoint when `VITE_AUTH_DRIVER=whoami`; defaults to `/api/portal/whoami`.
- `VITE_FEATURE_FLAGS`: comma-separated feature flag names.
- `VITE_FAKE_SESSION`: optional JSON authenticated fixture session seed for local tests.
