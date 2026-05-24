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

This repo is the target home for porting current `honua-portal` logic and converging the long-term web surface. The long-term runtime remains the Blazor Web / shared Razor architecture described in ADR-0001; the current React/TypeScript/Vite scaffold is the transitional shell used to wire already-open Console port branches against shared SDK contracts.

[honua-console#7](https://github.com/honua-io/honua-console/issues/7) wires this shell to shared SDK contracts:

- `src/sdk/` is the only place allowed to import from `@honua/sdk-js` (enforced by `eslint.config.js`).
- `src/session/` carries the `SessionClient` facade, `SessionProvider`, `useCapability`, `useEntitlement`, and `<RequireCapability />` guard. Capability/entitlement gates derive from the server bundle and configured Honua server origin; there is no Console-local role matrix.
- `src/shell/ControlPlaneProvider.tsx` constructs the SDK `HonuaClient` with a `fetchFn` that defaults `credentials: "include"` so cookie-backed auth flows to the configured Honua server origin without consumers re-wiring fetch.
- `src/surfaces/LoadSurface.ts` and `src/surfaces/ResourceState.tsx` are the shared loader contract and empty/error surface used by Catalog, Studio, Operate, and Share. Hooks reset to `pending-binding` (and re-emit smoke) when their SDK inputs disappear, so navigation away from a wired binding does not leave a stale `ok` surface visible.
- `src/telemetry/smoke.ts` emits one `{ surface, sdkSubpath, status, durationMs }` event per loader resolution; Portal-style listeners on `window` keep dashboard parity. `emitPendingBindingSmoke` is the shared helper for the pending-binding case and stamps `detail.waitingFor` with the upstream contract list.
- `/studio/preview` is code-split via `React.lazy` + `Suspense` so the Studio chunk does not block Catalog/Operate/Share startup paths.

Gaps from upstream contracts (still in flight) are tracked in [docs/server-sdk-gap-log.md](docs/server-sdk-gap-log.md). Console surfaces those gaps as `<ResourceState kind="pending-binding" />` instead of inventing local DTOs.

## Response Contract

Every SDK-backed loader returns `LoadSurface<T>`:

- `ok` carries the loaded SDK value.
- `missing` represents 404 or deleted/not-found items.
- `unauthorized` represents 401/403 or a failed capability/entitlement gate.
- `unsupported` represents unsupported service metadata, package bindings, or unexpected SDK errors; `reason` and optional `code` are preserved for UI and smoke telemetry.
- `pending-binding` lists upstream contracts that are not published yet, such as `honua-sdk-js#225` or `honua-server#1162`.

`HonuaControlPlaneResult<T>` adapts into this union through `src/surfaces/adapt.ts`: supported results become `ok`, 404 becomes `missing`, 401/403 thrown SDK errors become `unauthorized`, and unsupported/unknown failures become `unsupported`.

## Current Wiring Matrix

| Area | Route or hook | Gate | SDK surface | Current behavior |
| --- | --- | --- | --- | --- |
| Catalog | `/catalog/items`, `useContentItemList` | `catalog:read` | `src/sdk/content.ts` | `pending-binding` until metadata v2 content projections publish in `honua-sdk-js#225`. |
| Catalog / Viewer | `/catalog/packages`, `usePackageList`, `usePackageDetail` | `map-packages:read` | `@honua/sdk-js/control-plane` via `src/sdk/control-plane.ts` | Lists and reads map packages through `HonuaMapPackagesClient`. |
| Studio | `/studio/preview`, `useGeneratedAppPreview` | `studio:preview` | `@honua/sdk-js/generated-app` | Renders `pending-binding` until the Studio app-builder passes manifest input and load options, then returns the generated-app preview result. |
| Operate | `/operate/provenance`, `useProvenance` | `operate:provenance:read` | `@honua/sdk-js/operator/workspace` | Uses the SDK `ProvenanceRecord` type; page stays `pending-binding` until the server provenance loader is supplied. |
| Share | `/share`, `useShareMutate` | `sharing:read` | `@honua/sdk-js/control-plane` | Sharing mutation is wired through `HonuaSharingClient`; list/embed views wait on saved-map projections. |
| Collaboration | `useCollaborationSession` | caller-owned | `@honua/sdk-js/collaboration` | Hook joins a saved-map collaboration session when options and join request are provided; no route is mounted yet. |

## Local Development

```bash
npm install
npm run dev         # vite dev server on http://127.0.0.1:5173
npm run typecheck   # tsc --noEmit
npm run lint        # eslint (includes SDK barrel + DTO guards)
npm test            # vitest
npm run build       # tsc + vite build
```

`VITE_HONUA_BASE_URL` selects the Honua server origin for both SDK/control-plane calls and session bootstrap; if unset, the current page origin is used.

Until parity is accepted, source behavior remains in:

- `honua-portal` for current Portal, Catalog, Share, and Studio proof work.
- `honua-server-admin` for current legacy Admin and operator workflows.
- `honua-sdk-dotnet` for Console .NET clients, native gRPC paths, and shared contract packages.
- `honua-sdk-js` for browser-safe SDK contracts and generated app runtime.
- `honua-server` for server-owned metadata, content, RBAC, provenance, and package APIs.
- `honua-devops` for the single deployable artifact and release pipeline.
