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

## Scaffold Contract

The current shell is intentionally minimal. It establishes the runtime, route boundaries, session surface, and shared client seams that follow-on migration tickets will fill with product behavior.

### Route And Access Matrix

| Route | Area | Current behavior | Access |
| --- | --- | --- | --- |
| `/` | Console home | Authenticated placeholder shell overview. | Authenticated session. |
| `/studio` | Studio | Placeholder for the Studio app-builder and generated-app lifecycle port. | Authenticated session; side-nav item is visible to `member`, `operator`, or `admin`. |
| `/catalog` | Catalog | Placeholder for Portal catalog, viewer, saved maps, share, embed, and open-data porting. | Authenticated session; side-nav item is visible to `member`, `operator`, or `admin`. |
| `/operate` | Operate | Placeholder for operator workflows and transitional legacy Admin integration. | Authenticated `operator` or `admin` scope. |
| `/share` | Share | Placeholder for public links, embeds, open-data pages, exports, and sharing contracts. | Authenticated session; no additional scope in the scaffold. |
| `/auth/signin` | Auth | Fixture preset chooser locally, or server sign-in launcher when `VITE_AUTH_DRIVER=whoami`. | Public. |
| `/auth/signed-out` | Auth | Signed-out confirmation after local fixture sign-out. | Public. |
| `/auth/callback` | Auth | Redirects to `/`; server-owned auth callback wiring lands later. | Public. |
| `*` | Fallback | Protected "View not found" empty state. | Authenticated session. |

Protected routes redirect unauthenticated users to `/auth/signin?returnTo=...`. `returnTo` is sanitized to same-origin absolute paths and never returns to auth-loop routes.

### Session And RBAC Contract

The scaffold exposes a temporary local `Session` shape in `src/auth/types.ts` until honua-console#7 switches it to the shared SDK/server contract:

- `loading`, `unauthenticated`, `authenticated`, and `error` session states.
- Authenticated sessions include `user`, `workspace`, `scopes`, and optional `accessToken`.
- Builder navigation uses `member`, `operator`, or `admin` scopes.
- Operate navigation and the `/operate` route use the single `canSeeOperatorLinks` rule: `operator` or `admin`.

The default local `fixture` driver stores only local session state in `sessionStorage` and offers builder, operator, and admin presets. Production builds default to `whoami` when `VITE_AUTH_DRIVER` is unset; production fixture auth requires `VITE_AUTH_DRIVER=fixture` and `VITE_ALLOW_FIXTURE_AUTH=true` so preview/release cannot silently boot with public fixture sessions. The `whoami` driver calls `VITE_WHOAMI_URL` with credentials, treats `401`/`403` as unauthenticated, surfaces `501` as "Session endpoint not yet available", redirects sign-in/sign-out through server-owned auth endpoints, and expects an authenticated JSON payload with `user`, `workspace`, `scopes`, and optional `accessToken`.

### API Response Contract

All Console REST calls should go through `consoleFetch` in `src/api/client.ts` until a more specific SDK helper exists.

- Relative paths resolve against `VITE_API_BASE_URL`; an empty base URL keeps same-origin requests.
- Requests send `Accept: application/json`, JSON bodies receive `Content-Type: application/json`, and authenticated sessions with `accessToken` receive a bearer `Authorization` header.
- `204` responses resolve as `undefined`; other successful responses are parsed as JSON.
- Failed responses throw `ConsoleApiError` with `status`, `url`, `message`, and optional `envelope`.
- Error envelopes follow `{ "error": { "code": "...", "message": "...", "details": { ... } } }`.

Console must not duplicate server or SDK protocol DTOs. Service, metadata, content, map, package, sharing, embed, and RBAC contracts should be imported from stable `@honua/sdk-js` subpaths once those contracts are available.

## Local Development

Install dependencies with `npm install`, then run `npm run dev`. Vite binds to `127.0.0.1:5173` by default.

In local dev and tests, the default auth driver is `fixture`, so protected routes redirect to `/auth/signin` and let local users choose a builder, operator, or admin fixture session. The fixture flow stores session state in `sessionStorage` only.

### Scripts

- `npm run typecheck` runs TypeScript with `--noEmit`.
- `npm run lint` runs Biome checks for `src` and `tests`.
- `npm run test` runs Vitest.
- `npm run build` runs typecheck and produces the Vite production build.
- `npm run smoke` runs the Playwright shell smoke suite for fixture sign-in, navigation, and Operate gating.

### Environment

All client environment reads live in `src/env.ts` and use Vite `VITE_*` variables:

- `VITE_API_BASE_URL`: Honua Server REST base URL. Empty means same-origin proxy.
- `VITE_ADMIN_BASE_URL`: transitional legacy Admin base URL for Operate link-back.
- `VITE_AUTH_DRIVER`: `fixture` or `whoami`; defaults to `fixture` in dev/test and `whoami` in production builds.
- `VITE_ALLOW_FIXTURE_AUTH`: set to `true` only for intentional production-build fixture runs, such as the local Playwright smoke harness.
- `VITE_WHOAMI_URL`: whoami endpoint when `VITE_AUTH_DRIVER=whoami`; defaults to `/api/portal/whoami`.
- `VITE_AUTH_SIGN_IN_URL`: server-owned sign-in endpoint when `VITE_AUTH_DRIVER=whoami`; defaults to `/api/auth/signin`.
- `VITE_AUTH_SIGN_OUT_URL`: server-owned sign-out endpoint when `VITE_AUTH_DRIVER=whoami`; defaults to `/api/auth/signout`.
- `VITE_FEATURE_FLAGS`: comma-separated feature flag names.
- `VITE_FAKE_SESSION`: optional JSON authenticated fixture session seed for local tests.
