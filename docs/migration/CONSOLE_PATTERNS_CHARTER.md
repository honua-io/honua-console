# Honua Console Patterns Charter

Status: filed 2026-05-23.

Decision source: [ADR-0001: Unified Honua Console Runtime](../adr/0001-unified-honua-console-runtime.md).
Backlog source: [Honua Console Migration Backlog](../roadmap/HONUA_CONSOLE_MIGRATION_BACKLOG.md).

## Purpose

This charter is the binding pattern contract for the Honua Console migration.

Honua Console is a single product surface for Studio, Catalog, Operate, and Share. The migration ports `honua-portal` behavior into Console and folds legacy Admin into the same surface as a transitional Operate area. To keep that work coherent across nine in-repo child tickets and six external dependencies, the cross-cutting patterns are decided **once** here and in [`honua-console#2`](https://github.com/honua-io/honua-console/issues/2), then reused by every porting and integration ticket.

Per ADR-0001:

> Net-new and redesigned web surfaces should converge on one frontend shell.

This charter operationalizes that decision. Any deviation requires an ADR update, not a per-ticket choice.

## Scope

- Applies to: every child ticket under `honua-console-1` (`honua-console#2`-`#10`), and any future Console feature ticket that lands before `honua-portal` is retired.
- Does **not** apply to: legacy Admin internals (Blazor/MudBlazor), which remain frozen and only need the embed/redirect contract described in section "Operator vs. builder IA" below until they are rebuilt natively.

## Pattern Ownership

- [`honua-console#2`](https://github.com/honua-io/honua-console/issues/2) (scaffold) owns the **canonical implementations** of the patterns below: shell, router, query client, auth provider, error/empty/loading surfaces, smoke harness, performance budgets.
- [`honua-console#3`](https://github.com/honua-io/honua-console/issues/3) (IA, RBAC, navigation) owns the **route taxonomy** and the RBAC predicates the patterns enforce.
- All other child tickets (`#4`-`#10`) **consume** these patterns. They do not re-decide them and do not fork the shell.

If a porting ticket discovers a pattern gap that cannot be expressed inside the established surfaces, the resolution is to amend `#2` (or `#3` for IA/RBAC) and update this charter - not to introduce a parallel structure inside the porting ticket.

## Pattern Inventory

### 1. Stack and dependencies

- React 18 + TypeScript + Vite SPA. No SSR.
- `react-router-dom@6` for routing.
- `@tanstack/react-query` for server reads, with one `QueryClientProvider` at the root.
- `maplibre-gl` for map rendering; Vega-Lite for chart specs.
- `@honua/sdk-js` for service, content-item, package, RBAC, and sharing contracts.
- Lint/format via Biome. Unit/component tests via Vitest + Testing Library. Smoke via Playwright.

No new top-level frontend framework, runtime, or design-system dependency is introduced without an ADR update.

### 2. Route taxonomy and lazy loading

- Top-level route groups: `/studio`, `/catalog`, `/operate`, `/share`. Authentication routes (`/auth/*`), public routes (`/public/*`), and embed routes (`/embed/*`) sit outside those groups but inside the same Console origin.
- All non-landing routes are `React.lazy`-imported. Eager exceptions are limited to: workspace landing, sign-in, signed-out. New routes default to lazy.
- Each route group resolves its own data inside its own lazy boundary. There is no centralized "load everything before shell renders" data layer.
- The shell renders a `Suspense` fallback (`LoadingShell`) and an `ErrorBoundary` per route. Both are owned by the shell - porting tickets compose, they do not replace.

Reference pattern: `honua-portal/src/router.tsx` (lazy + `ProtectedRoute` + `AppShell` + `ErrorBoundary`).

### 3. Auth, session, and RBAC

- One login/session/RBAC model across `/studio`, `/catalog`, `/operate`, `/share` (ADR-0001).
- Session is read via a single hook exported from the auth module. Porting tickets do not read raw scopes inline; they call permission helpers (`canSeeOperatorLinks(session)`, etc.).
- Protected routes are wrapped with the shared `<ProtectedRoute>`. Unauthenticated users are redirected to `/auth/signin?returnTo=...`.
- Operator-only nav items are filtered at the nav definition layer, not inside per-route components. This preserves the IA separation in section "Operator vs. builder IA" below.
- Embed and open-data routes have explicit, narrower auth requirements; they share the same session contract but do not require a signed-in user.

Authoritative session and RBAC contracts come from `@honua/sdk-js` projections of `honua-server` (`honua-server#1162`). Console does not redefine session DTOs locally - see section "DRY: contracts only via SDK" below.

### 4. Operator vs. builder IA on one shell

- Builder/end-user workflow areas: `/studio`, `/catalog`, `/share`.
- Operator/control-plane workflow area: `/operate`.
- The shell, auth, navigation primitives, error surfaces, and deployment runtime are **shared**. The IA is **distinct**: operator entry points are not promoted on builder routes, and vice versa.
- Legacy Admin lives at `/operate/legacy/*` during the transition (see [`honua-console#6`](https://github.com/honua-io/honua-console/issues/6)). Old Admin URLs redirect into the same path. The transition strategy (in-Console embed vs. edge reverse-proxy) is owned by `#6`; either way, deep links from the legacy surface must continue to work.

### 5. Error, empty, forbidden, and loading surfaces

Use the shell-owned surfaces. Do not invent alternates.

- `EmptyState` for "no items here" (with optional primary/secondary actions, tone switch).
- `Forbidden` for permission-denied (wraps `EmptyState` with permission-aware copy).
- `LoadingShell` for session bootstrap and Suspense fallbacks.
- `ErrorBoundary` - one boundary per route, mounted by the shell.

Unsupported service metadata and unsupported package bindings (per project constraints) surface through the same primitives - never through a bespoke per-feature surface.

### 6. DRY: contracts only via SDK

Per ADR-0001:

> The non-negotiable unifier is shared contracts, not duplicated UI code.

- Metadata v2, content items, service->item provenance, saved map / map package, dashboard / report / app package, Vega-Lite chart spec embeds, sharing / embed / authorization, audit / lineage - all enter Console as types projected from `@honua/sdk-js`.
- Console does not re-declare server DTOs locally. It does not re-export SDK types under Console-flavored names.
- If a needed projection is not yet in `@honua/sdk-js`, see [`SDK_SHIM_POLICY.md`](./SDK_SHIM_POLICY.md). Shims live behind a single boundary file and are removed in [`honua-console#7`](https://github.com/honua-io/honua-console/issues/7).

### 7. Performance budgets and network waterfalls

Per project constraints, Console startup, catalog list, map viewer, and generated-app preview must not introduce avoidable network waterfalls relative to current `honua-portal` behavior.

- Session probe is the only blocking network call before the shell renders.
- Route-specific data fetches are owned by the route, not centralized.
- React Query defaults: `staleTime: 30_000`, `retry: 1`, `refetchOnWindowFocus: false` (matches the portal). Per-query overrides must be justified in the calling code.
- Vite `manualChunks` keeps `@tanstack/react-query` and `react-router-dom` out of per-route bundles. Heavy deps (MapLibre, Vega-Lite) are added to the chunk policy when they land.
- Lazy-route boundaries are not removed for ergonomic reasons. If a porting ticket needs to share state across routes, that shared state lives in a hook or query - not in eager imports across boundaries.

### 8. Telemetry and smoke evidence

Per project constraints:

> Add or preserve smoke evidence for publish, catalog, viewer, Studio generation, share, and embed flows when behavior changes.

- Each porting ticket that changes a flow (publish, catalog, viewer, Studio generation, share, embed, open-data) carries forward or adds Playwright smoke that produces the same evidence the portal currently produces.
- Cross-surface smoke (publish service -> catalog item -> Studio artifact -> share/embed) is the gate at [`honua-console#9`](https://github.com/honua-io/honua-console/issues/9). Per-port smoke is preferred over deferring to `#9`, because regressions caught at `#9` mean rework across multiple already-merged ports.
- Smoke runbooks live under `docs/runbook/` and are referenced from PR evidence.

The [`honua-portal/smoke/`](https://github.com/honua-io/honua-portal/tree/main/smoke) inventory (catalog, catalog-browser, saved-maps, sharing-and-embed, open-data, publish-handoff, annotations-comments, maputnik-style-editor) is the reference set Console must preserve or replace one-for-one.

### 9. Design system

Decision deferred to [`honua-console#2`](https://github.com/honua-io/honua-console/issues/2): extend `honua-portal/src/ui` patterns (faster, inherits portal styling) vs. introduce a fresh token/component layer the Operate redesign can also adopt (slower, avoids a second migration).

Until `#2` decides:

- Porting tickets reuse the portal's CSS variable + per-component CSS pattern when porting a portal route.
- No new design-system dependency is introduced without `#2`'s decision.
- If `#2` decides "new token layer," that work is filed as a bounded sibling ticket (`honua-console#11`) ahead of `#4`/`#5` porting.

### 10. File layout (binding)

The scaffold ticket establishes this layout; porting tickets extend it without reorganizing:

```
src/
  api/          # Bearer-injected fetch wrapper, typed contract calls (consumes @honua/sdk-js).
  auth/         # SessionProvider, drivers, permission helpers, ProtectedRoute.
  contracts/    # SDK shim boundary file (see SDK_SHIM_POLICY.md). Removed in #7.
  hooks/        # Cross-cutting hooks.
  routes/       # One file per top-level route, grouped under /studio, /catalog, /operate, /share.
                # Co-locate route-specific queries.ts + components.tsx as features grow.
  shell/        # AppShell, NavConfig, UserMenu, EmptyState, Forbidden, LoadingShell, ErrorBoundary.
  styles/       # global.css and shared design tokens (pending #2's design-system decision).
tests/
  setup.ts      # Vitest setup.
  fixtures.ts   # Reusable fixtures.
  unit/         # Vitest tests that don't fit next to a single source file.
  smoke/        # Playwright specs (publish, catalog, viewer, Studio, share, embed, open-data).
docs/
  adr/          # Architecture decisions.
  migration/    # This charter, freeze policy, SDK shim policy.
  roadmap/      # Migration backlog and per-area roadmaps.
  runbook/      # Per-ticket smoke runbooks attached to PR evidence.
```

## Out of Scope For This Charter

- Concrete scaffold code (owned by `#2`).
- Route map and RBAC predicates (owned by `#3`).
- Per-area port plans (owned by `#4`/`#5`/`#6`).
- Single deployable artifact mechanics (owned by `#8` + `honua-devops#55`/`#56`).
- Cross-surface smoke wiring (owned by `#9`).
- `honua-portal` retirement mechanics (owned by `#10`; see [`PORTAL_FREEZE_POLICY.md`](./PORTAL_FREEZE_POLICY.md)).
