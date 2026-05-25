# Honua Console Patterns Charter

Status: filed 2026-05-23, reconciled with ADR-0001 .NET-first amendment; amended 2026-05-24 with the real-server integration policy (section 11).

Decision source: [ADR-0001: Unified Honua Console Runtime](../adr/0001-unified-honua-console-runtime.md).
Backlog source: [Honua Console Migration Backlog](../roadmap/HONUA_CONSOLE_MIGRATION_BACKLOG.md).
Design source: [Honua Console Design Handoff](../design-handoff/README.md).

## Purpose

This charter is the binding pattern contract for the Honua Console migration.

Honua Console is a single product surface for Studio, Catalog, Operate, and Share. The migration ports `honua-portal` behavior into Console and folds legacy Admin into the same surface as a transitional Operate area. To keep that work coherent across nine in-repo child tickets and external dependencies, the cross-cutting patterns are decided **once** here and in [`honua-console#2`](https://github.com/honua-io/honua-console/issues/2), then reused by every porting and integration ticket.

Per ADR-0001:

> Net-new and redesigned Console surfaces should converge on a .NET-first UI architecture: Blazor Web App is the default web Console shell.

> The non-negotiable unifier is shared .NET-owned contracts, not duplicated UI code.

This charter operationalizes those decisions. Any deviation requires an ADR update, not a per-ticket choice.

## Scope

- Applies to: every child ticket under `honua-console-1` (`honua-console#2`-`#10`, plus the Studio, GitOps, observability, temporal, and native-host tickets tracked in the backlog), and any future Console feature ticket that lands before `honua-portal` is retired.
- Does **not** apply to: legacy Admin internals (existing Blazor WebAssembly/MudBlazor surface in `honua-server-admin`), which remain frozen and only need the embed/redirect contract described in section "Operator vs. builder IA" below until they are rebuilt natively.
- Does **not** apply to: the React/TypeScript/Vite code already living inside `honua-portal`. That code is the porting source; it is not the long-term Console runtime. New Console code follows the .NET-first patterns in this charter.

## Pattern Ownership

- [`honua-console#2`](https://github.com/honua-io/honua-console/issues/2) (scaffold) owns the **canonical implementations** of the patterns below: the Blazor Web Console shell, shared Razor component library, router, HTTP client wiring against `honua-sdk-dotnet`, authentication state provider, error/empty/loading/forbidden surfaces, smoke harness, performance budgets.
- [`honua-console#3`](https://github.com/honua-io/honua-console/issues/3) (IA, RBAC, navigation) owns the **route taxonomy** and the RBAC predicates the patterns enforce.
- All other child tickets **consume** these patterns. They do not re-decide them and do not fork the shell.

If a porting ticket discovers a pattern gap that cannot be expressed inside the established surfaces, the resolution is to amend `#2` (or `#3` for IA/RBAC) and update this charter - not to introduce a parallel structure inside the porting ticket.

## Pattern Inventory

### 1. Stack and dependencies

- **Blazor Web App** (current supported .NET, with interactive Server + WebAssembly render modes selected per route) as the default Console shell.
- **Shared Razor component library** (`Honua.Console.Components`) consumed by the Blazor Web host and, later, by the optional MAUI Blazor Hybrid host described in [`honua-console#26`](https://github.com/honua-io/honua-console/issues/26).
- **`honua-sdk-dotnet`** as the authoritative client for metadata, content, packages, RBAC, jobs, telemetry, GitOps, temporal, sync, and publishing APIs ([`honua-sdk-dotnet#166`](https://github.com/honua-io/honua-sdk-dotnet/issues/166)).
- **`honua-sdk-js`** is consumed only inside contained JS interop bundles for generated apps, browser embeds, MCP/QGIS bridges, and the open-data/embed runtime ([`honua-sdk-js#225`](https://github.com/honua-io/honua-sdk-js/issues/225), [`#226`](https://github.com/honua-io/honua-sdk-js/issues/226)). It is not the Console-side contract source.
- **MapLibre GL JS** for map rendering, **Vega-Lite** for chart specs, and a Monaco-based editor for code surfaces, all via narrow JS interop modules under `wwwroot/interop/`. No additional top-level JS framework.
- **Lint/format** via `dotnet format`. **Unit/component tests** via xUnit + bUnit. **Integration tests** via xUnit + Testcontainers against a real honua-server (see section 11). **Smoke** via Playwright for .NET.
- React/TypeScript/Vite is permitted only while reading from `honua-portal` to port behavior; no React/Vite code lands in the long-term Console runtime without an ADR amendment.

No new top-level framework, runtime, or design-system dependency is introduced without an ADR update.

### 2. Route taxonomy and lazy loading

- Top-level route groups: `/studio`, `/catalog`, `/operate`, `/share`. Authentication routes (`/auth/*`), public routes (`/public/*`), and embed routes (`/embed/*`) sit outside those groups but inside the same Console origin.
- Routes are declared with `@page` directives in feature components inside the shared Razor library. The shell hosts a single `Router` with shared `Found`/`NotFound` templates and a `CascadingAuthenticationState`.
- Heavy WebAssembly-rendered features (Studio editor, map viewer, dashboard composer, generated-app preview) are split into **lazy-loaded assemblies** via Blazor `LazyAssemblyLoader`; the shell-rendered routes (workspace landing, sign-in, signed-out, navigation chrome) load eagerly.
- Each route group resolves its own data inside its own route component. There is no centralized "load everything before shell renders" data layer.
- The shell renders a `LoadingShell` component for Suspense-equivalent transitions (initial assembly load, auth probe) and wraps each route group with a shared `ConsoleErrorBoundary`. Both are owned by the shell - porting tickets compose, they do not replace.

Reference for behavior (not implementation): `honua-portal/src/router.tsx` (lazy + protected routes + AppShell + ErrorBoundary). The Blazor equivalents are the canonical implementation.

### 3. Auth, session, and RBAC

- One login/session/RBAC model across `/studio`, `/catalog`, `/operate`, `/share` (ADR-0001).
- Session state is exposed via a single `AuthenticationStateProvider` registered by the shell; components read it through `CascadingAuthenticationState` and the shared `HonuaSession` cascading value. Porting tickets do not read raw claims inline; they call permission helpers (`Permissions.CanSeeOperatorLinks(session)`, etc.) defined alongside the auth provider.
- Protected routes use shared `<AuthorizeView>` markup or the `[Authorize]` attribute against named policies. Unauthenticated users are redirected to `/auth/signin?returnTo=...` by the shell.
- Operator-only nav items are filtered at the nav definition layer, not inside per-route components. This preserves the IA separation in section "Operator vs. builder IA" below.
- Embed and open-data routes have explicit, narrower auth requirements; they share the same session contract but do not require a signed-in user.

Authoritative session and RBAC contracts come from `honua-sdk-dotnet` projections of `honua-server` ([`honua-server#1162`](https://github.com/honua-io/honua-server/issues/1162)). Console does not redefine session DTOs locally - see section "DRY: contracts only via SDK" below.

### 4. Operator vs. builder IA on one shell

- Builder/end-user workflow areas: `/studio`, `/catalog`, `/share`.
- Operator/control-plane workflow area: `/operate`.
- The shell, auth, navigation primitives, error surfaces, and deployment runtime are **shared**. The IA is **distinct**: operator entry points are not promoted on builder routes, and vice versa.
- Legacy Admin lives at `/operate/legacy/*` during the transition (see [`honua-console#6`](https://github.com/honua-io/honua-console/issues/6)). Old Admin URLs redirect into the same path. The transition strategy (in-Console host iframe vs. edge reverse-proxy) is owned by `#6`; either way, deep links from the legacy surface must continue to work and Console session/RBAC must be the system of record.

### 5. Error, empty, forbidden, and loading surfaces

Use the shell-owned Razor components in `Honua.Console.Components`. Do not invent alternates.

- `<EmptyState>` for "no items here" (with optional primary/secondary actions, tone switch).
- `<Forbidden>` for permission-denied (wraps `EmptyState` with permission-aware copy).
- `<LoadingShell>` for session bootstrap and lazy-assembly transitions.
- `<ConsoleErrorBoundary>` - one boundary per route group, mounted by the shell. Exceptions bubble to the boundary; routes do not swallow them.

Unsupported service metadata and unsupported package bindings (per project constraints) surface through the same primitives - never through a bespoke per-feature surface.

### 6. DRY: contracts only via SDK

Per ADR-0001:

> The non-negotiable unifier is shared .NET-owned contracts, not duplicated UI code.

- Metadata v2, content items, service->item provenance, saved map / map package, dashboard / report / app package, Vega-Lite chart spec embeds, sharing / embed / authorization, audit / lineage, jobs / telemetry / alerting / realtime, temporal data history, disconnected sync, GitOps, and AI-generated artifact contracts - all enter Console as types projected from `honua-sdk-dotnet`.
- Console does not re-declare server DTOs locally. It does not re-export SDK types under Console-flavored names.
- Generated apps, browser embeds, and the open-data runtime continue to consume `honua-sdk-js` from inside JS interop bundles. Those bundles must not be the source of truth for Console-side server payloads; the Razor side reads `honua-sdk-dotnet`.
- If a needed projection is not yet in `honua-sdk-dotnet` (or, for embed/generated-app paths, `honua-sdk-js`), see [`SDK_SHIM_POLICY.md`](./SDK_SHIM_POLICY.md). Shims live behind a single boundary file and are removed in [`honua-console#7`](https://github.com/honua-io/honua-console/issues/7).

### 7. Performance budgets and network waterfalls

Per project constraints, Console startup, catalog list, map viewer, and generated-app preview must not introduce avoidable network waterfalls relative to current `honua-portal` behavior.

- Session probe is the only blocking network call before the shell renders. Interactive Server routes can avoid an explicit probe because the auth state is already on the circuit; WebAssembly routes share the same probe through the shell.
- Route-specific data fetches are owned by the route, not centralized. Prefer `IAsyncEnumerable<T>` streaming or `HttpClient.GetFromJsonAsync` with `CancellationToken` plumbed from the component.
- Default HTTP client policy (registered once in the shell): retry-once on transient transport errors, no retry on HTTP 4xx, `Cache-Control` respected. Per-call overrides must be justified in the calling code.
- Lazy-loaded assemblies cover the Studio editor, map viewer, dashboard composer, generated-app preview, and Operate observability workspace. The eager bundle stays bounded to shell + auth + nav + landing.
- Trim/AOT-friendly: avoid reflection-heavy patterns in shared library code so WebAssembly publish budgets stay predictable.
- Lazy-route boundaries are not removed for ergonomic reasons. If a porting ticket needs to share state across routes, that shared state lives in a scoped service or a cascading value - not in eager imports across boundaries.

### 8. Telemetry and smoke evidence

Per project constraints:

> Add or preserve smoke evidence for publish, catalog, viewer, Studio generation, share, and embed flows when behavior changes.

- Each porting ticket that changes a flow (publish, catalog, viewer, Studio generation, share, embed, open-data) carries forward or adds Playwright-for-.NET smoke that produces the same evidence the portal currently produces.
- Cross-surface smoke (publish service -> catalog item -> Studio artifact -> share/embed, plus open-data publication and unauthenticated embed rendering) is the gate at [`honua-console#9`](https://github.com/honua-io/honua-console/issues/9). Per-port smoke is preferred over deferring to `#9`, because regressions caught at `#9` mean rework across multiple already-merged ports. The `#9` gate runs against a real honua-server started via Testcontainers/compose (`sourceHydrated: true`); a smoke that passes only against in-memory fixtures does not satisfy it (see section 11).
- Console emits OpenTelemetry traces and metrics from the .NET host. Telemetry attributes follow the conventions shared with `honua-server`; Console does not invent a parallel telemetry vocabulary.
- Smoke runbooks live under `docs/runbook/` and are referenced from PR evidence.

The [`honua-portal/smoke/`](https://github.com/honua-io/honua-portal/tree/main/smoke) inventory (catalog, catalog-browser, saved-maps, sharing-and-embed, open-data, publish-handoff, annotations-comments, maputnik-style-editor) is the reference set Console must preserve or replace one-for-one.

### 9. Design system

Decision deferred to [`honua-console#2`](https://github.com/honua-io/honua-console/issues/2): build a fresh Razor token/component layer in `Honua.Console.Components` from scratch (slower, but matches the .NET-first direction and avoids re-creating MudBlazor or React patterns) vs. reuse an existing Razor component library (e.g., FluentUI Blazor, Radzen) to accelerate scaffolding.

Until `#2` decides:

- Porting tickets reuse the portal's CSS variable + per-component CSS pattern as a **visual reference** when porting a portal route, re-expressed as CSS-isolated Razor components.
- No new design-system dependency is introduced without `#2`'s decision.
- If `#2` decides "new token layer," that work is filed as a bounded sibling ticket (`honua-console#11`) ahead of `#4`/`#5` porting.

MudBlazor remains scoped to legacy Admin; it is not added to the long-term Console shell without an ADR amendment.

### 10. File layout (binding)

The scaffold ticket establishes this layout; porting tickets extend it without reorganizing.

```
src/
  Honua.Console.Web/             # Blazor Web host (Server + WebAssembly render modes).
    Program.cs                   # DI, auth, HTTP clients, OpenTelemetry, render-mode setup.
    Routes.razor / App.razor     # Router, CascadingAuthenticationState, layout selection.
    wwwroot/
      interop/                   # JS interop modules: maps, charts, editor, sdk-js shim layer.
  Honua.Console.Components/      # Shared Razor component library.
    Shell/                       # AppShell, NavConfig, UserMenu, LoadingShell, ConsoleErrorBoundary.
    Surfaces/                    # EmptyState, Forbidden, status pills, evidence panels.
    Routes/Studio/               # @page-bearing Razor components per top-level group.
    Routes/Catalog/
    Routes/Operate/
    Routes/Share/
    Routes/Auth/
    Routes/Public/
    Routes/Embed/
  Honua.Console.Contracts/       # SDK shim boundary (see SDK_SHIM_POLICY.md). Removed in #7.
  Honua.Console.Auth/            # AuthenticationStateProvider, permission helpers, policies.
  Honua.Console.Tests/           # xUnit + bUnit component tests.
  Honua.Console.IntegrationTests/ # xUnit + Testcontainers against a real honua-server (section 11).
  Honua.Console.Smoke/           # Playwright-for-.NET smoke specs.
docs/
  adr/                           # Architecture decisions.
  migration/                     # This charter, freeze policy, SDK shim policy.
  roadmap/                       # Migration backlog and per-area roadmaps.
  runbook/                       # Per-ticket smoke runbooks attached to PR evidence.
  design-handoff/                # Information model, workflow catalog, surface briefs.
```

The `Honua.Console.Components` project is the same one a future MAUI Blazor Hybrid host would reference - the shell project is the only swap.

### 11. Real-server integration and no standing mocks (binding)

ADR-0001's "shared .NET-owned contracts" unifier means Console binds to **real honua-server data**, not hand-built fakes. The mock-first latitude in earlier planning drafts (the "or a stable checked-in mock contract" language in the roadmap backlogs) is **withdrawn**.

- **No standing mocks for server-owned data.** Any surface whose data is owned by honua-server (catalog/content, packages, publications, jobs, events, telemetry, GitOps, temporal, sync, RBAC/session) binds to honua-server through `honua-sdk-dotnet` projections - or, only until that projection lands, a thin `HttpClient` behind the single `Honua.Console.Contracts` shim boundary ([`SDK_SHIM_POLICY.md`](./SDK_SHIM_POLICY.md)), removed in [`honua-console#7`](https://github.com/honua-io/honua-console/issues/7). In-memory / fixture data clients are permitted only as a transient scaffold **inside the same PR** that lands the real binding and its integration test; an in-memory implementation must never be the merged data source of a server-owned surface.
- **A Testcontainers integration test is Definition of Done.** Every server-backed surface ships an xUnit integration test in `Honua.Console.IntegrationTests` that boots a real honua-server via Testcontainers (`Testcontainers.PostgreSql` + the honua-server container image, both already pinned in `honua-server/Directory.Packages.props`), seeds a known fixture, and asserts the surface renders from live data. Console reuses the honua-server bootstrap + seed contract from the `sdk-integration-testing-against-honua-server` work contract; it does not invent a parallel harness. Tests skip gracefully when Docker is unavailable, matching honua-server's existing pattern.
- **The #9 cross-surface gate runs against a real server** (`sourceHydrated: true`), not the in-memory adapters currently under `smoke/parity/`.
- **Genuinely-local client state is excluded.** Native environment profiles, the local session/token cache, and other host-local state are legitimately local; they may use local persistence and are out of scope for the no-mock rule. The rule targets server-owned data only.
- **Blocked surfaces wait; they do not fake.** A surface whose server contract is not yet implemented (open wrappers `honua-server#1181`-`#1186`, `#1165`) stays `blocked` until that contract lands - standing up a mock does not unblock it. Surfaces whose contracts are already closed (`#1162`, `#1180`, `#1168`, `#1170`) are unblocked now.

## Out of Scope For This Charter

- Concrete scaffold code (owned by `#2`).
- Route map and RBAC predicates (owned by `#3`).
- Per-area port plans (owned by `#4`/`#5`/`#6`).
- Single deployable artifact mechanics (owned by `#8` + [`honua-devops#55`](https://github.com/honua-io/honua-devops/issues/55)/[`#56`](https://github.com/honua-io/honua-devops/issues/56)).
- Cross-surface smoke wiring (owned by `#9`).
- `honua-portal` retirement mechanics (owned by `#10`; see [`PORTAL_FREEZE_POLICY.md`](./PORTAL_FREEZE_POLICY.md)).
- Optional MAUI Blazor Hybrid native host shell (owned by [`honua-console#26`](https://github.com/honua-io/honua-console/issues/26); reuses `Honua.Console.Components`).
