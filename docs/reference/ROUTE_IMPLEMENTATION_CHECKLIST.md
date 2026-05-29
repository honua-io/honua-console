# Route Implementation Checklist

Status: filed 2026-05-28 as a contributor guide for adding a new
route/feature slice to Honua Console.

Decision sources:

- [ADR-0001: Unified Honua Console Runtime](../adr/0001-unified-honua-console-runtime.md)
- [Console Patterns Charter](../migration/CONSOLE_PATTERNS_CHARTER.md) — binding patterns
- [Console Route Map, RBAC, and Navigation](../console-route-map.md) — IA source of truth
- [Shared Component API Reference](./SHARED_COMPONENT_API.md) — reusable components

This guide walks a contributor through adding a new route/feature slice
end to end. It does **not** re-decide the patterns it references — the
route taxonomy, RBAC predicates, and exception surfaces are owned by the
route map and the charter, and a new feature consumes them rather than
forking them (charter, "Pattern Ownership"). If a step exposes a pattern
gap, the fix is to amend the route map or charter, not to invent a
parallel structure inside the feature.

A fully worked example tracing the implemented `/catalog` route through
every step is in §3.

---

## 1. Where things live

The implemented layout (matching charter §10, with the shared library
currently named `Honua.Console.Shell` rather than the charter's working
name `Honua.Console.Components`):

```
src/
  Honua.Console.Shell/            # Shared Razor library (routes + components + seams)
    ConsoleRoutes.razor           # Single <Router>; Found/NotFound; DefaultLayout
    _Imports.razor                # Shared @using set for all .razor files
    Layout/                       # ConsoleLayout (default), EmbedLayout (shell-less)
    Pages/                        # @page-bearing route components
    Components/                   # Reusable components (see component API reference)
    Models/                       # Route map, parameter model types, fixtures
    Services/                     # Data-client seams: I<X>Client + InMemory<X>Client
    DependencyInjection/          # AddHonuaConsoleShell() registers the seams
  Honua.Console.Web/              # Blazor Web host (Program.cs, render modes)
  Honua.Console.Contracts/        # SDK shim boundary types (charter §6, SDK_SHIM_POLICY)
tests/
  Honua.Console.Native.Core.Tests/ # xUnit tests for seams, models, and route map
smoke/
  parity/                         # Cross-surface parity smoke (honua-console#9)
docs/
  console-route-map.md            # IA source of truth
```

---

## 2. The checklist

Do these in order. Each step cites the file(s) you touch.

### Step 1 — Route-map entry

Add or confirm the route in
[`docs/console-route-map.md`](../console-route-map.md) **before** writing
code. The route map is the IA source of truth; a feature that diverges
without first updating the map should be sent back (route map §14).

- Pick the URL shape under a frozen top-level prefix: `/studio`,
  `/catalog`, `/operate`, `/share`, or the out-of-group `/auth/*`,
  `/public*`, `/embed/*`, `/maps/*`, `/groups` (route map §1).
- Declare the **gate** using the gate grammar (route map §4.6):
  `anonymous`, `auth`, `scope:<name>`, `item-role:<min>`,
  `share-tier:<min>`, `open-data`, `entitlement:<key>`, `edition:<tier>`.
- Name the **empty** and **forbidden** surfaces from the canonical set
  (route map §7) — do not invent new copy.
- Assign the **code-split chunk** (route map §9) so `/share/*` and
  `/embed/*` can paint without loading Studio or Operate.
- If the route is portal/admin parity, add the **smoke label** (route map
  §3, §10) and the redirect/frozen-URL disposition (§3, §8, §5).

### Step 2 — IA / navigation

- Top-level areas are defined once in
  `src/Honua.Console.Shell/Models/ConsoleRouteMap.cs` (`Areas`) and
  rendered by `Layout/ConsoleLayout.razor`'s primary nav. A new top-level
  area is added there.
- Operator-only entries are filtered at the nav layer, not inside the
  route component (charter §3). The Operate secondary nav in
  `ConsoleLayout.razor` is shown only on `/operate/*` routes via
  `ConsoleRouteMap.IsOperateRoute(...)`. Add new Operate sub-nav links
  there, behind the same predicate.
- Do not promote operator entry points on builder routes or vice versa
  (charter §4).

### Step 3 — Component (the route)

- Create a `@page`-bearing component under
  `src/Honua.Console.Shell/Pages/`. A component can carry multiple
  `@page` directives for aliases/deep links (see
  `Pages/OperateObservabilityPage.razor`, which serves
  `/operate/observability` plus the event/alert/job detail deep links).
- Route parameters bind via `[Parameter]` properties named to match the
  `{segment}` tokens in the `@page` template.
- Reuse shared components for chrome and state surfaces (see the
  [Shared Component API Reference](./SHARED_COMPONENT_API.md)); render
  `<ConsoleStateView>` for loading/empty/forbidden/missing states rather
  than authoring bespoke markup (charter §5).
- Resolve route-specific data **inside the route component**, not in a
  centralized pre-shell layer (charter §2, §7). Use
  `OnParametersSetAsync` and flow a `CancellationToken` where the seam
  accepts one.
- The route is owned by the shared library so the same component renders
  under both the web host and the optional MAUI Blazor Hybrid host
  (charter §10).

### Step 4 — Guards (auth / RBAC / edition / entitlement)

- Enforce the gate declared in Step 1. Read session/permission state
  through the shared seam, not raw claims inline (charter §3).
- For anonymous-capable routes (Share, Embed, Catalog item, Maps viewer),
  resolve the read context first and render
  `<ConsoleStateView Kind="unavailable">` for anonymous denials so
  upgrade copy and protected titles are not leaked (route map §7, §13 Q5).
- Authenticated item routes rely on the server/SDK read call to authorize
  the load; map a denial to `forbidden`, a not-found to `missing`, and an
  unsupported schema/package to `unsupported-service` /
  `unsupported-package` (route map §4.6, §7).
- Edition/entitlement gates parse `LicenseInfo.Edition` to the rank table
  (`Community=0, Pro=1, Enterprise=2`) — never lexicographic string
  compare (route map §2.3).

### Step 5 — Data-client seam

- Server-owned data binds through `honua-sdk-dotnet` projections or, until
  those land, a thin client behind the single `Honua.Console.Contracts`
  shim boundary (charter §6, §11; `SDK_SHIM_POLICY.md`). **No standing
  mocks** for server-owned data — an in-memory client is permitted only as
  a transient scaffold inside the same PR that lands the real binding and
  its integration test (charter §11).
- Define the seam as an interface in `src/Honua.Console.Shell/Services/`
  (e.g. `IConsoleCatalogClient`), keyed off `Honua.Console.Contracts`
  request/result types. Return a result record with a status enum so the
  route can map each status to a §7 surface (see
  `CatalogItemReadResult` / `CatalogReadStatus`).
- Register the seam in
  `DependencyInjection/HonuaConsoleShellServiceCollectionExtensions.cs`
  via `TryAdd*` so the host can override it.
- Inject the seam into the route component with `@inject`.

### Step 6 — Unit / bUnit tests

- Add xUnit tests under
  `tests/Honua.Console.Native.Core.Tests/`. The current suite tests the
  data-client seams, route map, parameter models, and route-state mapping
  helpers directly (e.g. `ConsoleCatalogClientTests`,
  `ConsoleRouteMapTests`, `CatalogSearchStateTests`). Cover at least: the
  gate/read-context decision, each status → surface mapping, and the
  query/URL contract.
- The charter (§1) names **bUnit** for component/markup tests. bUnit is
  **not yet wired into the test project**
  (`tests/Honua.Console.Native.Core.Tests/Honua.Console.Native.Core.Tests.csproj`
  references xUnit only). Until it is added, factor route logic into
  testable static/instance helpers (as `MapDraftPage.ResolveReadState`,
  `MapViewerPage.ResolveViewerActions`, and `CatalogSearchState.FromUri`
  do) and assert those with xUnit. When bUnit lands, component-render
  assertions go in the same project.

### Step 7 — Parity smoke expectations

- If the route carries a smoke label (route map §3, §10), preserve or add
  evidence at that label in the cross-surface parity smoke under
  `smoke/parity/` (charter §8; `docs/smoke/parity.md`). The pipeline is
  `publish → catalog → Studio → share/embed`; the labels are
  `catalog-list`, `catalog-detail`, `viewer`, `studio-generation`,
  `open-data`, `share`, `embed`.
- Run `npm run smoke:parity` for the scenario and
  `npm run smoke:parity:test` for the harness's own unit tests.
- The `honua-console#9` gate runs against a **real honua-server**
  (`sourceHydrated: true`), not the in-memory adapters; a smoke that
  passes only against fixtures does not satisfy it (charter §8, §11).

### Step 8 — Build and verify

- `dotnet build` (the sln is `Honua.Console.slnx`).
- `dotnet test` for the xUnit suite.
- `npm run smoke:parity:test` for the smoke harness unit tests.

---

## 3. Worked example: `/catalog`

This traces the **implemented** `/catalog` list route through every step,
citing the actual files.

### Step 1 — Route-map entry

`/catalog` is row 8 of the Portal → Console destination map
(`docs/console-route-map.md` §3) and is catalogued in §6.3:

- URL: `/catalog`; query contract pinned to Portal keys `q`, `type`,
  `tag`, `owner`, `visibility`, `sort`, `cursor` (unknown keys are ignored,
  not forwarded).
- Gate: `auth` — the list requires a signed-in workspace session; public
  collection reads live under `/share/*` instead.
- Empty surface: `empty-catalog` ("start a search / publish first item");
  forbidden surface: `unauth-redirect`.
- Chunk: `catalog` (§9). Smoke label: `catalog-list` (§3, §10).

### Step 2 — IA / navigation

Catalog is a top-level builder area, defined once in
`src/Honua.Console.Shell/Models/ConsoleRouteMap.cs`:

```csharp
new("catalog", "Catalog", "/catalog", "Builder",
    "Data, services, maps, dashboards, apps, metadata, and provenance."),
```

`Layout/ConsoleLayout.razor` renders it in the primary nav by iterating
`ConsoleRouteMap.Areas`. Because Catalog is a builder area, no Operate
secondary nav appears (the secondary nav is gated by
`ConsoleRouteMap.IsOperateRoute`).

### Step 3 — Component

`src/Honua.Console.Shell/Pages/CatalogPage.razor` declares
`@page "/catalog"` and injects the seam and supporting services:

```razor
@page "/catalog"
@inject IConsoleCatalogClient Catalog
@inject IConsoleCatalogReadContextResolver ReadContexts
@inject NavigationManager Navigation
```

It parses the URL into `CatalogSearchState.FromUri(Navigation.Uri)` and
loads data inside `OnParametersSetAsync` (route-owned fetch, charter §2),
rendering the filter bar, type strip, and content table.

### Step 4 — Guards

The `auth` gate is enforced via the read-context resolver before any data
loads, in `CatalogPage.OnParametersSetAsync`:

```csharp
var context = await ReadContexts.ResolveAsync(publicLinkToken: null);
_accessResolved = true;
if (context.Anonymous)
{
    _requiresAuthentication = true;
    return;
}
_result = await Catalog.SearchAsync(_search.ToListRequest(), context);
```

An anonymous context renders
`<ConsoleStateView Kind="unauthenticated" ... ActionHref="@BuildSignInHref()">`
(the sign-in surface from route map §7), and the loading/empty branches
render `<ConsoleStateView Kind="loading">` and
`<ConsoleStateView Kind="empty">`. No raw claims are read in the
component — the decision comes through the seam (charter §3).

### Step 5 — Data-client seam

The seam is `IConsoleCatalogClient` in
`src/Honua.Console.Shell/Services/IConsoleCatalogClient.cs`, keyed off
`Honua.Console.Contracts` types (`CatalogListRequest`,
`CatalogReadContext`) and returning result records with a status enum
(`CatalogSearchResult`, `CatalogItemReadResult`, `MapPackageReadResult`,
`CatalogReadStatus`) so the route maps each status to a §7 surface.

The read context is resolved by
`IConsoleCatalogReadContextResolver` /
`ConsoleCatalogReadContextResolver`
(`Services/IConsoleCatalogReadContextResolver.cs`), which checks the
active environment profile and session store to decide authenticated vs.
anonymous-public-link.

The transient scaffold implementation is
`InMemoryConsoleCatalogClient` (charter §11 — permitted only until the
real `honua-sdk-dotnet` binding and its Testcontainers integration test
land in the same PR). Both the resolver and the client are registered in
`DependencyInjection/HonuaConsoleShellServiceCollectionExtensions.cs`:

```csharp
services.TryAddScoped<IConsoleCatalogReadContextResolver, ConsoleCatalogReadContextResolver>();
services.TryAddSingleton<IConsoleCatalogClient, InMemoryConsoleCatalogClient>();
```

### Step 6 — Unit tests

`tests/Honua.Console.Native.Core.Tests/ConsoleCatalogClientTests.cs`
covers the seam and read-context decisions, e.g.:

- `CatalogSearchFiltersProtectedSummariesForAnonymousContext` — anonymous
  vs. authenticated visibility filtering.
- `NoSessionTokenlessReadsUseAnonymousPublicContext` and
  `ActiveSessionTokenlessReadsUseAuthenticatedContext` — the resolver's
  gate decision.
- `UnsupportedStatesUseSharedReadStatuses` — status → surface mapping
  (`UnsupportedServiceMetadata`, `UnsupportedPackageBinding`).

`CatalogSearchStateTests.cs` covers the `q/type/tag/owner/visibility/
sort/cursor` query contract and unknown-key handling. Per Step 6, these
are xUnit tests against testable helpers; bUnit is not yet wired into this
project.

### Step 7 — Parity smoke

`/catalog` carries the `catalog-list` smoke label (route map §3 row 8,
§10). The cross-surface parity smoke under `smoke/parity/` exercises the
`publish → catalog → Studio → share/embed` chain; the catalog step asserts
the published item appears in the list/detail surfaces. Run
`npm run smoke:parity` and `npm run smoke:parity:test`. The
`honua-console#9` gate runs this against a real server, not the in-memory
adapter (charter §8, §11).

### Step 8 — Build

`dotnet build`, then `dotnet test`, then `npm run smoke:parity:test`.

---

## 4. Maintenance

This guide tracks the implemented route slice patterns. When the data
seam, route registration, exception-surface, or test conventions change,
update the relevant step and re-verify the §3 worked example against the
cited files in the same PR.
