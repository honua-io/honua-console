# Honua Console Route Map, RBAC, and Navigation

Status: filed 2026-05-23 for `honua-console#3`; reconciled
2026-05-24 for the catalog/share route slice in `honua-console#34`
and native host profile/trust routes in `honua-console#44`; reconciled
2026-05-26 for the server-bound Studio form route in `honua-console#57`;
reconciled 2026-05-30 for the server-bound catalog/content + RBAC binding in
`honua-console#7` (Catalog/Studio/Share/Operate-visible content metadata now
binds to honua-server's Console metadata v2 content + RBAC API,
`honua-server#1162`: `/api/v1/console/content` and `/api/v1/console/actions`,
gated on a configured server base URL, else the missing-binding state).

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
/auth/signin                   Sign in (anonymous; returnTo)
/auth/callback                 OIDC callback (anonymous)
/auth/signed-out               Post-signout landing (anonymous)

/studio                        Studio entry (AI-assisted creation)
/studio/query                  Generated query.package editor
/studio/analysis               Server-bound spatial analysis builder (honua-server#1182 closed; list + cost-estimate degraded until honua-server#1237)
/studio/map                    Generated map.package editor
/studio/dashboard              Generated dashboard.package editor
/studio/report                 Generated report.package editor
/studio/form                   Server-bound form package builder (honua-server#1184)
/studio/app                    Server-bound app.package builder (honua-server#1180/#1183)
/studio/proof                  Legacy alias for current proof flow
/studio/drafts                 Source-scoped draft start (source, id)
/studio/apps/:itemId/preview   Generated-app preview / publish lifecycle
/studio/workflows/new          New unified GP/ETL workflow package draft
/studio/workflows/:draftId     Reopen a workflow.package draft editor

/catalog                       Search / list (q, type, tag, owner, visibility, sort, cursor)
/catalog/:idOrSlug             Catalog item detail

/maps/:mapId                   Interactive map viewer (Catalog/Studio/Share target; anonymous-capable via ShareAccess + ?token=)
/maps/new                      New-map flow (from=:itemId)

/groups                        Workspace groups (authenticated; any scope — member, operator, or admin)

/operate                       Operator landing (operator scope only)
/operate/connections           Data connections list
/operate/connections/new       Create
/operate/connections/:id       Detail
/operate/connections/:id/diagnostics
/operate/resources             Data resources list and edit queue
/operate/resources/new         Create resource from table/file or one-time remote-service migration
/operate/resources/:id         Resource detail, validation, publish, access, presentation, and advanced tabs
/operate/publishing            Publishing workspace
/operate/identity/providers
/operate/identity/status
/operate/identity/diagnostics
/operate/license               License + entitlement workspace
/operate/observability         Top-level observability tile
/operate/events/:eventId       Event timeline detail deep link
/operate/alerts                Alerts list (firing, acknowledged, suppressed, resolved)
/operate/alerts/:alertId       Alert evidence detail deep link
/operate/alerts/rules          Realtime/geofence rule list
/operate/alerts/rules/:ruleId  Rule detail and condition builder
/operate/jobs/:jobRunId        Unified job-run detail deep link
/operate/operations            Operations console
/operate/control-center        Control center
/operate/services              Service list and layer explorer
/operate/services/:name/settings
/operate/layers                Flat layer list
/operate/layers/:id            Layer configuration (default + ?tab=configure)
/operate/layers/:id/style      Layer style editor
/operate/settings              Auth providers, API keys, CORS, license, server info, and catalog endpoints
/operate/catalogs              Catalogs · discovery-endpoints surface (Esri/OGC/OData/STAC/DCAT endpoint cards, auto-default-on vs opt-in, feeders, per-endpoint issues; bound to the catalog discovery-endpoints registry honua-server#1279, else missing-binding). DISTINCT from the singular content catalog at /catalog.
/operate/catalogs/:key         Catalogs · one discovery endpoint drill-down (Items/Settings/Access/Activity/Validation tabs + mirrored-items table; honua-server#1279)
/operate/catalogs/:key/items/:itemId  Catalogs · single catalog item editor (auto-mirror item: derived identity, catalog-only presentation, service bindings, standards mapping; honua-server#1279)
/operate/access                Access · roles & permissions (RBAC overview: scope hierarchy + role × permission matrix; bound to Console metadata/RBAC #1162)
/operate/access/members        Access · team members + scoped-invite drawer (bound to Console metadata/RBAC #1162)
/operate/releases              GitOps metadata releases (server has no list endpoint; open a release by package id)
/operate/releases/:id          Release detail: proposal/semantic diff, environment matrix + drift, Git PR preview, CI/GitOps timeline, rollback readiness (bound to honua-server release package #1163 + release-operation lifecycle #1165)
/operate/deploy                Deploy control (GitOps metadata release)
/operate/environments          Environment and fleet overview
/operate/environments/:id      Environment detail (drift, fleet tasks)
/operate/temporal              Temporal data viewer (capability-gated)
/operate/sync                  Disconnected sync conflict review (capability-gated)
/operate/versions              Branch-version manager (#177): list/create/alter/delete branch versions + reconcile with an auto-resolution policy (none/last-write-wins/version-wins/default-wins) + abort-if-conflicts; surfaces auto-resolved/remaining counts. Bound to honua-server GeoServices VersionManagementServer (#371 / PR #1551), else the missing-binding state.
/operate/versions/:service/:guid/conflicts  Conflict resolution (#177): per-feature 3-way diff (base vs DEFAULT vs version) for attributes + geometry (WKT) with per-field highlighting; take-version/take-default/take-base per feature → resolveConflicts; Post to DEFAULT disabled while conflicts remain.
/operate/import/esri           Import-from-Esri wizard (#102): Source → Select content → Map → Run → Scorecard (?step=0..4). REUSES PublishWizard. Map step shows deterministic per-item conversion fidelity; Run + Scorecard bind the honua-devops migration-run API, else the missing-binding state (Console Patterns Charter §11). Content imports — DISTINCT from the data-layer→PostGIS importer.
/operate/import/esri/web-map   Import Esri Web Map JSON → honua.map-package.v1 (#100): paste/upload/URL/connected-ArcGIS intake, layer→layer mapping with per-layer fidelity badge, MapPreview target, Create map package CTA → /operate/publishing, inline missing-binding banner when a layer has no resource
/operate/import/esri/dashboard Import Esri Dashboard JSON → dashboard package (#101): element→widget mapping grid with supported/unsupported callouts, target layout preview, create CTA
/operate/import/esri/storymap  Import StoryMap / Hub → report content (#104, P4): section→content-block mapping with fidelity badges, target content preview, create CTA
/operate/server-info           Server info
/operate/analytics             Usage analytics
/operate/events                Event evidence view (?jobId=<id>)
/operate/native-stream         Native gRPC streaming proof (shared route; native service optional)
/operate/legacy/<path>         Transitional iframe container for legacy Admin pages

/environments                  Native-ready environment profile list
/environments/new              Add environment profile (native host only)
/environments/:profileId       Environment profile diagnostics

/share                         Share area entry; current slice renders the public open-data collection
/share/public                  Open-data collection page (public + openData service/layer/document items)
/public                        Legacy open-data collection alias; newly generated links use /share/public
/share/public/items/:idOrSlug  Open-data item page (same eligibility)
/public/items/:idOrSlug        Legacy open-data item alias; same eligibility as /share/public/items/:idOrSlug

/embed/maps/:mapId             Iframe-safe public embed (anonymous-capable, no shell chrome)

*                              404 NotFound
```

Top-level placement notes:

- `/maps/:mapId` and `/embed/maps/:mapId` remain top-level because the
  viewer is a shared target of Catalog, Studio, and Share, and the embed
  URL is the contract surface that external sites already use. Forcing
  them under `/catalog` or `/studio` would either invent two list
  surfaces or break embed deep-links. `/maps/:mapId` is additionally the
  share-link target emitted by `honua-portal:src/share/snippet.ts:27` for
  saved-map items (`/maps/:id` plus `?token=<value>` for public-link tier),
  so the route must accept anonymous reads when `ShareAccess` authorizes
  them; see §2.5 and §6.4.
- `/groups` stays top-level (not under `/operate`) because workspace
  groups are member-accessible in Portal today
  (`honua-portal:src/routes/Groups.tsx:10` allows `member`, `operator`, or
  `admin`), and `/operate/*` is gated to `canSeeOperatorLinks(session)`
  (operator-or-admin only). Folding groups under Operate would remove
  member access. Preserving the legacy `/groups` URL also avoids a
  redirect for existing deep links.
- `/operate/legacy/<path>` is a transitional container that mirrors the
  legacy `@page` path one-to-one (see §5, EMBED rows). This survives
  existing deep-links from internal docs and runbooks.
- `/environments*` are host-support routes, not a fifth product area.
  They let the same shell bind to browser HTTP profiles or native MAUI
  profiles. The browser host renders native gRPC, native mTLS,
  certificate selection, connect/disconnect, and trust validation as
  unsupported states without loading native services. `/operate/native-stream`
  is also a host-support proof route; it lives under `/operate` because
  the proof is operator telemetry, but it is not a legacy Admin route.

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

Connections, Resources, Publishing, Identity, License, Observability,
Operations, Control Center, Services, Layers, Catalogs, Settings, Deploy,
Server Info, Analytics, Legacy.

Catalogs (`/operate/catalogs`) is the discovery-endpoints surface
(`honua-console#125`): which catalog dialects the server publishes
(Esri / OGC API Records / OData / STAC / DCAT), their server-wide on/off
state, auto-default-on vs opt-in registration, feeders, and per-endpoint
issues. It lives under Operate (not the top-level `/catalog`) because it
is operator administration of server-published endpoints — the same
information area as `Settings → Catalog endpoints`, which owns the
server-wide on/off toggles — and is **distinct** from the singular
content catalog at `/catalog` (`CatalogPage.razor`), which is the
member-facing content search/list. It binds the catalog
discovery-endpoints registry (`honua-server#1279`) and renders the
missing-binding state until that contract ships.

`honua-console#36` implements the first native Blazor transition group:
Connections, Resources, Services, Layers, and Settings. These entries use
the non-legacy `/operate/...` routes listed in §1. The Legacy submenu
continues to expose remaining `/operate/legacy/...` Admin paths until
their native replacements have parity evidence and retirement gates.

The Legacy submenu contains the transitional iframe-embedded pages from
§5 (EMBED rows) and disappears when those pages are retired.

Groups is **not** in Operate secondary nav. `/groups` is a top-level
workspace surface accessible to any authenticated user (§1, §6.1); it is
surfaced via deep links and the workspace switcher menu until a dedicated
Groups feature ticket lands (tracked alongside `honua-portal#15`).

Native host support navigation is always visible in the shared shell:
Environments (`/environments`) and Native Stream
(`/operate/native-stream`). On the web host these routes remain usable
for seeded profile inspection, active-profile selection, and
unsupported-state rendering; native profile creation, native gRPC, native
mTLS, certificate selection, connect/disconnect, and trust validation
require the MAUI host.

### 2.3 Edition gates

Edition gates render as a per-route badge or upgrade tile for
authenticated sessions that can otherwise reach the route (not a hidden
route). Anonymous users still follow the route's base response contract:
`auth` routes redirect to sign-in, while anonymous-capable
Share/Catalog/Maps denials render `ConsoleStateView Kind="unavailable"`
without upgrade copy (§7, §13 Q5). Edition is sourced from
`LicenseInfo.Edition`
(`honua-server:src/Honua.Core/Features/Licensing/Domain/LicenseInfo.cs:14`).
On the wire, `LicenseInfo.Edition` is a `string` populated from
`HonuaEdition.ToString()` (e.g. server mappers at
`honua-server:src/Honua.Server/Features/Admin/LicenseStatusResponseMapper.cs:40`
and `LicenseAdminEndpoints.cs:91`), so the literal values are
`"Community"`, `"Pro"`, or `"Enterprise"`. A direct lexicographic string
comparison is **incorrect** (`"Enterprise" >= "Pro"` is false because
`'E' < 'P'`); Console must parse the wire string into an ordered rank
from `HonuaEdition`
(`honua-server:src/Honua.Core/Features/Licensing/Domain/LicenseModels.cs:9–25`)
before evaluating the gate:

```
edition rank: Community = 0, Pro = 1, Enterprise = 2
```

This rank map lives once in the `<RouteGuard>` projection
(`honua-console#2`); per-route gates only name the required tier.

Edition gate values:

- `edition:any` — no edition constraint (default).
- `edition:Pro` — requires `rank(LicenseInfo.Edition) ≥ rank("Pro")`.
- `edition:Enterprise` — requires `rank(LicenseInfo.Edition) ≥ rank("Enterprise")`.

Console owns the rank table for now (a six-line constant). The wire DTO
stays a string so the trimmer constraint flagged for `honua-server#1162`
(§11) remains satisfied; if a future SDK projection ever exposes a typed
`Edition` surface, Console swaps the local rank table for it (tracked
by §14 alongside the other Portal-to-SDK swaps).

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
- `analytics.clustering`, `analytics.spatial-join`,
  `analytics.buffer-aggregate`, `analytics.density`
- `temporal.filtering`, `temporal.extent-discovery`,
  `temporal.histogram`, `temporal.time-series-tiles`,
  `temporal.animation-api`
- `raster.cloud-cog-serving`, `raster.cloud-storage-config`,
  `raster.temporal-mosaic`

### 2.5 Anonymous routes

`/auth/signin`, `/auth/callback`, `/auth/signed-out`, `/share`,
`/share/public`, `/public`, `/share/public/items/:idOrSlug`,
`/public/items/:idOrSlug` (only for public open-data service/layer/document
items per §4.3),
`/embed/maps/:mapId`,
`/maps/:mapId` (only when `ShareAccess` authorizes the read — either
`sharing = public` or `sharing = public-link` with a valid
`?token=<value>` query),
`/catalog/:idOrSlug` (same condition: anonymous read only when
`ShareAccess` authorizes the item via `?token=<value>` at public-link
tier, or `sharing = public`), and `*` (NotFound) are anonymous-capable.
The Share, Embed, Catalog-item, and public map-viewer routes still
resolve through `ShareAccess` and `resolveEmbedAuthorization` (§4) —
anonymous-capable does not mean unauthenticated reads always succeed.

The `/maps/:mapId` and `/catalog/:idOrSlug` anonymous cases exist
because `honua-portal:src/share/snippet.ts:27` emits one of two URLs:

- `/maps/:id?token=<value>` for `itemKind = "map"` at public-link tier
  (test fixture `share/__tests__/snippet.test.ts:23` expects
  `${HOST}/maps/m-1?token=abc-123`).
- `/catalog/:id?token=<value>` for every other `itemKind` (service,
  layer, dashboard, report, app, etc.) at public-link tier — same
  `buildShareLink` builder, different `itemKind` branch
  (`share/snippet.ts:29`).

The token is in the query (not the fragment) because share-link
recipients paste the full URL into a browser; recipients are not
embedding the URL in an iframe — that path uses
`/embed/maps/:mapId#embedToken=` (§8) instead. Console must therefore
honor the `?token=` query on both `/maps/:mapId` and
`/catalog/:idOrSlug` as an anonymous bearer; signed-in users get the
authenticated read path instead.

---

## 3. Portal → Console Destination Map

Every current `honua-portal` route appears here. The Portal inventory is
16 paths declared in `honua-portal:src/router.tsx`.

| # | Portal path | Console destination | Query / fragment contract | Gate | Code-split chunk | Smoke label |
|---|---|---|---|---|---|---|
| 1 | `/` | `/` | — | `auth` | shell | — |
| 2 | `/auth/signin` | `/auth/signin` | `returnTo=<sanitized>` | `anonymous` | shell+auth | — |
| 3 | `/auth/callback` | `/auth/callback` | — | `anonymous` | shell+auth | — |
| 4 | `/auth/signed-out` | `/auth/signed-out` | — | `anonymous` | shell+auth | — |
| 5 | `/public` | `/share/public` (`/public` is accepted as a compatibility alias in the Blazor route slice) | collection includes only `isPublicOpenDataSummary` items (`sharing = public`, `openData = true`, type in `PUBLIC_OPEN_DATA_TYPES`); newly generated links use `/share/public` | `anonymous` | share | open-data |
| 6 | `/public/items/:idOrSlug` | `/share/public/items/:idOrSlug` (`/public/items/:idOrSlug` is accepted as a compatibility alias) | item must pass `isPublicOpenDataItem` (`access.sharing = public`, `access.openData = true`, type in `PUBLIC_OPEN_DATA_TYPES`); DCAT-US / schema.org JSON-LD remains part of full open-data parity | `anonymous` (+ open-data eligibility) | share | open-data, share |
| 7 | `/embed/maps/:mapId` | `/embed/maps/:mapId` | `chrome`, `legend`, `zoom`, `extent=W,S,E,N` (WGS84 lon/lat); public embeddable maps may render tokenless, token-authorized embeds carry the bearer in fragment `#embedToken=` only, and query-string `token` / `embedToken` is rejected | `anonymous` (+ `resolveEmbedAuthorization`) | embed | embed |
| 8 | `/catalog` | `/catalog` | `q`, `type`, `tag`, `owner`, `visibility`, `sort`, `cursor` (per `honua-portal:src/catalog/searchParams.ts:38` and `ListItemsRequest` in `honua-portal:src/contracts/content-item.ts:251`; the wire contract sets `additionalProperties: false`, so the Console list page must not invent new query keys) | `auth` | catalog | catalog-list |
| 9 | `/catalog/:idOrSlug` | `/catalog/:idOrSlug` | `?token=<value>` for public-link share tier (`honua-portal:src/share/snippet.ts:29` emits this for non-map items) | `auth` (+ server item read; `resolvePortalItemRole` only gates actions) **or** `anonymous` (+ `ShareAccess` with `share-tier:public` or `share-tier:public-link` + valid token) | catalog | catalog-detail |
| 10 | `/maps` | `/catalog?type=map` (list) + `/studio` (create CTA) | preserves `from=:itemId` on the create path | `auth` | catalog (list), studio (create) | — |
| 11 | `/maps/:mapId` | `/maps/:mapId` | `?token=<value>` for public-link share tier (`honua-portal:src/share/snippet.ts:27`); draft hydration from Catalog uses `/maps/new?from=:itemId` | `auth` (+ server saved-map/package read) **or** `anonymous` (+ `ShareAccess` with `share-tier:public` or `share-tier:public-link` + valid token) | viewer | viewer |
| 12 | `/app-builder/proof` | `/studio` (entry) + `/studio/proof` (legacy alias) + `/studio/drafts` (source-scoped start) | legacy 301 → `/studio/drafts?source=…&id=…` | `auth` | studio | studio-generation |
| 13 | `/apps/:itemId/preview` | `/studio/apps/:itemId/preview` | preserves `?revision=<n>` | `auth` (+ generated-app preview read) | studio | studio-generation |
| 14 | `/data` | `/catalog` | — (Portal `/data` renders an `EmptyState` placeholder at `honua-portal:src/routes/Data.tsx:10` — "Data view is coming soon"; Console drops `/data` as a dedicated surface and uses Catalog's type strip instead. The `honua-console#34` route slice includes `dataset` as a supported catalog type, but a dataset-like multi-type filter remains deferred until the shared catalog filter contract changes.) | `auth` | catalog | — |
| 15 | `/groups` | `/groups` | — | `auth` (any scope — member, operator, or admin, per `hasAnyScope(session, ["member", "operator", "admin"])` in `honua-portal:src/routes/Groups.tsx:10`) | shell | — |
| 16 | `*` | `*` (Console NotFound) | — | `anonymous` | shell | — |

**Redirects.** Old `/app-builder/proof`, `/maps` (list),
`/apps/:itemId/preview`, and `/data` serve a 301 to their Console
destination once Console is live. `/public` is currently served as a
compatibility alias for `/share/public` by the Blazor route slice, with
canonical generated links pointing at `/share/public`. `/groups` is
**not** redirected — the legacy path is preserved at top level (see §1
placement notes and §6.1 routing). The exceptions are `/embed/maps/:mapId` and open-data item
detail URLs (`/public/items/:idOrSlug` legacy and
`/share/public/items/:idOrSlug` Console): these are served at both paths
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
- `ROLE_MATRIX` (`share/rbac.ts:22`) for post-load item action
  availability on `/catalog/:idOrSlug` (editMetadata, updateSharing,
  inviteEditors, inviteViewers, revokeAccess; its `read` flag describes
  the resolved role after the item is already loaded).

`resolvePortalItemRole` is an action-role projection after an item is
loaded, not the route read-authorization primitive. Item/detail preview
routes load through the server-owned content, saved-map/package, or
generated-app read API; a 403/`CatalogError("unauthorized")` or
`GeneratedAppLifecycleError("unauthorized")` maps to the §7 forbidden
or unavailable surface. After a successful load, `ROLE_MATRIX` controls
which authenticated item actions are visible.

### 4.3 Sharing and embed

Defined in `honua-portal:src/share/`, `honua-portal:src/embed/`, and
`honua-portal:src/open-data/`:

- `SharingTier = "private" | "org" | "group" | "public-link" | "public"`
  (`share/types.ts:14`).
- `ShareAccess` (`share/types.ts:22`) — sharing tier, embeddability,
  group ids, public-link token.
- `evaluateShareEscalation(...)` (`share/policy.ts:45`) — client parity
  with the server 409 closure-violation contract.
- `resolveEmbedAuthorization(...)` (`embed/permissions.ts:68`) — the
  read decision for `/embed/maps/:mapId`.
- `PUBLIC_OPEN_DATA_TYPES = ["service", "layer", "document"]`
  (`open-data/public-items.ts:11`) — the only item types exposed through
  `/share/public` and `/share/public/items/:idOrSlug`.
- `isPublicOpenDataSummary(item)` (`open-data/public-items.ts:32`) —
  collection-row predicate: `item.sharing === "public"`,
  `item.openData === true`, and `item.type` is in
  `PUBLIC_OPEN_DATA_TYPES`.
- `isPublicOpenDataItem(item)` (`open-data/public-items.ts:36`) —
  detail-route predicate: `item.access.sharing === "public"`,
  `item.access.openData === true`, and `item.type` is in
  `PUBLIC_OPEN_DATA_TYPES`.
- `buildShareLink({ portalHost, itemId, itemKind, publicLinkToken })`
  (`share/snippet.ts:27`) — emits `/maps/:id` for saved-map items and
  `/catalog/:id` for other item kinds, adding `?token=<value>` at the
  public-link tier. The Console `/maps/:mapId` and
  `/catalog/:idOrSlug` routes must accept the anonymous `?token=`
  variant so share-link recipients are not bounced to sign-in (§2.5,
  §6.3, §6.4). Tests pin the map format at
  `share/__tests__/snippet.test.ts:23`.

### 4.4 Edition and entitlement

Server-authored, consumed by Console via the `LicenseInfo` DTO:

- `HonuaEdition` enum: `Community = 0`, `Pro = 1`, `Enterprise = 2`
  (`honua-server:src/Honua.Core/Features/Licensing/Domain/LicenseModels.cs:9`,
  values at lines 14, 19, 24). The integer ordinals encode the rank used
  by `edition:<tier>` gates (§2.3).
- `LicenseEntitlementDecision` with `UpgradeMessage`
  (`LicenseModels.cs:141`, `UpgradeMessage` at line 147).
- `ILicenseEntitlementService.GetSnapshot()` and
  `CheckEntitlement(entitlementKey)`
  (`honua-server:src/Honua.Core/Features/Licensing/Abstractions/ILicenseEntitlementService.cs:11/17/24`).
- `LicenseInfo` HTTP DTO with `Edition` (`string`, populated from
  `HonuaEdition.ToString()`), `IsValid`, `ExpiresAt`, `ValidationState`,
  `Entitlements[].Key/Name/IsActive`
  (`honua-server:src/Honua.Core/Features/Licensing/Domain/LicenseInfo.cs:9/14/19/24/29/49`;
  `Entitlement` at line 55, fields at 60/65/70). Wire values are the
  enum names: `"Community"`, `"Pro"`, `"Enterprise"`. Console parses to
  the rank table in §2.3 before evaluating `edition:<tier>`; do **not**
  string-compare on the wire value.
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

Each route row in this document uses the following gate vocabulary for
client-side route guards. Rows may also name a server or SDK read call
when read authorization is returned by the load itself rather than by a
client-side gate. A route may have multiple gates; all must pass.

| Token | Meaning |
|---|---|
| `anonymous` | No session required. The route is reachable without sign-in. |
| `auth` | `isAuthenticated(session)` must be true. |
| `scope:<name>` | `hasScope(session, <name>)`, e.g. `scope:operator`. |
| `item-role:<min>` | Post-load action gate: `resolvePortalItemRole(session, item)` ≥ `<min>` per `ROLE_MATRIX`; not used as the route read-authorization primitive. |
| `share-tier:<min>` | `ShareAccess.sharing` ≥ `<min>`. |
| `open-data` | Item passes `isPublicOpenDataSummary` (collection) or `isPublicOpenDataItem` (detail): public sharing, `openData = true`, and type in `PUBLIC_OPEN_DATA_TYPES`. |
| `entitlement:<key>` | `ILicenseEntitlementService.CheckEntitlement(<key>)` is granted. |
| `edition:<tier>` | `rank(LicenseInfo.Edition) ≥ rank(<tier>)` per the rank table in §2.3 (`Community = 0`, `Pro = 1`, `Enterprise = 2`); the wire value is a string and must not be compared lexicographically. |

Authenticated item routes (`/catalog/:idOrSlug`, `/maps/:mapId`, and
`/studio/apps/:itemId/preview`) first rely on the server or SDK read call
to authorize the item/package load. Console must implement the explicit
gate tokens as one `<RouteGuard>` consuming the named contracts; the
guard surface is the seam called out in `honua-console#2`.

### 4.7 SDK projection timing (default)

Several helpers (`resolvePortalItemRole`, `evaluateShareEscalation`,
`resolveEmbedAuthorization`, `buildShareLink`, `PUBLIC_OPEN_DATA_TYPES`,
`isPublicOpenDataSummary`, `isPublicOpenDataItem`) live in
`honua-portal` and have not yet been projected to shared SDK contracts.
Default decision (§13, Q7): the Blazor Web Console treats the Portal
helpers as behavior references until `honua-sdk-dotnet#166` projects the
server-owned route/RBAC/content DTOs; generated apps, embeds,
MCP/QGIS/browser integrations, and map/chart/editor interop use
`honua-sdk-js#225` once it projects the equivalent JS contracts. Portal
is on the retirement path (`honua-console#10`), so the dual-reference
window is bounded.

---

## 5. Admin → Disposition Map

Every `@page` declared in
`honua-server-admin/src/Honua.Admin/Pages` appears here. The inventory
is 41 unique paths across 36 `.razor` files (some files declare
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
the gate, the canonical empty/forbidden surface, and the code-split
chunk. Smoke labels are anchored in the Portal destination map (§3) and
the smoke pipeline (§10).

### 6.1 Shell and Auth

| Route | Gates | Empty surface | Forbidden surface | Chunk |
|---|---|---|---|---|
| `/` | `auth` | empty-workspace | unauth-redirect | shell |
| `/auth/signin` | `anonymous` | — | — | shell+auth |
| `/auth/callback` | `anonymous` | — | session-error | shell+auth |
| `/auth/signed-out` | `anonymous` | — | — | shell+auth |
| `/groups` | `auth` (any scope: `member`, `operator`, or `admin`) | empty-groups (no group memberships yet) | unauth-redirect / forbidden | shell |
| `/environments` | `host-support` | empty environment profiles; browser native actions unsupported | — | shell |
| `/environments/new` | `native-host` (`SupportsNativeTransports`) | unsupported-native on web | — | shell |
| `/environments/:profileId` | `host-support` | missing environment profile | browser native actions unsupported | shell |
| `/support` | `auth` | unsupported-binding when honua-support base URL unset | unauth-redirect | shell |
| `/support/tickets/:ticketId` | `auth` | missing/forbidden/unavailable ticket via shared section surface | unauth-redirect | shell |
| `*` | `anonymous` | notfound | — | shell |

`/groups` lives in the shell chunk (not Operate or Catalog) because it
is a member-accessible workspace surface and is a placeholder pending a
dedicated Groups feature ticket. Empty-state copy mirrors current Portal
("You're not a member of any groups yet" — `honua-portal:src/routes/Groups.tsx:27`).
Authenticated sessions without `member`, `operator`, or `admin` render
`ConsoleStateView Kind="forbidden"` rather than redirecting.

The environment routes are shared host-support routes added by
`honua-console#44`. They use the shell-owned `IConsoleHostCapabilities`
seam: the web host keeps `SupportsNativeTransports = false`, does not
register `IConsoleConnectionManager`, and renders native-only controls as
"Native host only"; the MAUI host registers native profile storage,
certificate resolution, connection management, server-certificate probe,
and the honua-server#1171 validation client. The native trust gate
resolves the bound client certificate once and passes it, with the
accepted server fingerprint, into both the validation call and the native
HTTP/gRPC connection factory, so the transport presents exactly the server
identity and client certificate that were validated. When a fingerprint is supplied, the
transport callback requires the presented certificate fingerprint to
match it rather than accepting a different OS-trusted certificate; when
no fingerprint is supplied, the callback falls back to the OS chain
decision. This keeps pinned private or self-signed server identities
usable after acknowledgement while still blocking changed server
identities. Unreachable HTTPS probes preserve the previously persisted
trust state. Environment profile state is Console-owned local host state;
server-validated trust results remain behind `Honua.Console.Contracts`
until `honua-sdk-dotnet#166` is consumable.

### 6.2 Studio

| Route | Gates | Empty | Forbidden | Chunk |
|---|---|---|---|---|
| `/studio` | `auth` | Studio home landing (`StudioHome`): hero "What do you want to build?" prompt with suggestion chips that targets the inline-authoring shell at `/studio/proof?prompt=…`, the eight "Start with a content type" cards linking to the existing builders (`/studio/map`, `/studio/dashboard`, `/studio/report`, `/studio/form`, `/studio/app`, `/studio/query`, `/studio/analysis`, `/studio/workflows/new`), and a "Recent projects (last 14 days)" table bound to the server content listing (`IConsoleCatalogClient`); recent-projects renders missing-binding when no server base address is configured (honua-server#1162), empty-state when bound-but-no-rows — never seeded rows. The inline-authoring shell stays reachable at `/studio/proof` / `/studio/drafts` | unauth-redirect | studio |
| `/studio/query` | `auth` | missing-binding when no server base address; bound-but-no-list capability state (honua-server#1182 saved-query content has no list route — open by id or create new); authoring editor (source binding, predicate builder, generated SQL/filter readout, projection, parameter editor, save-as-content) with live map/table preview after save | unauth-redirect / missing-permission (server RBAC) | studio |
| `/studio/analysis` | `auth` | missing-binding when no server base address; core content/artifacts contract live-bound (honua-server#1182, closed); bound-but-no-list capability state and server cost-estimate ("estimate unavailable from server" → local Console projection) both degraded until honua-server#1237 (analysis content API: list + cost-estimate) lands — open by id or create new; submit blocked until title, method, a bound input, an output-schema field, and a compute estimate are present; result-artifact panel after submit | unauth-redirect / missing-permission (server RBAC) | studio |
| `/studio/map` | `auth` | missing-binding when no server base address; server-bound map builder on the Studio package lifecycle (honua-server#1180) + content publication registry (honua-server#1183) — author layers/filters/style/popup/legend/basemap/extent/interactions, save a `map.package` draft, review then publish (freezes an immutable content version and routes it to the publication contract), and reopen a published version as a new draft; bound-but-no-list capability state (no Studio package list route — open by id or create new); publish blocked until title, a sourced layer, basemap, and initial extent are present; Comments + Activity editor tabs host the multiplayer collaboration surface (honua-console#124 — presence avatar stack, named live cursors, per-draft collaborative markup layer, feature-pinned comment pins + thread drawer, follow-mode pill, live activity feed) which renders an explicit missing-binding state until the collaboration/presence + comments API lands (honua-server#1278) — never fabricated presence/cursors/comments | unauth-redirect / missing-permission (server RBAC) | studio |
| `/studio/dashboard` | `auth` | missing-binding when no server base address; server-bound builder (honua-console#55) on the honua-server Studio package lifecycle + publication registry (#1180/#1181/#1183) — save draft, server validate, publish (save content version then publish request), and reopen against the live `dashboard` family; data bindings, layout panels, Vega-Lite charts, filters, narrative, version pinning, and responsive preview; publish blocked until every chart declares a Vega-Lite spec and every panel binds to a declared data binding; package-list surface is empty with an "Unsupported" capability state until honua-server exposes a Studio content-item listing endpoint (New/open-by-id is live) | unauth-redirect / missing-permission (server RBAC) | studio |
| `/studio/report` | `auth` | missing-binding when no server base address; server-bound report builder (honua-console#56) on the content publication registry (honua-server#1183) — author a report (outline/sections, narrative, data bindings with version pins, Vega-Lite charts, map/table/filter panels, responsive preview), then publish (claims a route), republish (advances the active version), update visibility/embed policy, and roll back (pin) to an earlier immutable version; publish blocked until the report has a title, at least one panel bound to a declared data binding, and every chart declares a Vega-Lite spec | unauth-redirect / missing-permission (server RBAC) | studio |
| `/studio/form` | `auth` | missing-binding when no server base address; empty form-package list when bound; publish blocked until the offline/sync policy is reviewed and the submit target validates | unauth-redirect / missing-permission (server RBAC) | studio |
| `/studio/app` | `auth` | missing-binding when no server base address; server-bound app.package builder (honua-console#58) on the Studio package lifecycle (honua-server#1180) and publication registry (honua-server#1183) — author pages/components/navigation/bindings/actions/permissions, review the share/embed policy, validate, preview, then save versions and publish; version history offers preview/reopen and rollback, and reopened edits create new content versions rather than mutating published state; publish blocked until a page binds content, every action declares a permission, and the share/embed policy is reviewed | unauth-redirect / missing-permission (server RBAC) | studio |
| `/studio/proof` | `auth` | inline-authoring shell (prompt → clarification → server package draft → validate → preview-plan → save → publish); accepts `?prompt=…` to seed the prompt from the `/studio` home hero | unauth-redirect | studio |
| `/studio/drafts` | `auth` | empty-studio (source-scoped draft start) | unauth-redirect | studio |
| `/studio/apps/:itemId/preview` | `auth` (+ generated-app preview read) | missing-item | forbidden / unsupported-package | studio |
| `/studio/workflows/new` | `auth` (+ workflow author permission) | empty-studio | forbidden / missing-binding | studio |
| `/studio/workflows/:draftId` | `auth` (+ workflow draft read/write) | missing-item | forbidden / missing-binding | studio |

The current `/studio` implementation is the package-first Studio shell
(real-server revisit `honua-console#61` of the `honua-console#38`
slice). It runs in the shared Razor component library and binds the
server-owned package lifecycle — draft create/read/update, validation,
preview-planning, content-version save, and publish — to honua-server
(`honua-server#1180`/`#1181`) through the `IStudioPackageLifecycleClient`
shim in `Honua.Console.Contracts`; prompt and structured clarification
stay Console-local UX. Workflow choices cover map, dashboard, report,
form, app, query, analysis, workflow, GP service, and ETL. Ambiguous
prompts produce structured clarification questions; while any question
remains open, Validate, Preview, Save Version, and Publish controls are
disabled. The inspector remains visible for the active package and must
include assumptions, data bindings, warnings, validation, and
provenance. Draft, Saved version, and Published are the lifecycle states;
Preview is a transient preview-plan action (not a stored state), and
rendered preview output / generated-app preview has no server contract
yet, so it renders the shared missing-binding surface. When no server
base address is configured the shell renders that missing-binding surface
instead of mock package data; the in-memory authoring shell
(`AddHonuaConsoleDemoStudioAuthoringShell`) is demo/test-only.

`/studio/proof`, `/studio/drafts?source=<kind>&id=<itemId>`,
`/studio?source=<kind>&itemId=<itemId>`, and
`/studio/apps/:itemId/preview` are mounted to the same package shell for
the current Console slice so smoke evidence, legacy proof links, viewer
edit actions, and generated-app reopen paths resolve through an
implemented Studio route. The `?source=...&itemId=...` spelling is the
route-compatible action URL emitted by the `honua-console#34` map viewer
and draft-map slice. The route parameters are accepted but not yet used
to hydrate a server-backed package. Save Version and Publish do create
server-owned content versions and publication requests through the
honua-server package lifecycle.

The package editor routes above started as the `honua-console#39`
Console-native Studio slice and per-editor families `honua-console#52`–
`#58`. `/studio/form` is now the `honua-console#57` server-bound form
package builder over `honua-server#1184`; it uses its own
missing-binding and capability-state surface instead of the local package
simulator. The remaining generated-package editor routes render shared
Razor editors from the Studio package editor catalog and still use the
local lifecycle simulator, but their validate/preview actions surface a
missing-binding state rather than mock validation success until each
editor's backend contract lands. The local projection is documented in
[`docs/studio/package-editor-routes.md`](studio/package-editor-routes.md)
and must be replaced behind the same editor model when shared contracts
land; the shared `/studio` shell already binds that lifecycle.

Workflow routes are builder-owned Studio surfaces. The real-server revisit
(`honua-console#62`) binds the `workflow.package/v1` editor to honua-server
(`honua-server#1185`) through the `IWorkflowPackageApiClient` HTTP shim in
`Honua.Console.Contracts`: the node registry, package drafts, immutable
versions, dry-runs, and publications/runs render from live
`/api/v1/console/workflow-*` data, and job-backed published runs link into
Operate with job-scoped URLs. They do not move workflow authoring into the Operate
information architecture. When no server base address is configured the editor
renders the shared missing-binding surface instead of seeded workflow data; the
in-memory seeded client (`AddHonuaConsoleDemoStudioWorkflowPackages`) is
demo/test-only.

### 6.3 Catalog

| Route | Gates | Empty | Forbidden | Chunk |
|---|---|---|---|---|
| `/catalog` | `auth` | empty-catalog (start a search / publish first item) | unauth-redirect | catalog |
| `/catalog/:idOrSlug` | `auth` (+ server item read) **or** `anonymous` (+ `ShareAccess` with `share-tier:public`, or `share-tier:public-link` and a valid `?token=<value>`) | missing-item | forbidden / unsupported-service / unavailable (anonymous) | catalog |

The `honua-console#34` Blazor slice pins the public list query contract to
the Portal keys `q`, `type`, `tag`, `owner`, `visibility`, `sort`, and
`cursor`. Unknown query keys are tracked as ignored keys and are not
forwarded. `visibility` is the route/query spelling; it maps to the SDK
request field `sharing`. The public URL must not accept a `sharing` query
key. Invalid `type`, `visibility`, and `sort` enum values normalize to no
type filter, no sharing filter, and `relevance`, respectively. Accepted
`visibility` values are `private`, `org`, `group`, `public-link`, and
`public`; accepted `sort` values are `relevance`, `modified-desc`,
`modified-asc`, `title-asc`, and `title-desc`. The list surface requires
an authenticated workspace session; public/open-data collection reads
live under `/share`, `/share/public`, and `/public`.

The route slice exposes the unified content strip for `dataset`,
`service`, `layer`, `document`, `map`, `dashboard`, `report`, `form`,
`app`, `workflow`, `analysis`, `gp-service`, `etl-pipeline`, `connector`,
and `template`. Detail pages expose Overview, Versions, Lineage,
Bindings, Publication, Permissions, Activity, and Usage tabs. Publication
shows the canonical share link; Usage is the pre-retirement dependency
risk surface. Detail tab state is carried by
`tab=overview|versions|lineage|bindings|publication|permissions|activity|usage`;
missing or unknown tab values render Overview.

`/catalog/:idOrSlug` is anonymous-capable on the same `ShareAccess` +
`?token=` contract as `/maps/:mapId` (§6.4, §2.5). Anonymous denials
render `ConsoleStateView Kind="unavailable"` (not the upgrade tile) so
upgrade copy is not leaked to anonymous users (§13 Q5). Authenticated
server-read denials render `ConsoleStateView Kind="forbidden"`. Action
gates (editMetadata, updateSharing, etc.) still require authenticated
`item-role` per
`ROLE_MATRIX` (§4.2) and are hidden from the anonymous read surface.
Anonymous public-link detail and map actions preserve the validated
`?token=<value>` on followable `/catalog/:idOrSlug` and `/maps/:mapId`
links. When a signed-in workspace session exists, Console resolves the
authenticated read path before the token path, so stale query tokens do
not downgrade the read context or propagate into action links. Non-map
draft hydration remains authenticated-only.

### 6.4 Maps (viewer)

| Route | Gates | Empty | Forbidden | Chunk |
|---|---|---|---|---|
| `/maps/:mapId` | `auth` (+ server saved-map/package read) **or** `anonymous` (+ `ShareAccess` with `share-tier:public`, or `share-tier:public-link` and a valid `?token=<value>`) | missing-item | forbidden / unsupported-package / unavailable (anonymous) | viewer |
| `/maps/new` | `auth` | empty-new-map (choose a catalog item) / unsupported-source | unauth-redirect | viewer |

`/maps/:mapId` is anonymous-capable for `ShareAccess.sharing = public`
and for `sharing = public-link` when the URL carries the matching
`?token=<value>` query that `honua-portal:src/share/snippet.ts:27` emits
(see §2.5). Authenticated reads use the server-owned saved-map/package
read path described in §4.2 and §4.6; `item-role` gates only post-load
actions. The `forbidden` surface renders for authenticated denials; the
`unsupported-package` surface renders when the saved-map package is newer
than the Console runtime; the `unavailable` surface (§7) renders for
anonymous denials so upgrade copy is not leaked to anonymous users
(§13 Q5).

`/maps/new?from=<itemId>` hydrates an unsaved draft map from a supported
catalog service or layer only after an authenticated workspace session is
resolved, and keeps the Studio continuation as
`/studio?source=catalog&itemId=<itemId>`. Anonymous or public-link
contexts render an unauthenticated/sign-in surface before hydration.
Unsupported source metadata uses the same unsupported-service surface as
catalog detail.

### 6.5 Operate

Operate landing and administration routes require `auth` and
`canSeeOperatorLinks(session)` (scope `operator` or higher). The
host-support `/operate/native-stream` proof route is the exception: it is
shared by the web and native hosts, renders unavailable on web when no
native streaming service is registered, and requires an active environment
profile before emitting events. In the native host the proof opens the
active profile through `IConsoleConnectionManager`; blocked or unreachable
trust states emit no proof events, and successful streams update only
resume/stream diagnostics while preserving profile trust pins. Job and
event evidence routes can also be entered from builder workflows when the
server authorizes read access to the specific job. Per-route gates listed
below add edition or entitlement requirements on top.

For `honua-console#60`, the native transition group
(`/operate/connections`, `/operate/resources`, `/operate/services`,
`/operate/layers`, and `/operate/settings`) resolves through
`IOperateTransitionDataSource`. Normal browser runtime binds that source to
honua-server admin endpoints when an absolute HTTP(S) server base URL is
configured, or renders a named missing-binding state when it is not. Missing
server subcontracts, permission denials, and unsupported admin endpoints
surface as in-page capability states rather than seeded rows.

| Route | Additional gates | Empty | Forbidden | Chunk |
|---|---|---|---|---|
| `/operate` | — | empty-operate | forbidden (operator) | operate |
| `/operate/connections` | — | empty-operate (no connections) | forbidden | operate |
| `/operate/connections/new` | — | — | forbidden | operate |
| `/operate/connections/:id` | — | missing-item | forbidden | operate |
| `/operate/connections/:id/diagnostics` | — | missing-item | forbidden | operate |
| `/operate/resources` | — | empty-operate (no resources) | forbidden | operate |
| `/operate/resources/new` | — | — | forbidden | operate |
| `/operate/resources/:id` | — | missing-item | forbidden | operate |
| `/operate/publishing` | — | empty-operate | forbidden | operate |
| `/operate/identity/providers` | `entitlement:identity.oidc` (gate the OIDC provider) | empty-operate | forbidden / upgrade | operate |
| `/operate/identity/status` | — | empty-operate | forbidden | operate |
| `/operate/identity/diagnostics` | `entitlement:identity.claims-mapping` (gate claims tab) | — | forbidden / upgrade | operate |
| `/operate/license` | — | — | forbidden | operate |
| `/operate/observability` | — | empty-operate | forbidden | operate |
| `/operate/events/:eventId` | — | no matching events | forbidden | operate |
| `/operate/alerts/:alertId` | — | no active alerts | forbidden | operate |
| `/operate/jobs/:jobRunId` | — | missing job | forbidden | operate |
| `/operate/operations` | — | empty-operate | forbidden | operate |
| `/operate/control-center` | — | empty-operate | forbidden | operate |
| `/operate/services` | — | empty-operate (no services) | forbidden | operate |
| `/operate/services/:name/settings` | — | missing-item | forbidden | operate |
| `/operate/layers` | — | empty-operate (no layers) | forbidden | operate |
| `/operate/layers/:id` | — | missing-item | forbidden | operate |
| `/operate/layers/:id/style` | — | missing-item | forbidden | operate |
| `/operate/settings` | — | — | forbidden | operate |
| `/operate/deploy` | — | empty-operate | forbidden | operate |
| `/operate/server-info` | — | — | forbidden | operate |
| `/operate/analytics` | `edition:Pro` | empty-operate | forbidden / upgrade | operate |
| `/operate/events` | — | missing-item | forbidden | operate |
| `/operate/native-stream` | `host-support`; native gRPC service + active profile for event stream | native proof unavailable / no active environment | — | operate |
| `/operate/legacy/<path>` | — | — | forbidden | operate |

`honua-console#24` replaces the native Blazor observability checkpoint
runtime binding for `/operate/observability` and the event, alert, and
job detail routes above. Server-owned Operate data now resolves through
`IConsoleOperateObservabilityClient`, a thin `HttpClient` boundary over
the live honua-server admin APIs under `/api/v1/admin/version`,
`/api/v1/admin/capabilities`, `/api/v1/admin/observability`,
`/api/v1/admin/alerts`, `/api/v1/admin/jobs`, and
`/api/v1/admin/investigations`. `OperateObservabilityFixture.Default`
remains test/scaffolding data only; runtime missing, forbidden,
unsupported, and unavailable states render through the shared Operate
section status surface.

Job detail routes issue a server read for the requested job id and map
server 404/403/501 responses to the shared missing/forbidden/unsupported
surfaces. Event and alert detail routes are page-scoped in this slice:
they select the matching live row from the loaded event or alert page and
only default to the first returned live row when no route id is supplied.
If the route id is present but missing from the loaded page, the detail
panel renders the shared missing surface instead of unrelated live data.

The current runtime status contract treats `unknown`, `unsupported`,
`missing`, `disabled`, `not configured`, and `unconfigured` as neutral
states. `missing` displays as `unknown`; `unconfigured` displays as
`not configured`. `error` event severities and `firing` alert states
render as failures. AI advisory panels render beside raw evidence links,
invalid realtime/geofence rules keep their enable action disabled, job
actions use the server-declared action descriptors, structured logs render
from `/api/v1/admin/observability/logs`, and Studio, publishing, GitOps,
temporal, alert delivery, import, and maintenance jobs all use
`/operate/jobs/:jobRunId` as the detail URL.

`OperateObservabilityPage` is the **sole** routable owner of
`/operate/jobs/:jobRunId` (and of `/operate/observability`,
`/operate/events/:eventId`, and `/operate/alerts/:alertId`). The workflow
"Workflow Job Monitor" job-evidence surface is **not** a second component
on this template — that historical duplicate (`OperateJobPage`) terminated
the interactive circuit with an ambiguous-route exception. Its
`IStudioWorkflowPackageClient`-bound evidence (status/logs/artifacts/event
evidence + the honua-server#1185 missing-binding state) is preserved as the
non-routable `OperateWorkflowJobEvidencePanel`, which the unified job-run
detail renders for the deep-linked job id. A `ShellRouteUniquenessTests`
guard in `tests/Honua.Console.IntegrationTests` enumerates every routable
component's `@page`/`[Route]` template and fails if any template is owned by
more than one component, so the combined-route-table ambiguity (which bUnit
render tests cannot observe) is caught in unit CI.

Workflow publish results reuse `/operate/jobs/:jobRunId` and
`/operate/events?jobId=<id>` as evidence views only when the server returns a
`jobId`, not workflow-run ids or workflow editors. They may be opened from a
Studio published run when the server authorizes the caller to read that job.
The `#1185` dry-run is a synchronous estimation that creates no Operate job,
so it emits no job-scoped URLs. Scheduled workflow publications can return a
`workflowRunId`; Console must not route that id through `/operate/jobs/*`
until Operate owns a workflow-run projection. Missing or unauthorized job ids
render the standard missing/forbidden surfaces from server-backed job reads.
Blocked workflow
publications do not queue jobs and therefore do not produce job-scoped
Operate URLs, whether the blocker is endpoint parameter validation,
schedule validation, graph coverage, failure routing, or output schema
validation.

Other entitlement gates that surface inside Operate sub-pages but do
not own a top-level route (so they appear as in-page upgrade tiles
rather than route gates): `alerts.dwell`, `channels.slack`,
`geocoding.batch`, `import.geoserver`, `streaming.feature-subscriptions`,
`staticmap.high-dpi`, `analytics.clustering`,
`analytics.spatial-join`, `analytics.buffer-aggregate`,
`analytics.density`, `temporal.filtering`,
`temporal.extent-discovery`, `temporal.histogram`,
`temporal.time-series-tiles`, `temporal.animation-api`,
`raster.cloud-cog-serving`, `raster.cloud-storage-config`,
`raster.temporal-mosaic`. The Analytics page exposes those analytics
entitlements as per-tab gates after the `edition:Pro` route shell has
loaded; Console must not evaluate wildcard entitlement tokens.

### 6.6 Share

| Route | Gates | Empty | Forbidden | Chunk |
|---|---|---|---|---|
| `/share` | `anonymous`, Console Share area entry; current slice renders `/share/public` content | empty-share | — | share |
| `/share/manage` | `auth` (+ server item read; the `share`/`embed`/`administer` facets gate the per-panel controls); missing-binding when no server base address is configured. Server-bound Share access management on the Console Share access API (honua-server#1215, closed): public/private access-tier change, public-link token mint/revoke + expiration, embed enablement + embed-token mint, dependency-closure preview, and open-data eligibility. Driven by `?itemId=`. | empty-share | missing-binding / missing-permission (server RBAC) / not-found (anonymous-safe) | share |
| `/share/public` | `anonymous`, `open-data` per item (`isPublicOpenDataSummary`) | empty-share | — | share |
| `/public` | `anonymous`, compatibility alias for `/share/public` | empty-share | — | share |
| `/share/public/items/:idOrSlug` | `anonymous`, `open-data` (`isPublicOpenDataItem`) | missing-item / not-public | unavailable (anonymous, see §13 Q5) | share |
| `/public/items/:idOrSlug` | `anonymous`, compatibility alias for `/share/public/items/:idOrSlug` with the same `open-data` eligibility | missing-item / not-public | unavailable (anonymous, see §13 Q5) | share |

The Share open-data routes are not generic public-item routes. They expose
only items whose access is public, whose `openData` flag is true, and whose
type is one of `service`, `layer`, or `document` (`PUBLIC_OPEN_DATA_TYPES`).
Public maps, apps, dashboards, reports, and public-but-not-open-data items
must render the same not-public/missing surface as private or unknown items
so anonymous crawlers and users cannot infer protected titles. This mirrors
Portal tests at `honua-portal:src/open-data/OpenDataItemPage.test.tsx:109`
and `:117`.

### 6.7 Embed

| Route | Gates | Empty | Forbidden | Chunk |
|---|---|---|---|---|
| `/embed/maps/:mapId` | `anonymous` (+ `resolveEmbedAuthorization`) | missing-item | unavailable | embed |

The embed route uses the shellless embed layout rather than the primary
Console shell. Supported query options are `chrome`, `legend`, `zoom`,
and `extent=W,S,E,N`. `chrome` accepts Portal snippet profiles
`full`, `minimal`, and `none`; `legend` and `zoom` accept
`on/off` plus `true/false`, `yes/no`, and `1/0`. Extents must be valid,
non-degenerate WGS84 bounds before they override the saved map extent.
Public embeddable maps may render without a token. When an embed bearer
is required or supplied, it must be in the fragment as
`#embedToken=<value>`. A query-string `token` or `embedToken` is treated
as an unavailable embed because it would leak the bearer to server,
proxy, or CDN logs.

---

## 7. Exception Surfaces

One canonical surface per category is referenced by route. Console routes
do not author bespoke 403/404/empty copy. The `honua-console#34` Blazor
slice renders these through `ConsoleStateView` with a stable `Kind`
value; future component names can wrap that surface without changing the
read contract.

| Surface | Component | When | Source / contract |
|---|---|---|---|
| Unauthenticated | redirect | route requires `auth` and session is `UnauthenticatedSession` | `redirect('/auth/signin?returnTo=' + sanitizeReturnTo(location))` (`auth/returnTo.ts:5`) |
| Forbidden | `ConsoleStateView Kind="forbidden"` | authenticated gate denial or authenticated item/package read denial (scope, item-role action, share-tier, edition, entitlement, server read) | failed authenticated gate token from §4.6 or server/SDK unauthorized read result; `entitlement:*` and `edition:*` render with `LicenseEntitlementDecision.UpgradeMessage` (`LicenseModels.cs:147`); anonymous denials on anonymous-capable Share/Public/Catalog/Maps/Embed routes use `Kind="unavailable"` |
| Missing item | `ConsoleStateView Kind="missing"` | item id resolved to not found, or an anonymous open-data item URL resolves to an item that fails `open-data` eligibility | item-kind hint: map, service, layer, app, dashboard, report; open-data failures use generic public-not-found copy |
| Unsupported service metadata | `ConsoleStateView Kind="unsupported-service"` | service metadata schema not yet supported by Console (e.g. pre-Metadata v2) | shared between `/catalog/:idOrSlug`, `/maps/new?from=:itemId`, and Studio "open from catalog" |
| Unsupported package binding | `ConsoleStateView Kind="unsupported-package"` | generated-app, generated Studio package, or saved-map package newer than Console runtime understands | used on `/studio/apps/:itemId/preview`, `/studio/map`, `/maps/:mapId`, and `/embed/maps/:mapId` (`/studio/app`, `/studio/form`, `/studio/query`, `/studio/analysis`, `/studio/dashboard`, and `/studio/report` are server-bound and render their own missing-binding / capability-state surface instead) |
| Empty state | `ConsoleStateView Kind="empty"` | list/query returned zero rows | per-area copy + CTA; areas: catalog, studio, share, operate, workspace, groups |
| Loading | `ConsoleStateView Kind="loading"` | route mounted, content pending | never blocks shell paint |
| Errored session | `<SessionErrorView retry>` | session is `ErroredSession` | distinct from unauthenticated; renders diagnostic id |
| Unavailable (anonymous) | `ConsoleStateView Kind="unavailable"` | anonymous read or authorization denial on `/share/*`, `/public*`, `/catalog/:idOrSlug`, `/maps/:mapId`, or `/embed/maps/:mapId`, including private/org/group content, protected content, missing or invalid public-link tokens, failed embed authorization, and entitlement/license denials before authentication | does not reveal protected titles, token validity, or upgrade copy to anonymous users (§13 Q5) |
| Unsupported native capability | native host-support panel / native-only disabled action | browser host reaches `/environments*` or `/operate/native-stream`, or a native-only action is visible without a registered native service | native gRPC, native mTLS, certificate selection, connect/disconnect, trust validation, and profile creation render as "Native host only" or unavailable states; the web host keeps no MAUI, native-core, or `Grpc.Net.Client` dependency |

The catalog/share route-slice client returns `CatalogItemReadResult` or
`MapPackageReadResult` with `Status` values `Allowed`, `Missing`,
`Forbidden`, `Unavailable`, `UnsupportedServiceMetadata`, and
`UnsupportedPackageBinding`. `AnonymousRead` controls whether Studio,
Share, and permission details are hidden after an otherwise allowed
public or public-link read.

---

## 8. Frozen URLs (Cannot 3xx)

External consumers (third-party sites, DCAT-US/data.json crawlers,
embed iframes inlined into customer pages) have already inlined some
Honua URLs and cannot be expected to follow redirects. Those URLs must
resolve with a 200 at the frozen path. When both a legacy path and a
Console path exist, both paths must serve the same eligible content with
a 200.

| URL | Reason | Status |
|---|---|---|
| `/embed/maps/:mapId` | inlined in third-party iframes | served with a 200 at the same frozen path; public embeddable maps may render tokenless, token-authorized embeds keep the bearer in `#embedToken=` so it is never sent to the server, and `extent=W,S,E,N` is WGS84 lon/lat |
| `/public/items/:idOrSlug` and `/share/public/items/:idOrSlug` | item detail URLs are emitted into DCAT-US / data.json / schema.org contexts, and some crawlers may not follow 3xx | served at both legacy and Console paths with a 200; eligibility still requires `open-data` (§6.6); canonical links in newly generated documents point at `/share/public/items/:idOrSlug` |
| `/public` (root open-data) | legacy Portal collection root; newly generated links use `/share/public` | served as a compatibility alias by the `honua-console#34` Blazor route slice; edge-level 301 may replace the alias only after the compatibility window is closed |

Edge config for the no-3xx frozen URL behavior is flagged for
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
| shell + auth | `/`, `/auth/*`, `/groups`, `*` |
| studio | `/studio`, `/studio/query`, `/studio/analysis`, `/studio/map`, `/studio/dashboard`, `/studio/report`, `/studio/form`, `/studio/app`, `/studio/proof`, `/studio/apps/:itemId/preview` |
| catalog | `/catalog`, `/catalog/:idOrSlug` |
| viewer | `/maps/:mapId`, `/maps/new` |
| operate | all `/operate/*` |
| share | `/share`, `/share/public`, `/public`, `/share/public/items/:idOrSlug`, `/public/items/:idOrSlug` |
| embed | `/embed/maps/:mapId` |

The embed chunk explicitly excludes shell chrome, Studio prompt UI,
Operate workspace, and Catalog search UI. The share chunk excludes
Studio and Operate.

---

## 10. Smoke Evidence Map

The cross-surface smoke (`honua-console#9`) follows this pipeline.
Each step names the content kind it acts on, because the published
service and the Studio-generated artifact are routed through different
surfaces (the generated artifact is `type: "app"` with
`access.openData: false` per
`honua-portal:src/generated-apps/lifecycle.ts:66/107`, so it is not
eligible for `/share/public/items/:idOrSlug` per §6.6).

1. **POST publish service** — operator publishes a service via
   `/operate/publishing` (or the equivalent SDK control-plane call
   under `HONUA_CONTROL_PLANE_BASE_PATH` = `/api/v1/admin`). The
   service is published as public open-data (`access.sharing = public`,
   `access.openData = true`, `type = "service"`) so it satisfies
   `isPublicOpenDataItem` per §4.3.
2. **`/catalog/:id`** (service) — the published service appears as a
   catalog item; Catalog detail loads. Smoke labels: `catalog-detail`
   (row 9), `catalog-list` (row 8) if the smoke walks the list first.
3. **`/studio?source=map&itemId=:mapId`** →
   **`/studio/apps/:itemId/preview`** — Studio prompt
   generates a saved map / dashboard / app from the catalog item; apply
   succeeds; preview renders. Smoke label: `studio-generation` (rows
   12–13).
4. **Share / embed surfaces** — three distinct routes, one per
   eligible content kind. Together they exercise the remaining smoke
   labels.
   - **`/share/public/items/:sourceServiceId`** — open-data detail
     for the **source service** from step 1 (eligible because
     `type = "service"` and the publish step set
     `openData = true`/`sharing = public`). Smoke labels: `open-data`,
     `share` (row 6). The Studio-generated artifact is **not** routed
     here; it fails `isPublicOpenDataItem` on both `openData` and
     `type`.
   - **`/maps/:mapId`** — viewer load of the generated saved map
     (authenticated path uses the server-owned saved-map/package read
     path; anonymous-via-token path uses `?token=<value>` per §2.5 /
     §6.4). Smoke label: `viewer` (row 11).
   - **`/embed/maps/:mapId`** — anonymous embed of the generated
     saved map (`resolveEmbedAuthorization`; token-authorized embeds
     keep the bearer in the `#embedToken=` fragment per §8). Smoke
     label: `embed` (row 7).

Route rows in §3 carry smoke labels matching this pipeline:
`catalog-list`, `catalog-detail`, `viewer`, `studio-generation`,
`open-data`, `share`, `embed`. Migration tickets `#4`, `#5`, and `#9`
must preserve evidence emission at each label and must not route
generated artifacts through `/share/public/items/:idOrSlug` (§6.6).

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
| `honua-sdk-dotnet#231` | Projects the Operate observability admin contracts currently mirrored by `OperateObservabilityContracts.cs` for events, logs, alerts, rules, zones, jobs, and investigations. |
| `honua-sdk-js#225` | Projects `resolvePortalItemRole`, `evaluateShareEscalation`, `resolveEmbedAuthorization`, `buildShareLink`, open-data predicates/constants, and metadata/content/package DTOs for generated apps, embeds, MCP/QGIS/browser integrations, and map/chart/editor interop. Until landed, Portal helpers remain behavior references (§4.7). |
| `honua-server#1171` | Backs native mTLS/client-certificate validation for `/environments*` diagnostics. Console calls `POST /api/v1/admin/security/client-certificates/validate` through the temporary `Honua.Console.Contracts` shim until the .NET SDK projects the trust client. |
| `honua-server-admin#96` | Prepare legacy Admin for the `/operate/legacy/*` iframe container; ensure session cookie domain and CSP frame-ancestors allow the Console origin. |
| `honua-devops#55` / `honua-devops#56` | Edge config that preserves the §8 frozen URLs at both paths with a 200; preview/release pipeline that bundles Console + Admin into one origin. |
| `honua-server#969` | Backs the `/admin/identity/api-keys` RETIRE decision. |

---

## 12. Migration Ticket Cross-Link Map

The following migration tickets cite specific sections of this map.
PRs that touch each ticket should link to its section number(s).

| Ticket | Cites |
|---|---|
| `honua-console#4` — Port Catalog, Viewer, Saved Maps, Share, Embed, Open Data | §1 (taxonomy: `/catalog`, `/maps`, `/share`, `/embed`), §3 rows 5–11 and row 14 (`/data` → `/catalog`; the #34 route slice now includes `type=dataset` while broader dataset-like multi-type filtering remains deferred), §5.4 REDIRECTs (`/layers`, `/services`, `/layers/{id}/preview`), §6.3 / §6.4 / §6.6 / §6.7 (route catalogue), §7 (exception surfaces), §8 (frozen URLs), §9 (chunks), §10 (smoke). |
| `honua-console#5` — Port Studio app-builder and generated-app lifecycle | §1 (`/studio*`), §3 rows 12–13, §5.2 RETIRE (`/operator/app-builder`), §6.2 (route catalogue), §7 (`ConsoleStateView Kind="unsupported-package"`), §9 (studio chunk), §10 (studio-generation smoke). |
| `honua-console#6` — Integrate legacy Admin as transitional Operate surface | §1 (`/operate/*`, `/operate/legacy/<path>`), all of §5 (Admin disposition map), §6.5 (Operate gates and surfaces), §11 (`honua-server-admin#96` consumer notes). |
| `honua-console#7` — Wire Console to shared metadata / content / package / RBAC contracts | §4 (full RBAC and entitlement reference), §6 per-route gates, §11 (`honua-server#1162`, `honua-sdk-dotnet#166`, `honua-sdk-js#225` consumer notes). |
| `honua-console#9` — Console parity smoke | §10 (smoke evidence map), §3 rows carrying smoke labels. |
| `honua-console#41` — Native Blazor Operate observability checkpoint | §1 (`/operate/observability`, `/operate/events/:eventId`, `/operate/alerts/:alertId`, `/operate/jobs/:jobRunId`), §6.5 (Operate detail-route and neutral-state behavior), and [Operate Observability Information Model](./architecture/operate-observability-information-model.md). |
| `honua-console#44` — Native Console profiles trust diagnostics and mTLS | §1 (`/environments`, `/environments/new`, `/environments/:profileId`, `/operate/native-stream`), §2.2 (native host support navigation), §6.1 (host-capability seam and profile routes), §6.5 (`/operate/native-stream` exception), §7 (unsupported native capability), and [Optional MAUI Blazor Hybrid Host](./native/MAUI_BLAZOR_HOST.md). |
| `honua-console#24` — Server-backed Operate observability binding | §6.5 (`IConsoleOperateObservabilityClient`, admin API status contract, event/alert/job detail behavior), §11 (`honua-sdk-dotnet#231` consumer note), and [Operate Observability Information Model](./architecture/operate-observability-information-model.md). |

`honua-console#2` (scaffold) seeds the `<RouteGuard>`, the exception
surface components in §7, the chunk boundaries in §9, the redirect
behavior in §3 and §5, and the `/groups` placeholder route (§3 row 15,
§6.1) — including its empty-state copy that mirrors current Portal until
a dedicated Groups feature ticket lands alongside `honua-portal#15`.

---

## 13. Open IA Questions and Default Decisions

Each question from the design review carries a default decision so
migration tickets are not blocked. Defaults flip to "confirmed" once
the human review answers land in
`honua-agentflow:.agentflow/runtime/honua-console-3/answers.md`.

| # | Question | Default decision | Status |
|---|---|---|---|
| Q1 | Studio root path: `/studio` vs `/studio/proof` | `/studio` is the entry; `/studio/proof` is a legacy alias, and `/studio/drafts` starts source-scoped drafts. The first Console-native package editor sub-routes from `honua-console#39` are `/studio/query`, `/studio/analysis`, `/studio/map`, `/studio/dashboard`, `/studio/report`, and `/studio/app`; `/studio/form` is the server-bound `honua-console#57` form builder. Broader list/marketplace generator routes stay deferred to their owning tickets. | default |
| Q2 | Saved-map list: `/catalog?type=map` vs `/studio/maps` vs `/catalog/maps` | `/catalog?type=map` (Catalog filter). Avoids two list surfaces; preserves existing query-param contract. | default |
| Q3 | `/share/public` vs `/public` redirect semantics | `honua-console#34` serves `/public` as a compatibility alias for `/share/public` in the Blazor route slice. No 3xx for `/embed/maps/:mapId`; 200-at-both-paths for open-data item detail URLs (`/public/items/:idOrSlug` plus `/share/public/items/:idOrSlug`) because item URLs appear in DCAT-US / data.json / schema.org contexts. | default updated by #34 |
| Q4 | `/operate` visibility for cross-workspace admins | `canSeeOperatorLinks(session)` evaluated against the active workspace; switching workspaces re-evaluates. | default |
| Q5 | Anonymous user on a gated `/share/*` link | Generic `ConsoleStateView Kind="unavailable"` for anonymous; upgrade tile only when authenticated. | default updated by #34 |
| Q6 | Admin EMBED container path: `/operate/legacy/<path>` vs `/operate/admin-legacy/<opaque>` | One-to-one `/operate/legacy/<path>` to preserve deep-link history. | default |
| Q7 | SDK projection timing | Blazor Web Console treats Portal RBAC, share-link, embed, and open-data helpers as behavior references until `honua-sdk-dotnet#166` projects server-owned route/RBAC/content DTOs; JS runtimes use `honua-sdk-js#225` once available. Bounded interim, not a cross-repo blocker. | default |
| Q8 | Cross-repo scope | This ticket changes only `honua-console`. No bounded child tickets filed beyond the existing external consumer tickets in §11. | confirmed |

---

## 14. Maintenance

This document is the source of truth for Console IA. When a Portal or
Admin route is added, removed, or repositioned, the corresponding
section here must be updated in the same PR. Migration tickets that
diverge from this map without first updating the map should be sent
back for revision.

When `honua-sdk-dotnet#166` and `honua-sdk-js#225` land, §4.7 and §11
update to reflect that Console consumes the shared .NET and JS SDK
projections rather than treating `honua-portal` helpers as behavior
references. The Portal source rows in §4 remain as a historical pointer
until `honua-console#10` retires Portal.
