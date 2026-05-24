# Legacy Admin Route Disposition

Status: filed 2026-05-23.

Owners: Honua Console (this doc) and `honua-server-admin` (canonical inventory).

Decision source: [ADR-0001: Unified Honua Console Runtime](../adr/0001-unified-honua-console-runtime.md).

Implementing ticket: [honua-console#6](https://github.com/honua-io/honua-console/issues/6).

Related: [Operate Embed Contract](../operate/embed-contract.md).

## Purpose

ADR-0001 commits Honua to a single Console shell. Operator workflows still live in the Blazor WebAssembly `honua-server-admin` app. This document is the canonical, per-route record of how every legacy Admin route is exposed in Console during the transition, who owns each route, and what gate must pass before a route can be retired.

Two things this doc is, and one thing it is not:

- It IS the contract that the Console router, navigation, and embed component implement.
- It IS the contract that `honua-server-admin#96` consumes when it prepares the legacy bundle for transitional hosting (base href, CSP, frame ancestors).
- It is NOT the redesigned-operator IA. Native Blazor/Razor Operate views are the responsibility of follow-on tickets and replace `EMBED` rows one at a time.

## Disposition Vocabulary

| Disposition | Meaning |
| --- | --- |
| `KEEP` | Console exposes a native Console route at this path now. No legacy embed. |
| `EMBED` | Legacy route is reached from Console under `/operate/legacy/<verbatim-legacy-path>` via the same-origin embed contract. The Console mount preserves the legacy path verbatim — including any leading `/operator` or `/admin` segment — because the Blazor app's `@page` declarations resolve relative to base href `/operate/legacy/`. The legacy root `/` is an explicit exception: it maps only to Console `/operate` and is not part of the embed allowlist or bare-path passthrough. Retires when a native replacement parity-tests. |
| `REDIRECT-TO-STUDIO` | Legacy duplicate of a Studio surface. Console redirects the legacy path to the Studio canonical path. Not listed in Operate nav. The legacy route is still reachable at its verbatim `/operate/legacy/<verbatim-legacy-path>` location for use as a `MovedToStudioLanding` reference target (see the "Legacy reference targets" note below). |
| `RETIRE` | Marked for removal without a Console replacement. Either the workflow is being absorbed by Catalog/Share or it is being deleted outright. |

A route may carry one or two dispositions during the transition (for example, `EMBED` now, `REDIRECT-TO-STUDIO` once the Studio port lands). The "Retirement gate" column states what must be true before the row moves to its next state.

## Cross-Repo Preconditions

This table is implementable end-to-end only when all four preconditions hold:

1. `honua-console#2` lands the Blazor Web shell and shared Razor route table for `/operate/*` and `/studio/*`.
2. `honua-console#3` lands the IA, navigation, and `canSeeOperate` RBAC predicate.
3. `honua-server-admin#96` rehosts the legacy Blazor bundle so it serves under `/operate/legacy/` with the matching base href and a frame-ancestors policy that allows same-origin embedding, AND rewrites root-absolute in-app navigation (`NavMenu.razor`, `NavigateTo("/...")` call sites, in-page `<a href="/...">`) to base-relative form so in-frame clicks stay under `/operate/legacy/` (see [Legacy Link Resolution](../operate/embed-contract.md#legacy-link-resolution)).
4. `honua-devops#55` builds a single deployable artifact that serves Console and the legacy bundle from one origin.

Until all four hold, the EMBED rows operate in a degraded mode documented in [`embed-contract.md`](../operate/embed-contract.md) (`mode: link-out`).

## Route Disposition Table

Legacy paths are taken from `honua-server-admin/src/Honua.Admin/Pages/**/@page` declarations as of 2026-05-23.

Parameterized rows preserve the Blazor route constraint in the legacy-path column. The Console path column uses colon-prefixed `:param` names for readability, but the generated route projection and `OperateLegacyEmbed` allowlist MUST validate the inherited Blazor constraint before embedding: `{id:guid}` and `{ConnectionId:guid}` accept only GUID segments; `{LayerId:int}` accepts only integer segments.

`honua-console#36` adds native Blazor successors for the connection, resource, service, layer, and settings transition group. The rows below keep their legacy `/operate/legacy/...` paths until SDK-backed parity and smoke evidence retire them, but Operate navigation should prefer the native `/operate/...` routes documented in [Native Operate Transition Surface](../operate/native-transition-surface.md).

### Operator workflows kept in Operate

| Legacy path | Console path | Disposition | Replacement ticket | Owner | Retirement gate |
| --- | --- | --- | --- | --- | --- |
| `/` | `/operate` | `KEEP` | honua-console#3 (landing scaffold), follow-on | Console | Native `/operate` landing renders without the iframe and the Operate parity smoke passes. |
| `/operator/data-connections` | `/operate/legacy/operator/data-connections` | `EMBED` | honua-console#36 successor: `/operate/connections` | Operate | SDK-backed native connection list and connector smoke pass. |
| `/operator/data-connections/new` | `/operate/legacy/operator/data-connections/new` | `EMBED` | honua-console#36 successor: `/operate/connections/new` | Operate | Same as parent. |
| `/operator/data-connections/{id:guid}` | `/operate/legacy/operator/data-connections/:id` | `EMBED` | honua-console#36 successor: `/operate/connections/:id` | Operate | Same as parent, including legacy GUID deep-link handling. |
| `/operator/data-connections/{id:guid}/diagnostics` | `/operate/legacy/operator/data-connections/:id/diagnostics` | `EMBED` | honua-console#36 successor: `/operate/connections/:id/diagnostics` | Operate | Same as parent, with non-secret diagnostic evidence verified. |
| `/operator/publishing` | `/operate/legacy/operator/publishing` | `EMBED` | TBD (Publishing v2) | Operate | Native Publishing ships and publish smoke passes. |
| `/operator/operations` | `/operate/legacy/operator/operations` | `EMBED` | TBD | Operate | Native Operations view ships. |
| `/operator/control-center` | `/operate/legacy/operator/control-center` | `EMBED` | TBD | Operate | Native Control Center ships. |
| `/operator/license` | `/operate/legacy/operator/license` | `EMBED` | TBD | Operate | Native License view ships. |
| `/operator/print` | `/operate/legacy/operator/print` | `EMBED` | TBD | Operate | Native Print service ships, or workflow is absorbed into Share. |
| `/operator/admin-readiness` | `/operate/legacy/operator/admin-readiness` | `EMBED` (early `RETIRE` candidate) | Folded into `/operate/health` | Operate | `/operate/health` ships and matches readiness checks. |
| `/operator/analytics` | `/operate/legacy/operator/analytics` | `EMBED` | TBD | Operate | Native Analytics ships. |

### Admin/service-catalog routes kept in Operate

| Legacy path | Console path | Disposition | Replacement ticket | Owner | Retirement gate |
| --- | --- | --- | --- | --- | --- |
| `/services` | `/operate/legacy/services` | `EMBED` | honua-console#36 successor: `/operate/services` | Operate | SDK-backed native services list and publish/service smoke pass. |
| `/services/{ServiceName}/settings` | `/operate/legacy/services/:serviceName/settings` | `EMBED` | honua-console#36 successor: `/operate/services/:name/settings` | Operate | Same as parent, including runtime settings and publication slots. |
| `/layers` | `/operate/legacy/layers` | `EMBED` | honua-console#36 successor: `/operate/layers` | Operate | SDK-backed native layer list and service-layer smoke pass. |
| `/layers/{LayerId:int}` | `/operate/legacy/layers/:layerId` | `EMBED` | honua-console#36 successor: `/operate/layers/:id` | Operate | Same as parent, with canonical resource ownership links. |
| `/layers/{LayerId:int}/configure` | `/operate/legacy/layers/:layerId/configure` | `EMBED` | TBD | Operate | Same as parent. |
| `/layers/{LayerId:int}/preview` | `/operate/legacy/layers/:layerId/preview` | `EMBED` | TBD | Operate | Same as parent. |
| `/services/{ServiceName}/layers/{LayerId:int}/preview` | `/operate/legacy/services/:serviceName/layers/:layerId/preview` | `EMBED` | TBD | Operate | Same as parent. |
| `/layers/{LayerId:int}/style` | `/operate/legacy/layers/:layerId/style` | `EMBED` | TBD | Operate | Same as parent. |
| `/connections` | `/operate/legacy/connections` | `EMBED` (legacy already redirects) | honua-console#36 successor: `/operate/connections` | Operate | SDK-backed native connection list and connector smoke pass. |
| `/connections/new` | `/operate/legacy/connections/new` | `EMBED` | honua-console#36 successor: `/operate/connections/new` | Operate | Same as parent. |
| `/connections/{ConnectionId:guid}` | `/operate/legacy/connections/:connectionId` | `EMBED` | honua-console#36 successor: `/operate/connections/:id` | Operate | Same as parent, including legacy GUID deep-link handling. |
| `/connections/{ConnectionId}/layers` | `/operate/legacy/connections/:connectionId/layers` | `EMBED` | TBD | Operate | Same as parent. |
| `/connections/{ConnectionId}/publish` | `/operate/legacy/connections/:connectionId/publish` | `EMBED` | TBD | Operate | Native publish flow ships. |
| `/admin/connection-registry` | `/operate/legacy/admin/connection-registry` | `EMBED` | honua-console#36 successor: `/operate/connections` | Operate | SDK-backed native connection registry semantics and connector smoke pass. |
| `/admin/connection-registry/new` | `/operate/legacy/admin/connection-registry/new` | `EMBED` | honua-console#36 successor: `/operate/connections/new` | Operate | Same as parent. |
| `/admin/connection-registry/{ConnectionId}` | `/operate/legacy/admin/connection-registry/:connectionId` | `EMBED` | honua-console#36 successor: `/operate/connections/:id` | Operate | Same as parent, including legacy ID mapping. |
| `/admin/gitops` | `/operate/legacy/admin/gitops` | `EMBED` | TBD | Operate | Native GitOps view ships. |
| `/admin/identity/api-keys` | `/operate/legacy/admin/identity/api-keys` | `EMBED` | TBD (identity v2) | Operate | Native identity surface ships. |
| `/admin/identity/status` | `/operate/legacy/admin/identity/status` | `EMBED` | TBD | Operate | Same as parent. |
| `/admin/identity/providers` | `/operate/legacy/admin/identity/providers` | `EMBED` | TBD | Operate | Same as parent. |
| `/admin/identity/diagnostics` | `/operate/legacy/admin/identity/diagnostics` | `EMBED` | TBD | Operate | Same as parent. |
| `/server-info` | `/operate/legacy/server-info` | `EMBED` (native rewrite candidate) | Folded into `/operate/health` | Operate | `/operate/health` ships and matches server-info data. |
| `/observability` | `/operate/legacy/observability` | `EMBED` | TBD | Operate | Native observability view ships. |
| `/deploy` | `/operate/legacy/deploy` | `EMBED` | TBD | Operate | Native deploy view ships. |

### Duplicate-builder routes redirected to Studio

| Legacy path | Studio redirect target | Disposition | Legacy reference target (embed) | Replacement ticket | Owner | Retirement gate |
| --- | --- | --- | --- | --- | --- | --- |
| `/operator/app-builder` | `/studio/apps` | `REDIRECT-TO-STUDIO` | `/operate/legacy/operator/app-builder` | honua-console#5 | Studio | Studio app-builder ships and parity smoke passes; legacy page is marked "Reference / experimental" today (`Pages/Operator/AppBuilder.razor:14-17`). |
| `/operator/spec` | `/studio/spec` | `REDIRECT-TO-STUDIO` | `/operate/legacy/operator/spec` | honua-console#5 | Studio | Studio spec workspace ships. |
| `/operator/sql` | `/studio/sql` (target TBD) | `REDIRECT-TO-STUDIO` | `/operate/legacy/operator/sql` | honua-console#5 | Studio | Studio SQL surface decision lands (see Open Questions); until then, Console renders the `moved-to-studio` landing for this path. |
| `/operator/annotations` | `/studio/maps` (target TBD) | `REDIRECT-TO-STUDIO` | `/operate/legacy/operator/annotations` | honua-console#5 | Studio | Studio annotation surface lands (see Open Questions). |

Until Studio ports complete (honua-console#5), `REDIRECT-TO-STUDIO` rows resolve to a Console `moved-to-studio` landing that:

- Explains why the path moved.
- Links to the Studio target if available.
- Provides a single explicit "Open the legacy reference" affordance that opens the legacy route at the row's "Legacy reference target (embed)" path via `OperateLegacyEmbed` (gated by `canSeeOperate`).

**Legacy reference targets.** The "Legacy reference target (embed)" column extends the embed allowlist for these four duplicate-builder paths. `OperateLegacyEmbed` MUST accept these paths in addition to the `EMBED` rows above; the embed-mode `NotFound` rule in [`embed-contract.md`](../operate/embed-contract.md) applies to any path matching neither set. Reference targets are reachable only via `MovedToStudioLanding`; they do not appear in Operate navigation and there is no Console-side redirect from `/operate/legacy/operator/<duplicate>` back to Studio (the redirect rule lives at the bare legacy path, not the reference target).

### Routes flagged for retirement

| Legacy path | Console path | Console disposition | Owner | Retirement gate |
| --- | --- | --- | --- | --- |
| `/operator/open-data` | `/operate/legacy/operator/open-data` | `EMBED` now, `RETIRE` from Operate once Catalog/Share own Open Data | Share / Catalog | Catalog/Share open-data surface ships (honua-console#4) and is the only entry point. Until then the embed remains reachable via direct URL but is not listed in Operate nav. |
| `/operator/admin-readiness` | `/operate/legacy/operator/admin-readiness` | `EMBED` now, `RETIRE` once `/operate/health` exists | Operate | Native readiness view in `/operate/health`. |
| `/server-info` | `/operate/legacy/server-info` | `EMBED` now, `RETIRE` once `/operate/health` exists | Operate | Same as above. |

## Console-Side Surfaces This Ticket Establishes

The shapes below are the contract that scaffold and IA tickets implement. They live in this repo once `honua-console#2` lands; this ticket commits to the surface names so downstream code can plug in without rediscovery.

- `src/Honua.Console.Shell/Pages/OperatePage.razor` - native Console landing for `/operate`. Renders the Operate sub-section index.
- `src/Honua.Console.Shell/Components/OperateLegacyEmbed.razor` - single iframe host for `/operate/legacy/*`. Implements the [embed contract](../operate/embed-contract.md).
- `src/Honua.Console.Shell/Components/OperateRedirect.razor` - single component used by `REDIRECT-TO-STUDIO` rows. Reads target from disposition data, falls back to the `moved-to-studio` landing.
- `src/Honua.Console.Shell/Pages/MovedToStudioLanding.razor` - fallback target for redirects before Studio ports land.
- `src/Honua.Console.Shell/Models/LegacyAdminRouteDisposition.cs` - typed projection of this document used by router wiring. The source of truth for the table is this Markdown; the C# projection is a generated/maintained mirror, not a divergent copy.
- `src/Honua.Console.Shell/Auth/OperatePermissions.cs` - single RBAC predicate Operate routes use. Sourced from `canSeeOperatorLinks` (scopes `operator` / `admin`) per design.

## Error And Empty-State Surfaces

All four surfaces are required and must use the shared Console primitives that `honua-console#2` / `#3` establish. They are listed here so the embed component and router know which slot to render.

| Condition | Surface | Owner |
| --- | --- | --- |
| Unknown `/operate/legacy/<path>` | Console `NotFound` | Console |
| Authenticated user lacks Operate scope | Console `Forbidden` | Console |
| Iframe fails to load (network, frame-ancestors deny, base-href misconfig) | Console `EmptyState` with retry + direct legacy-URL fallback (when same-origin precondition is unmet) | Console |
| Legacy returns unsupported service metadata or unsupported package binding | Owned by legacy (Console only owns the frame) | Legacy |

## Operate Navigation Contract

`honua-console#3` owns the navigation definition. `honua-console#36` moves the first transition group to native Blazor routes. Current navigation should expose:

- **Connections** → `/operate/connections` (plus `/operate/connections/new`, `/operate/connections/:id`, and `/operate/connections/:id/diagnostics`)
- **Resources** → `/operate/resources` (plus `/operate/resources/new` and `/operate/resources/:id`)
- **Services** → `/operate/services` (plus `/operate/services/:name/settings`)
- **Layers** → `/operate/layers` (plus `/operate/layers/:id`)
- **Settings** → `/operate/settings`

Remaining legacy-only sections preserve the legacy prefix verbatim under `/operate/legacy/`:

- **Publishing** → `/operate/legacy/operator/publishing`
- **Identity** → `/operate/legacy/admin/identity/*`
- **Observability** → `/operate/legacy/observability`
- **Deploy** → `/operate/legacy/deploy`
- **GitOps** → `/operate/legacy/admin/gitops`
- **Operations** → `/operate/legacy/operator/operations`
- **Control Center** → `/operate/legacy/operator/control-center`
- **License** → `/operate/legacy/operator/license`
- **Print** → `/operate/legacy/operator/print`
- **Analytics** → `/operate/legacy/operator/analytics`

Legacy connection, service, and layer URLs remain valid direct paths through the embed while parity gates are open; they should not be the primary navigation target once the native transition routes are available.

Duplicate-builder routes (`app-builder`, `spec`, `sql`, `annotations`) are not exposed in Operate navigation.

`open-data`, `admin-readiness`, and `server-info` are reachable via direct URL while still under `EMBED`, but `admin-readiness` and `server-info` are not enumerated in nav once `/operate/health` exists, and `open-data` is not enumerated in Operate nav at all (it lives in Catalog/Share nav).

## Telemetry And Smoke Evidence

Per the project's telemetry constraint, a Console Operate smoke must:

1. Sign in as an operator-scoped user.
2. Visit `/operate` and confirm the landing renders.
3. Visit `/operate/legacy/operator/publishing` and confirm the embed loads (or the documented degraded surface, if the same-origin precondition is unmet).
4. Inside the publishing embed, click an in-frame nav entry (for example, the legacy NavMenu's "Services" link) and confirm the iframe URL stays under `/operate/legacy/` (proves the `honua-server-admin#96` link rewrite is in place and the embed does not escape to a bare legacy path).
5. Visit a bare legacy path that maps to a non-root `EMBED` row (for example, `/services`) and confirm the Console router passthrough redirects to `/operate/legacy/services`.
6. Trigger one embedded download — for example, visit `/operate/legacy/operator/print` and download a print preview, or `/operate/legacy/operator/analytics` and export a usage CSV — and confirm the browser writes the file (proves the `allow-downloads` sandbox token is in place).
7. Visit `/operator/app-builder` and confirm the redirect lands on the Studio target or the `moved-to-studio` page.
8. Sign in as a non-operator user, visit `/operate`, and confirm the `Forbidden` surface renders.

The smoke is owned by this ticket but lands physically once `honua-console#2` has the Playwright harness in place.

## Open Questions

Tracked here so #2/#3 implementers and Studio (#5) port owners see the open decisions:

1. `/operator/annotations` target — Studio map canvas, Catalog item-level annotations, or remain `EMBED`? Owner: Studio / Catalog joint call.
2. `/operator/sql` target — Studio surface, retire entirely in favor of NL query, or keep `EMBED`? Owner: Studio.
3. Embed mount path — confirmed `/operate/legacy/<verbatim-legacy-path>` here (including the `/operator` and `/admin` prefixes the legacy `@page` declarations carry). Tables and nav reflect this verbatim form; revisit only if `honua-server-admin#96` requires otherwise.
4. Iframe vs. true reverse proxy — this disposition assumes iframe-in-Console (Console chrome wraps the legacy surface). True reverse proxy is a fallback; revisit if `honua-devops#55` cannot embed.
5. Pre-`honua-devops#55` posture — the embed contract documents the degraded `link-out` mode. Operators reach legacy via direct origin until single-artifact lands.
6. Retirement gate refinement — current bar is "replacement ships AND parity smoke passes." Per-route owner sign-off MAY be added; flag a row here if so.

## How This Document Stays Current

- Every Console ticket that redesigns an operator workflow MUST update the corresponding row's disposition, replacement ticket, and retirement gate in the same PR.
- Every `honua-server-admin` change that adds or removes a legacy route MUST update the inventory in `honua-server-admin#96` and surface a PR here to keep this table aligned.
- The `HONUA_CONSOLE_MIGRATION_BACKLOG.md` parity gate links to this doc; the gate cannot close while any `EMBED` row remains.
