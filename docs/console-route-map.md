# Honua Console Route Map, RBAC, and Navigation

Status: filed 2026-05-23 for `honua-console#3`.

Decision sources:

- [ADR-0001: Unified Honua Console Runtime](./adr/0001-unified-honua-console-runtime.md)
- [Honua Console Migration Backlog](./roadmap/HONUA_CONSOLE_MIGRATION_BACKLOG.md)

This document fixes the Honua Console information architecture before any
feature ports start. Migration tickets `honua-console#4`, `#5`, `#6`, `#7`,
and `#9` cite specific sections of this map for their URL shapes, gates,
empty states, and smoke evidence. The intent is that no migration ticket
re-invents IA, RBAC bindings, or exception surfaces.

The artifact is internal to `honua-console`. It does not edit
`honua-server`, `honua-sdk-dotnet`, `honua-sdk-js`,
`honua-server-admin`, or `honua-devops`. Cross-repo consumers are listed
in §11.

---

## 1. Console Route Taxonomy

Four top-level workflow areas (per ADR-0001), plus a small set of shell
routes. Path prefixes are frozen for downstream tickets:

```
/                              Workspace dashboard (post-auth landing)
/auth/signin                   Sign in (anonymous; returnTo, as)
/auth/callback                 OIDC callback (anonymous)
/auth/signed-out               Post-signout landing (anonymous)

/studio                        Studio entry (AI-assisted creation)
/studio/proof                  Legacy alias for current proof flow
/studio/apps/:itemId/preview   Generated-app preview / publish lifecycle

/catalog                       Search / list (q, type, kind, visibility, tag, owner, sort, cursor)
/catalog/:idOrSlug             Catalog item detail

/maps/:mapId                   Interactive map viewer (Catalog/Studio/Share target)
/maps/new                      New-map flow (from=:itemId)

/operate                       Operator landing (operator scope only)
/operate/connections           Data connections list
/operate/connections/new       Create
/operate/connections/:id       Detail
/operate/connections/:id/diagnostics
/operate/publishing            Publishing workspace
/operate/identity/providers
/operate/identity/status
/operate/identity/diagnostics
/operate/license               License + entitlement workspace
/operate/observability         Top-level observability tile
/operate/operations            Operations console
/operate/control-center        Control center
/operate/services/:name/settings
/operate/layers/:id            Layer configuration (default + ?tab=configure)
/operate/layers/:id/style      Layer style editor
/operate/deploy                Deploy control
/operate/server-info           Server info
/operate/analytics             Usage analytics
/operate/groups                Workspace group management (moved from Portal /groups)
/operate/legacy/<path>         Transitional iframe container for legacy Admin pages

/share/public                  Open-data collection page
/share/public/items/:idOrSlug  Open-data item page (DCAT-US / schema.org JSON-LD)

/embed/maps/:mapId             Iframe-safe public embed (anonymous-capable, no shell chrome)

*                              404 NotFound
```

Top-level placement notes:

- `/maps/:mapId` and `/embed/maps/:mapId` remain top-level because the
  viewer is a shared target of Catalog, Studio, and Share, and the embed
  URL is the contract surface that external sites already use. Forcing
  them under `/catalog` or `/studio` would either invent two list
  surfaces or break embed deep-links.
- `/operate/legacy/<path>` is a transitional container that mirrors the
  legacy `@page` path one-to-one (see §5, EMBED rows). This survives
  existing deep-links from internal docs and runbooks.

---

## 2. Navigation Groups, Edition Gates, and Feature Flags

### 2.1 Primary navigation (signed-in)

| Group | Visible when | Default route |
|---|---|---|
| Studio | always (signed-in) | `/studio` |
| Catalog | always (signed-in) | `/catalog` |
| Share | always (signed-in); empty state when user has no shareable items | `/share/public` |
| Operate | `canSeeOperatorLinks(session)` returns true | `/operate` |
| Workspace switcher | always (signed-in); driven by `session.workspace` | n/a (menu) |

Operate visibility uses the same seam as current Portal nav
(`honua-portal:src/auth/permissions.ts:23` →
`canSeeOperatorLinks(session)`). Switching workspaces re-evaluates the
gate, so an admin of workspace A who is only a `member` in workspace B
will not see Operate while B is active. Confirmed default (§13, Q4).

### 2.2 Secondary navigation (within Operate)

Connections, Publishing, Identity, License, Observability, Operations,
Control Center, Services, Layers, Deploy, Server Info, Analytics,
Groups, Legacy.

The Legacy submenu contains the transitional iframe-embedded pages from
§5 (EMBED rows) and disappears when those pages are retired.

### 2.3 Edition gates

Edition gates render as a per-route badge or upgrade tile (not a hidden
route). Anonymous deep links to gated routes resolve to a clear upgrade
surface, not a 404. Edition is sourced from
`LicenseInfo.Edition` (HTTP DTO of `HonuaEdition` from
`honua-server:src/Honua.Core/Features/Licensing/Domain/LicenseModels.cs:9`).

Edition gate values:

- `edition:any` — no edition constraint (default).
- `edition:Pro` — requires `LicenseInfo.Edition` ≥ Pro.
- `edition:Enterprise` — requires `LicenseInfo.Edition` ≥ Enterprise.

### 2.4 Feature flag (entitlement) gates

Feature gates reference the verbatim key strings in
`honua-server:src/Honua.Core/Features/Licensing/Domain/FeatureCatalog.cs`.
Per-route gates use the syntax `entitlement:<key>` (e.g.
`entitlement:identity.oidc`). Feature gates are silent for users without
the entitlement on screens that have a non-gated fallback; on routes
that exist only because the entitlement is held, the route uses the same
upgrade-tile surface as the edition gate.

Keys cited by the route map below (all verified in `FeatureCatalog.cs`):

- `identity.oidc`, `identity.claims-mapping`
- `alerts.dwell`, `channels.slack`
- `geocoding.batch`, `import.geoserver`
- `streaming.feature-subscriptions`, `staticmap.high-dpi`
- families: `analytics.*`, `temporal.*`, `raster.*`

### 2.5 Anonymous routes

`/auth/signin`, `/auth/callback`, `/auth/signed-out`, `/share/public`,
`/share/public/items/:idOrSlug`, `/embed/maps/:mapId`, and `*` (NotFound)
are anonymous-capable. The Share and Embed routes still resolve through
`ShareAccess` and `resolveEmbedAuthorization` (§4) — anonymous-capable
does not mean unauthenticated reads always succeed.

---

## 3. Portal → Console Destination Map

Every current `honua-portal` route appears here. The Portal inventory is
16 paths declared in `honua-portal:src/router.tsx`.

| # | Portal path | Console destination | Query / fragment contract | Gate | Code-split chunk | Smoke label |
|---|---|---|---|---|---|---|
| 1 | `/` | `/` | — | `auth` | shell | — |
| 2 | `/auth/signin` | `/auth/signin` | `returnTo=<sanitized>`, `as=<preset>` (dev) | `anonymous` | shell+auth | — |
| 3 | `/auth/callback` | `/auth/callback` | — | `anonymous` | shell+auth | — |
| 4 | `/auth/signed-out` | `/auth/signed-out` | — | `anonymous` | shell+auth | — |
| 5 | `/public` | `/share/public` | — | `anonymous` | share | open-data |
| 6 | `/public/items/:idOrSlug` | `/share/public/items/:idOrSlug` | preserves DCAT-US / schema.org JSON-LD body | `anonymous` (+ `ShareAccess`) | share | open-data, share |
| 7 | `/embed/maps/:mapId` | `/embed/maps/:mapId` | `chrome`, `legend`, `zoom`, `extent=W,S,E,N` (WGS84 lon/lat); fragment `#embedToken=` | `anonymous` (+ `resolveEmbedAuthorization`) | embed | embed |
| 8 | `/catalog` | `/catalog` | `q`, `type`, `kind`, `visibility`, `tag`, `owner`, `sort`, `cursor` | `auth` | catalog | catalog-list |
| 9 | `/catalog/:idOrSlug` | `/catalog/:idOrSlug` | — | `auth` (+ `resolvePortalItemRole` for actions) | catalog | catalog-detail |
| 10 | `/maps` | `/catalog?type=map` (list) + `/studio` (create CTA) | preserves `from=:itemId` on the create path | `auth` | catalog (list), studio (create) | — |
| 11 | `/maps/:mapId` | `/maps/:mapId` | `from=:itemId` (when transiting from Catalog) | `auth` or `ShareAccess` (`item-role:viewer`) | viewer | viewer |
| 12 | `/data` | `/catalog?kind=dataset` | placeholder route removed; Catalog filters cover datasets, layers, and tables | `auth` | catalog | catalog-list |
| 13 | `/groups` | `/operate/groups` | member-facing group sharing remains in item share controls; workspace group management moves to Operate | `auth` (+ `canSeeOperatorLinks`) | operate | — |
| 14 | `/app-builder/proof` | `/studio` (entry) + `/studio/proof` (legacy alias) | legacy 301 → `/studio?source=…&itemId=…` | `auth` | studio | studio-generation |
| 15 | `/apps/:itemId/preview` | `/studio/apps/:itemId/preview` | preserves `?revision=<n>` | `auth` (+ `item-role:viewer`) | studio | studio-generation |
| 16 | `*` | `*` (Console NotFound) | — | `anonymous` | shell | — |

**Redirects.** Old `/public`, `/public/items/:idOrSlug`, `/app-builder/proof`,
`/maps` (list), `/data`, `/groups`, and `/apps/:itemId/preview` serve a
301 to their Console destination once Console is live. The exception is
`/embed/maps/:mapId` and any already-issued `/share/public/items/...` URL
with an external crawler audience: these are served at the legacy path
with a 200 (see §8, Frozen URLs).

---

## 4. RBAC and Entitlement Reference

All gates in this document reference names that already exist in
`honua-portal`, `honua-server`, `honua-sdk-dotnet`, or `honua-sdk-js`. No new
frontend-invented permission types appear here. File pointers below were
verified during the route-map pass.

### 4.1 Session and scope

Defined in `honua-portal:src/auth/types.ts` and consumed via
`honua-portal:src/auth/permissions.ts`:

- `Session = LoadingSession | UnauthenticatedSession | AuthenticatedSession | ErroredSession`
  (`auth/types.ts:9–47`).
- `Scope = "member" | "operator" | "admin" | (string & {})` (`auth/types.ts:9`).
- `isAuthenticated(session)` (`auth/permissions.ts:5`).
- `hasScope(session, scope)` (`auth/permissions.ts:9`),
  `hasAnyScope(session, scopes)` (`auth/permissions.ts:13`).
- `canSeeOperatorLinks(session)` (`auth/permissions.ts:23`) — the
  canonical operator gate, used by Portal nav and the
  Console primary nav (§2.1).
- `sanitizeReturnTo(value)` (`auth/returnTo.ts:5`) — guards the
  `returnTo=` query of `/auth/signin`.

### 4.2 Per-item permissions

Defined in `honua-portal:src/share/rbac.ts`:

- `PortalItemRole = "owner" | "editor" | "viewer"` (`share/rbac.ts:5`).
- `resolvePortalItemRole(session, item)` (`share/rbac.ts:66`).
- `ROLE_MATRIX` (`share/rbac.ts:22`) for the per-action gates on
  `/catalog/:idOrSlug` (read, editMetadata, updateSharing, inviteEditors,
  inviteViewers, revokeAccess).

### 4.3 Sharing and embed

Defined in `honua-portal:src/share/` and `honua-portal:src/embed/`:

- `SharingTier = "private" | "org" | "group" | "public-link" | "public"`
  (`share/types.ts:14`).
- `ShareAccess` (`share/types.ts:22`) — sharing tier, embeddability,
  group ids, public-link token.
- `evaluateShareEscalation(...)` (`share/policy.ts:45`) — client parity
  with the server 409 closure-violation contract.
- `resolveEmbedAuthorization(...)` (`embed/permissions.ts:68`) — the
  read decision for `/embed/maps/:mapId`.

### 4.4 Edition and entitlement

Server-authored, consumed by Console via the `LicenseInfo` DTO:

- `HonuaEdition` enum: `Community`, `Pro`, `Enterprise`
  (`honua-server:src/Honua.Core/Features/Licensing/Domain/LicenseModels.cs:9`,
  values at lines 14, 19, 24).
- `LicenseEntitlementDecision` with `UpgradeMessage`
  (`LicenseModels.cs:141`, `UpgradeMessage` at line 147).
- `ILicenseEntitlementService.GetSnapshot()` and
  `CheckEntitlement(entitlementKey)`
  (`honua-server:src/Honua.Core/Features/Licensing/Abstractions/ILicenseEntitlementService.cs:11/17/24`).
- `LicenseInfo` HTTP DTO with `Edition`, `IsValid`, `ExpiresAt`,
  `ValidationState`, `Entitlements[].Key/Name/IsActive`
  (`honua-server:src/Honua.Core/Features/Licensing/Domain/LicenseInfo.cs:9/14/19/24/29/49`;
  `Entitlement` at line 55, fields at 60/65/70).
- Feature keys: `honua-server:src/Honua.Core/Features/Licensing/Domain/FeatureCatalog.cs`,
  individual keys at the line numbers cited in §2.4.

### 4.5 SDK control-plane

Already in `honua-sdk-js:src/control-plane/types.ts`:

- `HONUA_CONTROL_PLANE_BASE_PATH = "/api/v1/admin"` (line 6).
- `HonuaControlPlaneCapability` (line 8): `hosted-maps`, `map-packages`,
  `imports`, `api-tokens`, `workspaces`, `connections`, `sharing`, `raw`.
- `HonuaShareRequest` (line 227), `HonuaShareResponse` (line 236).

`honua-sdk-dotnet#166` projects the equivalent server-owned Console
client contracts for the Blazor Web shell and optional MAUI host. The JS
SDK remains the browser/runtime contract for generated apps, embeds,
MCP/QGIS/browser integrations, and map/chart/editor interop.

### 4.6 Gate column grammar

Each route row in this document uses the following gate vocabulary. A
route may have multiple gates; all must pass.

| Token | Meaning |
|---|---|
| `anonymous` | No session required. The route is reachable without sign-in. |
| `auth` | `isAuthenticated(session)` must be true. |
| `scope:<name>` | `hasScope(session, <name>)`, e.g. `scope:operator`. |
| `item-role:<min>` | `resolvePortalItemRole(session, item)` ≥ `<min>` per `ROLE_MATRIX`. |
| `share-tier:<min>` | `ShareAccess.sharing` ≥ `<min>`. |
| `entitlement:<key>` | `ILicenseEntitlementService.CheckEntitlement(<key>)` is granted. |
| `edition:<tier>` | `LicenseInfo.Edition` ≥ `<tier>`. |

Console must implement these as one `<RouteGuard>` consuming the named
contracts; the guard surface is the seam called out in `honua-console#2`.

### 4.7 SDK projection timing (default)

Several helpers (`resolvePortalItemRole`, `evaluateShareEscalation`,
`resolveEmbedAuthorization`) live in `honua-portal` and have not yet
been projected to shared SDK contracts. Default decision (§13, Q7):
the Blazor Web Console treats the Portal helpers as behavior references
until `honua-sdk-dotnet#166` projects the server-owned route/RBAC DTOs;
generated apps, embeds, MCP/QGIS/browser integrations, and map/chart
interop use `honua-sdk-js#225` once it projects the equivalent JS
contracts. Portal is on the retirement path (`honua-console#10`), so the
dual-reference window is bounded.

---

## 5. Admin → Disposition Map

Every `@page` declared in
`honua-server-admin/src/Honua.Admin/Pages` appears here. The inventory
is 41 unique paths across 31 `.razor` files (some files declare
multi-route aliases). Disposition values:

- **REDIRECT** — Console covers it now or imminently. Admin path serves
  a 301 to the Console destination via the `honua-console#6` transitional
  Operate embed once Console is live.
- **REBUILD** — Console Operate is the primary surface. Trigger: Admin
  REDIRECTs to Console once the Console page reaches behavior parity.
- **EMBED** — Transitional iframe inside Console at
  `/operate/legacy/<path>` (shared session). Triggered by
  `honua-console#6`.
- **KEEP** — No Console replacement planned in this milestone. Reached
  via the EMBED surface; "KEEP" annotates "do not rebuild now."
- **RETIRE** — Removed from Admin; not linked from Console.

The "trigger ticket" column names the Console-side ticket that lands
the disposition; "cut-over signal" is the observable event that flips
state.

### 5.1 Root

| Admin path | Disposition | Console target | Trigger | Cut-over signal |
|---|---|---|---|---|
| `/` | REDIRECT | `/` | #6 | Console `/` reachable in same session |
| `/deploy` | REBUILD | `/operate/deploy` | #6, #7 | Deploy parity in Console; Admin 301s |
| `/server-info` | REBUILD | `/operate/server-info` | #6 | Server-info parity in Console; Admin 301s |
| `/observability` | REDIRECT | `/operate/observability` | #6 | Top tile reachable in Console |

### 5.2 Operator

| Admin path | Disposition | Console target | Trigger | Cut-over signal |
|---|---|---|---|---|
| `/operator/admin-readiness` | EMBED · KEEP | `/operate/legacy/operator/admin-readiness` | #6 | iframe container live |
| `/operator/annotations` | EMBED · KEEP | `/operate/legacy/operator/annotations` | #6 | iframe container live |
| `/operator/app-builder` | RETIRE | — (Studio is the surface) | #5 | Studio `/studio` covers proof flow |
| `/operator/control-center` | REDIRECT | `/operate/control-center` | #6 | Control center reachable in Console |
| `/operator/data-connections` | REBUILD | `/operate/connections` | #6, #7 | List parity |
| `/operator/data-connections/new` | REBUILD | `/operate/connections/new` | #6, #7 | Create parity |
| `/operator/data-connections/{id:guid}` | REBUILD | `/operate/connections/:id` | #6, #7 | Detail parity |
| `/operator/data-connections/{id:guid}/diagnostics` | REBUILD | `/operate/connections/:id/diagnostics` | #6, #7 | Diagnostics parity |
| `/operator/license` | REBUILD | `/operate/license` | #6, #7 | License workspace parity |
| `/operator/open-data` | EMBED · KEEP | `/operate/legacy/operator/open-data` | #6 | iframe container live |
| `/operator/operations` | REDIRECT | `/operate/operations` | #6 | Operations reachable in Console |
| `/operator/print` | EMBED · KEEP | `/operate/legacy/operator/print` | #6 | iframe container live |
| `/operator/publishing` | REBUILD | `/operate/publishing` | #6, #7 | Publishing workflow parity |
| `/operator/sql` | EMBED · KEEP | `/operate/legacy/operator/sql` | #6 | iframe container live |
| `/operator/spec` | EMBED · KEEP | `/operate/legacy/operator/spec` | #6 | iframe container live |
| `/operator/analytics` | REBUILD | `/operate/analytics` | #6, #7 | Analytics parity |

### 5.3 Admin

| Admin path | Disposition | Console target | Trigger | Cut-over signal |
|---|---|---|---|---|
| `/admin/connection-registry` | RETIRE | — (superseded by `/operate/connections`) | #6 | `/operate/connections` reachable |
| `/admin/connection-registry/{ConnectionId}` | RETIRE | — | #6 | `/operate/connections/:id` reachable |
| `/admin/connection-registry/new` | RETIRE | — | #6 | `/operate/connections/new` reachable |
| `/admin/gitops` | EMBED | `/operate/legacy/admin/gitops` | #6 | iframe container live |
| `/admin/identity/api-keys` | RETIRE | — (stub pending `honua-server#969`) | #6 | tracked in `honua-server#969` |
| `/admin/identity/diagnostics` | REBUILD | `/operate/identity/diagnostics` | #6, #7 | Diagnostics parity |
| `/admin/identity/providers` | REBUILD | `/operate/identity/providers` | #6, #7 | Providers parity |
| `/admin/identity/status` | REBUILD | `/operate/identity/status` | #6, #7 | Status parity |

### 5.4 Layers and Services

| Admin path | Disposition | Console target | Trigger | Cut-over signal |
|---|---|---|---|---|
| `/layers` | REDIRECT | `/catalog?type=layer` | #4, #6 | Catalog filter covers list |
| `/layers/{LayerId:int}` | REBUILD | `/operate/layers/:id` | #6, #7 | Configure parity (default tab) |
| `/layers/{LayerId:int}/configure` | REBUILD | `/operate/layers/:id` (`?tab=configure`) | #6, #7 | Configure parity (multi-route alias) |
| `/layers/{LayerId:int}/preview` | REDIRECT | `/maps/:mapId` (via layer→map projection) | #4, #6 | Console viewer renders layer preview |
| `/layers/{LayerId:int}/style` | REBUILD | `/operate/layers/:id/style` | #6, #7 | Style editor parity |
| `/services` | REDIRECT | `/catalog?type=service` | #4, #6 | Catalog filter covers list |
| `/services/{ServiceName}/settings` | REBUILD | `/operate/services/:name/settings` | #6, #7 | Settings parity |
| `/services/{ServiceName}/layers/{LayerId:int}/preview` | REDIRECT | `/maps/:mapId` | #4, #6 | Console viewer (multi-route alias of above) |

### 5.5 Legacy redirect-only

| Admin path | Disposition | Console target | Trigger | Cut-over signal |
|---|---|---|---|---|
| `/connections` | RETIRE | — | #6 | `/operate/connections` reachable |
| `/connections/new` | RETIRE | — | #6 | `/operate/connections/new` reachable |
| `/connections/{ConnectionId:guid}` | RETIRE | — | #6 | `/operate/connections/:id` reachable |
| `/connections/{ConnectionId}/layers` | RETIRE | — | #6 | Catalog layer list / Operate layers cover it |
| `/connections/{ConnectionId}/publish` | RETIRE | — (low-traffic legacy) | #6 | Operate publishing covers it |

### 5.6 Disposition totals

| Disposition | Count | Notes |
|---|---|---|
| REDIRECT | 8 | Admin 301 → Console once parity lands |
| REBUILD | 16 | Console Operate primary surface |
| EMBED | 7 | iframe under `/operate/legacy/*` (6 of these are also KEEP) |
| KEEP | 6 | Subset of EMBED; no rebuild in current backlog |
| RETIRE | 10 | Removed from Admin; not linked from Console |
| **Total** | **41** | All `@page` paths covered exactly once |

---

## 6. Console Route Catalogue (Gates and Surfaces)

This is the per-route catalogue Console must implement. Each row names
the gate, the canonical empty/forbidden surface, the code-split chunk,
and the smoke label (see §10) when applicable.

### 6.1 Shell and Auth

| Route | Gates | Empty surface | Forbidden surface | Chunk |
|---|---|---|---|---|
| `/` | `auth` | empty-workspace | unauth-redirect | shell |
| `/auth/signin` | `anonymous` | — | — | shell+auth |
| `/auth/callback` | `anonymous` | — | session-error | shell+auth |
| `/auth/signed-out` | `anonymous` | — | — | shell+auth |
| `*` | `anonymous` | notfound | — | shell |

### 6.2 Studio

| Route | Gates | Empty | Forbidden | Chunk |
|---|---|---|---|---|
| `/studio` | `auth` | empty-studio (start a prompt) | unauth-redirect | studio |
| `/studio/proof` | `auth` | empty-studio | unauth-redirect | studio |
| `/studio/apps/:itemId/preview` | `auth`, `item-role:viewer` | missing-item | forbidden / unsupported-package | studio |

### 6.3 Catalog

| Route | Gates | Empty | Forbidden | Chunk |
|---|---|---|---|---|
| `/catalog` | `auth` | empty-catalog (start a search / publish first item) | unauth-redirect | catalog |
| `/catalog/:idOrSlug` | `auth`, `item-role:viewer` | missing-item | forbidden / unsupported-service | catalog |

### 6.4 Maps (viewer)

| Route | Gates | Empty | Forbidden | Chunk |
|---|---|---|---|---|
| `/maps/:mapId` | `auth` or `ShareAccess`; `item-role:viewer` | missing-item | forbidden | viewer |
| `/maps/new` | `auth` | empty-studio (redirect to `/studio?from=`) | unauth-redirect | viewer + studio |

### 6.5 Operate

All Operate routes require `auth` and `canSeeOperatorLinks(session)`
(scope `operator` or higher). Per-route gates listed below add edition
or entitlement requirements on top.

| Route | Additional gates | Empty | Forbidden | Chunk |
|---|---|---|---|---|
| `/operate` | — | empty-operate | forbidden (operator) | operate |
| `/operate/connections` | — | empty-operate (no connections) | forbidden | operate |
| `/operate/connections/new` | — | — | forbidden | operate |
| `/operate/connections/:id` | — | missing-item | forbidden | operate |
| `/operate/connections/:id/diagnostics` | — | missing-item | forbidden | operate |
| `/operate/publishing` | — | empty-operate | forbidden | operate |
| `/operate/identity/providers` | `entitlement:identity.oidc` (gate the OIDC provider) | empty-operate | forbidden / upgrade | operate |
| `/operate/identity/status` | — | empty-operate | forbidden | operate |
| `/operate/identity/diagnostics` | `entitlement:identity.claims-mapping` (gate claims tab) | — | forbidden / upgrade | operate |
| `/operate/license` | — | — | forbidden | operate |
| `/operate/observability` | — | empty-operate | forbidden | operate |
| `/operate/operations` | — | empty-operate | forbidden | operate |
| `/operate/control-center` | — | empty-operate | forbidden | operate |
| `/operate/services/:name/settings` | — | missing-item | forbidden | operate |
| `/operate/layers/:id` | — | missing-item | forbidden | operate |
| `/operate/layers/:id/style` | — | missing-item | forbidden | operate |
| `/operate/deploy` | — | empty-operate | forbidden | operate |
| `/operate/server-info` | — | — | forbidden | operate |
| `/operate/analytics` | `edition:Pro`, `entitlement:analytics.*` (per tab) | empty-operate | forbidden / upgrade | operate |
| `/operate/groups` | — | empty-operate | forbidden | operate |
| `/operate/legacy/<path>` | — | — | forbidden | operate |

Other entitlement gates that surface inside Operate sub-pages but do
not own a top-level route (so they appear as in-page upgrade tiles
rather than route gates): `alerts.dwell`, `channels.slack`,
`geocoding.batch`, `import.geoserver`, `streaming.feature-subscriptions`,
`staticmap.high-dpi`, `temporal.*`, `raster.*`.

### 6.6 Share

| Route | Gates | Empty | Forbidden | Chunk |
|---|---|---|---|---|
| `/share/public` | `anonymous` (+ `ShareAccess.sharing = public` per item) | empty-share | — | share |
| `/share/public/items/:idOrSlug` | `anonymous` (+ `ShareAccess`) | missing-item | unavailable (anonymous, see §13 Q5) | share |

### 6.7 Embed

| Route | Gates | Empty | Forbidden | Chunk |
|---|---|---|---|---|
| `/embed/maps/:mapId` | `anonymous` (+ `resolveEmbedAuthorization`) | missing-item | unavailable | embed |

---

## 7. Exception Surfaces

One canonical component per category, referenced by route. Console
routes do not author bespoke 403/404/empty copy.

| Surface | Component | When | Source / contract |
|---|---|---|---|
| Unauthenticated | redirect | route requires `auth` and session is `UnauthenticatedSession` | `redirect('/auth/signin?returnTo=' + sanitizeReturnTo(location))` (`auth/returnTo.ts:5`) |
| Forbidden | `<ForbiddenView cause=...>` | gate denial (scope, item-role, share-tier, edition, entitlement) | failed gate token from §4.6; `entitlement:*` and `edition:*` render with `LicenseEntitlementDecision.UpgradeMessage` (`LicenseModels.cs:147`) |
| Missing item | `<MissingItemView kind=...>` | item id resolved → not found | item-kind hint: map, service, layer, app, dashboard, report |
| Unsupported service metadata | `<UnsupportedServiceView>` | service metadata schema not yet supported by Console (e.g. pre-Metadata v2) | shared between `/catalog/:idOrSlug` and Studio "open from catalog" |
| Unsupported package binding | `<UnsupportedPackageView>` | generated-app or saved-map package newer than Console runtime understands | used on `/studio/apps/:itemId/preview` and `/maps/:mapId` |
| Empty state | `<EmptyState area=...>` | list/query returned zero rows | per-area copy + CTA; areas: catalog, studio, share, operate, workspace |
| Loading | `<SectionSkeleton>` | route mounted, content pending | never blocks shell paint |
| Errored session | `<SessionErrorView retry>` | session is `ErroredSession` | distinct from unauthenticated; renders diagnostic id |
| Unavailable (anonymous) | `<UnavailableView>` | anonymous viewer on `/share/*` for content that requires entitlement | does not reveal upgrade copy to anonymous users (§13 Q5) |

These components are seeded by `honua-console#2` (scaffold) and used by
all downstream port tickets.

---

## 8. Frozen URLs (Cannot 3xx)

External consumers (third-party sites, DCAT-US/data.json crawlers,
embed iframes inlined into customer pages) have already inlined some
Honua URLs and cannot be expected to follow redirects. Those URLs must
be served at both the legacy path and the Console path with a 200.

| URL | Reason | Status |
|---|---|---|
| `/embed/maps/:mapId` | inlined in third-party iframes | served at both legacy and Console path with a 200; path frozen; embed token stays in `#embedToken=` fragment so it is never sent to the server; `extent=W,S,E,N` is WGS84 lon/lat |
| Already-issued `/share/public/items/...` URLs in DCAT-US / data.json index | crawler-driven; some may not follow 3xx | served at both legacy and Console path with a 200 (defensive); new issuances point at `/share/public/items/:idOrSlug` only |
| `/public` (root open-data) | low external embed risk; crawlers follow 3xx | 301 → `/share/public` (§13 Q3 default) |
| `/public/items/:idOrSlug` | same | 301 → `/share/public/items/:idOrSlug` (§13 Q3 default) |

Edge config for the 200-at-both-paths behavior is flagged for
`honua-devops#55` and `honua-devops#56`; this document is the source of
truth for which URLs cannot 3xx.

---

## 9. Code-Splitting Boundaries

Console bundles split into independent chunks so that `/share/*` and
`/embed/maps/*` can paint without loading Studio or Operate. This
protects Console startup, catalog browse, map viewer, and generated-app
preview from avoidable network waterfalls (per the project performance
constraint).

| Chunk | Routes |
|---|---|
| shell + auth | `/`, `/auth/*`, `*` |
| studio | `/studio*`, `/studio/apps/:itemId/preview` |
| catalog | `/catalog`, `/catalog/:idOrSlug` |
| viewer | `/maps/:mapId`, `/maps/new` |
| operate | all `/operate/*` |
| share | `/share/public`, `/share/public/items/:idOrSlug` |
| embed | `/embed/maps/:mapId` |

The embed chunk explicitly excludes shell chrome, Studio prompt UI,
Operate workspace, and Catalog search UI. The share chunk excludes
Studio and Operate.

---

## 10. Smoke Evidence Map

The cross-surface smoke (`honua-console#9`) follows this quadruple:

1. **POST publish service** — operator publishes a service via
   `/operate/publishing` (or the equivalent SDK control-plane call
   under `HONUA_CONTROL_PLANE_BASE_PATH` =`/api/v1/admin`).
2. **`/catalog/:id`** — the published service appears as a catalog
   item; Catalog detail loads.
3. **`/studio`** — Studio prompt generates a map/dashboard/app from
   the catalog item; apply succeeds; preview renders.
4. **`/share/public/items/:id`** and **`/embed/maps/:mapId`** —
   share/embed surfaces serve the generated artifact; anonymous embed
   loads.

Route rows in §3 and §6 carry smoke labels matching this pipeline:
`catalog-list`, `catalog-detail`, `viewer`, `studio-generation`,
`open-data`, `share`, `embed`. Migration tickets `#4`, `#5`, and `#9`
must preserve evidence emission at each label.

No new instrumentation is added by `#3`; this section pins which rows
the smoke flow consumes so the labels survive feature ports.

---

## 11. Cross-Repo Dependency Callouts

This document is internal to `honua-console`. The following external
work consumes the route map but is not edited by `#3`:

| Repo / ticket | Consumer of |
|---|---|
| `honua-server#1162` | Metadata v2 / content / RBAC API baseline backing `/catalog/:idOrSlug` and Studio open-from-catalog. License snapshot DTO contract for `/operate/license` (avoid leaking `System.*` types through trimmer). |
| `honua-sdk-dotnet#166` | Projects the server-owned Console client contracts for the Blazor Web shell and optional MAUI host, including metadata/content/package, route guard, RBAC, license, transport, and environment-profile DTOs. Until landed, Portal helpers remain behavior references (§4.7). |
| `honua-sdk-js#225` | Projects `resolvePortalItemRole`, `evaluateShareEscalation`, `resolveEmbedAuthorization`, and metadata/content/package DTOs for generated apps, embeds, MCP/QGIS/browser integrations, and map/chart/editor interop. Until landed, Portal helpers remain behavior references (§4.7). |
| `honua-server-admin#96` | Prepare legacy Admin for the `/operate/legacy/*` iframe container; ensure session cookie domain and CSP frame-ancestors allow the Console origin. |
| `honua-devops#55` / `honua-devops#56` | Edge config that preserves the §8 frozen URLs at both paths with a 200; preview/release pipeline that bundles Console + Admin into one origin. |
| `honua-server#969` | Backs the `/admin/identity/api-keys` RETIRE decision. |

---

## 12. Migration Ticket Cross-Link Map

The following migration tickets cite specific sections of this map.
PRs that touch each ticket should link to its section number(s).

| Ticket | Cites |
|---|---|
| `honua-console#4` — Port Catalog, Viewer, Saved Maps, Share, Embed, Open Data | §1 (taxonomy: `/catalog`, `/maps`, `/share`, `/embed`), §3 rows 5–12, §5.4 REDIRECTs (`/layers`, `/services`, `/layers/{id}/preview`), §6.3 / §6.4 / §6.6 / §6.7 (route catalogue), §7 (exception surfaces), §8 (frozen URLs), §9 (chunks), §10 (smoke). |
| `honua-console#5` — Port Studio app-builder and generated-app lifecycle | §1 (`/studio*`), §3 rows 14–15, §5.2 RETIRE (`/operator/app-builder`), §6.2 (route catalogue), §7 (`<UnsupportedPackageView>`), §9 (studio chunk), §10 (studio-generation smoke). |
| `honua-console#6` — Integrate legacy Admin as transitional Operate surface | §1 (`/operate/*`, `/operate/legacy/<path>`), §3 row 13 (`/groups` → `/operate/groups`), all of §5 (Admin disposition map), §6.5 (Operate gates and surfaces), §11 (`honua-server-admin#96` consumer notes). |
| `honua-console#7` — Wire Console to shared metadata / content / package / RBAC contracts | §4 (full RBAC and entitlement reference), §6 per-route gates, §11 (`honua-server#1162`, `honua-sdk-dotnet#166`, `honua-sdk-js#225` consumer notes). |
| `honua-console#9` — Console parity smoke | §10 (smoke evidence map), §3 / §6 rows carrying smoke labels. |

`honua-console#2` (scaffold) seeds the `<RouteGuard>`, the exception
surface components in §7, the chunk boundaries in §9, and the redirect
behavior in §3 and §5.

---

## 13. Open IA Questions and Default Decisions

Each question from the design review carries a default decision so
migration tickets are not blocked. Defaults flip to "confirmed" once
the human review answers land in
`honua-agentflow:.agentflow/runtime/honua-console-3/answers.md`.

| # | Question | Default decision | Status |
|---|---|---|---|
| Q1 | Studio root path: `/studio` vs `/studio/proof` | `/studio` is the entry; `/studio/proof` is a legacy alias. Sub-routes (`/studio/maps`, `/studio/dashboards`, `/studio/reports`) added per generator in `#5`. | default |
| Q2 | Saved-map list: `/catalog?type=map` vs `/studio/maps` vs `/catalog/maps` | `/catalog?type=map` (Catalog filter). Avoids two list surfaces; preserves existing query-param contract. | default |
| Q3 | `/share/public` vs `/public` redirect semantics | 301 for `/public/*` (DCAT-US crawlers follow 301). 200-at-both-paths only for `/embed/*` and for specific already-issued `/share/public/items/...` URLs. | default |
| Q4 | `/operate` visibility for cross-workspace admins | `canSeeOperatorLinks(session)` evaluated against the active workspace; switching workspaces re-evaluates. | default |
| Q5 | Anonymous user on a gated `/share/*` link | Generic `<UnavailableView>` for anonymous; upgrade tile only when authenticated. | default |
| Q6 | Admin EMBED container path: `/operate/legacy/<path>` vs `/operate/admin-legacy/<opaque>` | One-to-one `/operate/legacy/<path>` to preserve deep-link history. | default |
| Q7 | SDK projection timing | Blazor Web Console treats Portal RBAC/embed helpers as behavior references until `honua-sdk-dotnet#166` projects server-owned route/RBAC DTOs; JS runtimes use `honua-sdk-js#225` once available. Bounded interim, not a cross-repo blocker. | default |
| Q8 | Cross-repo scope | This ticket changes only `honua-console`. No bounded child tickets filed beyond the existing external consumer tickets in §11. | confirmed |

---

## 14. Maintenance

This document is the source of truth for Console IA. When a Portal or
Admin route is added, removed, or repositioned, the corresponding
section here must be updated in the same PR. Migration tickets that
diverge from this map without first updating the map should be sent
back for revision.

When `honua-sdk-dotnet#166` and `honua-sdk-js#225` land, §4.7 and §11
update to reflect that Console consumes the shared SDK projections rather
than treating `honua-portal` helpers as behavior references. The Portal
source rows in §4 remain as a historical pointer until
`honua-console#10` retires Portal.
