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
- It is NOT the redesigned-operator IA. Native React Operate views are the responsibility of follow-on tickets and replace `EMBED` rows one at a time.

## Disposition Vocabulary

| Disposition | Meaning |
| --- | --- |
| `KEEP` | Console exposes a native React route at this path now. No legacy embed. |
| `EMBED` | Legacy route is reached from Console under `/operate/legacy/<legacy-path>` via the same-origin embed contract. Retires when a native replacement parity-tests. |
| `REDIRECT-TO-STUDIO` | Legacy duplicate of a Studio surface. Console redirects the legacy path to the Studio canonical path. Not listed in Operate nav. |
| `RETIRE` | Marked for removal without a Console replacement. Either the workflow is being absorbed by Catalog/Share or it is being deleted outright. |

A route may carry one or two dispositions during the transition (for example, `EMBED` now, `REDIRECT-TO-STUDIO` once the Studio port lands). The "Retirement gate" column states what must be true before the row moves to its next state.

## Cross-Repo Preconditions

This table is implementable end-to-end only when all four preconditions hold:

1. `honua-console#2` lands the React/TypeScript shell that exposes a route table for `/operate/*` and `/studio/*`.
2. `honua-console#3` lands the IA, navigation, and `canSeeOperate` RBAC predicate.
3. `honua-server-admin#96` rehosts the legacy Blazor bundle so it serves under `/operate/legacy/` with the matching base href and a frame-ancestors policy that allows same-origin embedding.
4. `honua-devops#55` builds a single deployable artifact that serves Console and the legacy bundle from one origin.

Until all four hold, the EMBED rows operate in a degraded mode documented in [`embed-contract.md`](../operate/embed-contract.md) (`mode: link-out`).

## Route Disposition Table

Legacy paths are taken from `honua-server-admin/src/Honua.Admin/Pages/**/@page` declarations as of 2026-05-23.

### Operator workflows kept in Operate

| Legacy path | Console path | Disposition | Replacement ticket | Owner | Retirement gate |
| --- | --- | --- | --- | --- | --- |
| `/` | `/operate` | `EMBED` initially, then `KEEP` once the native landing ships | honua-console#3 (landing scaffold), follow-on | Console | Native `/operate` landing renders without the iframe and the Operate parity smoke passes. |
| `/operator/data-connections` | `/operate/legacy/data-connections` | `EMBED` | TBD (operator data-connections redesign) | Operate | Native replacement ships and connector smoke passes. |
| `/operator/data-connections/new` | `/operate/legacy/data-connections/new` | `EMBED` | TBD | Operate | Same as parent. |
| `/operator/data-connections/{id}` | `/operate/legacy/data-connections/:id` | `EMBED` | TBD | Operate | Same as parent. |
| `/operator/data-connections/{id}/diagnostics` | `/operate/legacy/data-connections/:id/diagnostics` | `EMBED` | TBD | Operate | Same as parent. |
| `/operator/publishing` | `/operate/legacy/publishing` | `EMBED` | TBD (Publishing v2) | Operate | Native Publishing ships and publish smoke passes. |
| `/operator/operations` | `/operate/legacy/operations` | `EMBED` | TBD | Operate | Native Operations view ships. |
| `/operator/control-center` | `/operate/legacy/control-center` | `EMBED` | TBD | Operate | Native Control Center ships. |
| `/operator/license` | `/operate/legacy/license` | `EMBED` | TBD | Operate | Native License view ships. |
| `/operator/print` | `/operate/legacy/print` | `EMBED` | TBD | Operate | Native Print service ships, or workflow is absorbed into Share. |
| `/operator/admin-readiness` | `/operate/legacy/admin-readiness` | `EMBED` (early `RETIRE` candidate) | Folded into `/operate/health` | Operate | `/operate/health` ships and matches readiness checks. |
| `/operator/analytics` | `/operate/legacy/analytics` | `EMBED` | TBD | Operate | Native Analytics ships. |

### Admin/service-catalog routes kept in Operate

| Legacy path | Console path | Disposition | Replacement ticket | Owner | Retirement gate |
| --- | --- | --- | --- | --- | --- |
| `/services` | `/operate/legacy/services` | `EMBED` | TBD (service catalog v2) | Operate | Native service catalog ships and publish smoke passes. |
| `/services/{ServiceName}/settings` | `/operate/legacy/services/:serviceName/settings` | `EMBED` | TBD | Operate | Same as parent. |
| `/layers` | `/operate/legacy/layers` | `EMBED` | TBD | Operate | Native layers view ships. |
| `/layers/{LayerId}` | `/operate/legacy/layers/:layerId` | `EMBED` | TBD | Operate | Same as parent. |
| `/layers/{LayerId}/configure` | `/operate/legacy/layers/:layerId/configure` | `EMBED` | TBD | Operate | Same as parent. |
| `/layers/{LayerId}/preview` | `/operate/legacy/layers/:layerId/preview` | `EMBED` | TBD | Operate | Same as parent. |
| `/services/{ServiceName}/layers/{LayerId}/preview` | `/operate/legacy/services/:serviceName/layers/:layerId/preview` | `EMBED` | TBD | Operate | Same as parent. |
| `/layers/{LayerId}/style` | `/operate/legacy/layers/:layerId/style` | `EMBED` | TBD | Operate | Same as parent. |
| `/connections` | `/operate/legacy/connections` | `EMBED` (legacy already redirects) | TBD | Operate | Same as parent. |
| `/connections/new` | `/operate/legacy/connections/new` | `EMBED` | TBD | Operate | Same as parent. |
| `/connections/{ConnectionId}` | `/operate/legacy/connections/:connectionId` | `EMBED` | TBD | Operate | Same as parent. |
| `/connections/{ConnectionId}/layers` | `/operate/legacy/connections/:connectionId/layers` | `EMBED` | TBD | Operate | Same as parent. |
| `/connections/{ConnectionId}/publish` | `/operate/legacy/connections/:connectionId/publish` | `EMBED` | TBD | Operate | Native publish flow ships. |
| `/admin/connection-registry` | `/operate/legacy/connection-registry` | `EMBED` | TBD | Operate | Native connection-registry view ships. |
| `/admin/connection-registry/new` | `/operate/legacy/connection-registry/new` | `EMBED` | TBD | Operate | Same as parent. |
| `/admin/connection-registry/{ConnectionId}` | `/operate/legacy/connection-registry/:connectionId` | `EMBED` | TBD | Operate | Same as parent. |
| `/admin/gitops` | `/operate/legacy/gitops` | `EMBED` | TBD | Operate | Native GitOps view ships. |
| `/admin/identity/api-keys` | `/operate/legacy/identity/api-keys` | `EMBED` | TBD (identity v2) | Operate | Native identity surface ships. |
| `/admin/identity/status` | `/operate/legacy/identity/status` | `EMBED` | TBD | Operate | Same as parent. |
| `/admin/identity/providers` | `/operate/legacy/identity/providers` | `EMBED` | TBD | Operate | Same as parent. |
| `/admin/identity/diagnostics` | `/operate/legacy/identity/diagnostics` | `EMBED` | TBD | Operate | Same as parent. |
| `/server-info` | `/operate/legacy/server-info` | `EMBED` (native rewrite candidate) | Folded into `/operate/health` | Operate | `/operate/health` ships and matches server-info data. |
| `/observability` | `/operate/legacy/observability` | `EMBED` | TBD | Operate | Native observability view ships. |
| `/deploy` | `/operate/legacy/deploy` | `EMBED` | TBD | Operate | Native deploy view ships. |

### Duplicate-builder routes redirected to Studio

| Legacy path | Console path | Disposition | Replacement ticket | Owner | Retirement gate |
| --- | --- | --- | --- | --- | --- |
| `/operator/app-builder` | `/studio/apps` | `REDIRECT-TO-STUDIO` | honua-console#5 | Studio | Studio app-builder ships and parity smoke passes; legacy page is marked "Reference / experimental" today (`Pages/Operator/AppBuilder.razor:14-17`). |
| `/operator/spec` | `/studio/spec` | `REDIRECT-TO-STUDIO` | honua-console#5 | Studio | Studio spec workspace ships. |
| `/operator/sql` | `/studio/sql` (target TBD) | `REDIRECT-TO-STUDIO` | honua-console#5 | Studio | Studio SQL surface decision lands (see Open Questions); until then, Console renders the `moved-to-studio` landing for this path. |
| `/operator/annotations` | `/studio/maps` (target TBD) | `REDIRECT-TO-STUDIO` | honua-console#5 | Studio | Studio annotation surface lands (see Open Questions). |

Until Studio ports complete (honua-console#5), `REDIRECT-TO-STUDIO` rows resolve to a Console `moved-to-studio` landing that:

- Explains why the path moved.
- Links to the Studio target if available.
- Provides a single explicit "Open the legacy reference" affordance that opens the legacy route in `/operate/legacy/<path>` (gated by `canSeeOperate`).

These rows are absent from Operate navigation.

### Routes flagged for retirement

| Legacy path | Console disposition | Owner | Retirement gate |
| --- | --- | --- | --- |
| `/operator/open-data` | `EMBED` now, `RETIRE` from Operate once Catalog/Share own Open Data | Share / Catalog | Catalog/Share open-data surface ships (honua-console#4) and is the only entry point. Until then the embed remains reachable via direct URL but is not listed in Operate nav. |
| `/operator/admin-readiness` | `EMBED` now, `RETIRE` once `/operate/health` exists | Operate | Native readiness view in `/operate/health`. |
| `/server-info` | `EMBED` now, `RETIRE` once `/operate/health` exists | Operate | Same as above. |

## Console-Side Surfaces This Ticket Establishes

The shapes below are the contract that scaffold and IA tickets implement. They live in this repo once `honua-console#2` lands; this ticket commits to the surface names so downstream code can plug in without rediscovery.

- `src/operate/OperateLanding.tsx` — native React landing for `/operate`. Renders the Operate sub-section index.
- `src/operate/OperateLegacyEmbed.tsx` — single iframe host for `/operate/legacy/*`. Implements the [embed contract](../operate/embed-contract.md).
- `src/operate/OperateRedirect.tsx` — single component used by `REDIRECT-TO-STUDIO` rows. Reads target from disposition data, falls back to the `moved-to-studio` landing.
- `src/operate/movedToStudio/MovedToStudioLanding.tsx` — fallback target for redirects before Studio ports land.
- `src/operate/dispositionData.ts` — typed projection of this document used by router wiring. The source of truth for the table is this Markdown; the `.ts` projection is a generated/maintained mirror, not a divergent copy.
- `src/auth/canSeeOperate.ts` — single RBAC predicate Operate routes use. Sourced from `canSeeOperatorLinks` (scopes `operator` / `admin`) per design.

## Error And Empty-State Surfaces

All four surfaces are required and must use the shared Console primitives that `honua-console#2` / `#3` establish. They are listed here so the embed component and router know which slot to render.

| Condition | Surface | Owner |
| --- | --- | --- |
| Unknown `/operate/legacy/<path>` | Console `NotFound` | Console |
| Authenticated user lacks Operate scope | Console `Forbidden` | Console |
| Iframe fails to load (network, frame-ancestors deny, base-href misconfig) | Console `EmptyState` with retry + direct legacy-URL fallback (when same-origin precondition is unmet) | Console |
| Legacy returns unsupported service metadata or unsupported package binding | Owned by legacy (Console only owns the frame) | Legacy |

## Operate Navigation Contract

`honua-console#3` owns the navigation definition. This ticket commits to the section taxonomy it must expose:

- **Publishing** → `/operate/legacy/publishing`
- **Services & Layers** → `/operate/legacy/services` (plus children)
- **Connections** → `/operate/legacy/connections` and `/operate/legacy/connection-registry`
- **Data connections** → `/operate/legacy/data-connections`
- **Identity** → `/operate/legacy/identity/*`
- **Observability** → `/operate/legacy/observability`
- **Deploy** → `/operate/legacy/deploy`
- **GitOps** → `/operate/legacy/gitops`
- **Operations** → `/operate/legacy/operations`
- **Control Center** → `/operate/legacy/control-center`
- **License** → `/operate/legacy/license`
- **Print** → `/operate/legacy/print`
- **Analytics** → `/operate/legacy/analytics`

Duplicate-builder routes (`app-builder`, `spec`, `sql`, `annotations`) are not exposed in Operate navigation.

`open-data`, `admin-readiness`, and `server-info` are reachable via direct URL while still under `EMBED`, but `admin-readiness` and `server-info` are not enumerated in nav once `/operate/health` exists, and `open-data` is not enumerated in Operate nav at all (it lives in Catalog/Share nav).

## Telemetry And Smoke Evidence

Per the project's telemetry constraint, a Console Operate smoke must:

1. Sign in as an operator-scoped user.
2. Visit `/operate` and confirm the landing renders.
3. Visit `/operate/legacy/publishing` and confirm the embed loads (or the documented degraded surface, if the same-origin precondition is unmet).
4. Visit `/operator/app-builder` and confirm the redirect lands on the Studio target or the `moved-to-studio` page.
5. Sign in as a non-operator user, visit `/operate`, and confirm the `Forbidden` surface renders.

The smoke is owned by this ticket but lands physically once `honua-console#2` has the Playwright harness in place.

## Open Questions

Tracked here so #2/#3 implementers and Studio (#5) port owners see the open decisions:

1. `/operator/annotations` target — Studio map canvas, Catalog item-level annotations, or remain `EMBED`? Owner: Studio / Catalog joint call.
2. `/operator/sql` target — Studio surface, retire entirely in favor of NL query, or keep `EMBED`? Owner: Studio.
3. Embed mount path — confirmed `/operate/legacy/<verbatim-legacy-path>` here; revisit only if `honua-server-admin#96` requires otherwise.
4. Iframe vs. true reverse proxy — this disposition assumes iframe-in-React (Console chrome wraps the legacy surface). True reverse proxy is a fallback; revisit if `honua-devops#55` cannot embed.
5. Pre-`honua-devops#55` posture — the embed contract documents the degraded `link-out` mode. Operators reach legacy via direct origin until single-artifact lands.
6. Retirement gate refinement — current bar is "replacement ships AND parity smoke passes." Per-route owner sign-off MAY be added; flag a row here if so.

## How This Document Stays Current

- Every Console ticket that redesigns an operator workflow MUST update the corresponding row's disposition, replacement ticket, and retirement gate in the same PR.
- Every `honua-server-admin` change that adds or removes a legacy route MUST update the inventory in `honua-server-admin#96` and surface a PR here to keep this table aligned.
- The `HONUA_CONSOLE_MIGRATION_BACKLOG.md` parity gate links to this doc; the gate cannot close while any `EMBED` row remains.
