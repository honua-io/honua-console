# Layer / Service Metadata: Console UI Gap Analysis

> Read-only investigation. Server repo: `honua-srv-finish`. Console repo: `honua-console-metrics` (Blazor Server, `src/Honua.Console.Shell`). All citations are `file:line`.

## Status (2026-06-09) — gaps closed

Most of the gap below has been CLOSED. Console branch `feat/operate-metrics-viewer`; server branch `feat/finish-workflows-server` (new admin endpoints) — both committed, not pushed. ~70 new tests; console integration suite 532 pass / 1 pre-existing-flake / 56 skip.

- **Bucket 3-A (8 items, server PUT already existed → console-only): ALL CLOSED.** field alias + hidden; access-by-role; MapServer render settings (honest 501 handling); per-layer popup-info; drawing-info/renderer (real source replacing the Unsupported one); relationships editor (`/operate/layers/{id}/relationships`); time-info setter (on the Temporal page).
- **Bucket 3-B (needed new server endpoints): the high-value typed-metadata items CLOSED.** New server admin endpoints added (`AdminLayerMetadataAuthoringEndpoints.cs` + `ServiceSettingsEndpoints` extensions): `PUT .../layers/{id}/display` (min/max scale, default visibility, display field, queryable, hasZ/M), `/editing` (editor-tracking + edit caps), `/discovery` (layer + service: keywords/license/attribution/publisher/contact/links/title/description), `/spatial` (supportedCrs/storageCrs/epoch), `/services/{svc}/settings-caps` (maxRecordCount etc.), and the fields PUT extended for RANGE domains + per-field defaultValue + merge/split policy. Console UI: new `OperateLayerMetadataPage` (`/operate/layers/{id}/metadata`), `OperateDiscoveryMetadataPage`, a settings-caps panel + the extended fields editor.
- **The tail — NOW ALSO CLOSED.** subtypes (#11), Arcade attribute rules (#12), 3D extrusion/symbology (#19), lifecycle status (#20): new server endpoints `AdminLayerAdvancedMetadataAuthoringEndpoints.cs` (`PUT .../layers/{id}/subtypes|attribute-rules|extrusion|status`) + console pages (`OperateLayerSubtypesPage`, `OperateLayer3DPage`). Publication-level overrides (#17): new `PUT .../publications/{id}/overrides` + `OperatePublicationOverridesPage`. Permanent filter (#18): the server endpoint already existed (`.../layers/{id}/filter`); wired `OperateLayerFilterPage`. → **ALL 20 gap items are now configurable in the UI.**
- **Deploy note:** the new server endpoints are in source on `feat/finish-workflows-server` but the running testbed server is the OLD build — the console UI is wired + unit-tested against the contracts, but end-to-end use requires rebuilding/redeploying the server.

## Executive summary

The Honua server stores a rich, fully-typed layer/service metadata model — the **Metadata v2 graph** (`MetadataV2Resource` / `MetadataV2Service` / `MetadataV2Publication` and their typed sub-records in `src/Honua.Core.Abstractions/Features/Metadata/Domain/V2/`). It is served on the FeatureServer / MapServer / OGC API / OData surfaces, and a slice of it is *editable* through the server admin API (`src/Honua.Server/Features/Admin/*Endpoints.cs`).

The console exposes only a **small fraction** of that model for authoring. Concretely, the console admin client (`OperateAdminShims.cs`) wires exactly **five** server endpoints:

- `GET  /api/v1/admin/services` (list)
- `GET  /api/v1/admin/services/{service}/settings`
- `PUT  /api/v1/admin/services/{service}/protocols`
- `PUT  /api/v1/admin/services/{service}/access-policy`
- `GET/PUT /api/v1/admin/metadata/layers/{id}/fields`

Everything the operator can author today reduces to: **enabled protocols**, **anonymous read/write access**, **coded-value field domains**, plus the **layer publish** request (name/geometry/SRID/PK/field-selection/enabled) and the **canonical style body** over `/ogc/styles`.

The most material gaps: the server already ships PUT endpoints for **popup info**, **drawing info (renderer)**, **relationships**, **time-info (start/end/track fields)**, **MapServer render settings**, **per-layer access policy**, and **field alias / visibility (hidden)** — but the console has **no client wired to any of them**. A large set of typed model fields (min/max scale, default visibility, display field, editor-tracking fields, attribution/license/keywords/contact, supported/storage CRS, subtypes, attribute rules, extrusion/3D symbology, service settings caps like maxRecordCount) have **no authoring surface at all** and in most cases no server *write* endpoint either.

---

## The full server metadata model (authoritative enumeration)

Source of truth is the Metadata v2 graph. Field-by-field, with where it lives:

### Identity / discovery — `MetadataV2ObjectMetadata` (shared by every entity)
`src/Honua.Core.Abstractions/Features/Metadata/Domain/V2/MetadataV2Common.cs:34`
- `id`, `name`, `namespace`, `title`, `description` (`:47`–`:71`)
- `tags`, `labels`, `annotations` (`:77`–`:99`)
- `generation`, `createdAt`, `updatedAt` (`:108`–`:121`)
- `keywords` (`:128`), `themes` (`:140`), `language` (`:152`)
- `license` (SPDX) (`:161`), `attribution`/credits (`:169`), `publisher` (`:177`)
- `contactPoint` {name,email,url} (`:185`, record at `:302`)
- `links[]` {href,rel,type,title,hreflang} (`:192`, record at `:320`)

### Resource (the canonical "layer") — `MetadataV2Resource`
`MetadataV2Common.cs` is fields; resource shell in `...Domain/V2/MetadataV2Graph.cs:135`
- `type` (FeatureDataset/Style/etc.) (`MetadataV2Graph.cs:155`)
- `storageBindingIds` / `primaryStorageBindingId` (`:165`,`:177`)
- `schemaFields[]` → `MetadataV2Field` (`:188`)
- `policyIds[]` (`:198`), `accessPolicy` (resource-level) (`:223`)
- `spatial` → `MetadataV2ResourceSpatial` (`:230`)
- `temporal` → `MetadataV2ResourceTemporal` (`:237`)
- `permanentFilter` (`:245`)
- `subtypes` → `MetadataV2Subtypes` (`:254`)
- `attributeRules[]` → `MetadataV2AttributeRule` (`:264`)
- `extrusion` → `MetadataV2ExtrusionInfo` (`:274`)
- `symbology3D` (`:285`)
- `styleResourceIds[]` (`:293`) and inline `style` (`:304`)
- `display` → `MetadataV2ResourceDisplay` (`:311`)
- `editing` → `MetadataV2ResourceEditing` (`:318`)
- `status` (lifecycle/state/conditions) (`:324`)
- `relationships[]` → `MetadataV2Relationship` (`:208`)

### Field — `MetadataV2Field` (`MetadataV2Common.cs:347`)
`semanticId`, `name`, `type`, `title`, `description`, `nullable`, `semanticRoles[]`, `alias` (`:403`), `editable` (`:414`), `length` (`:433`), `defaultValue` (`:440`), `domain` (`:446`), `hidden` (`:452`), `sqlType` (`:460`).
- Domain → `MetadataV2FieldDomain` {name,type=codedValue|range,codedValues[],range[],mergePolicy,splitPolicy} (`:474`)
- `MetadataV2Subtypes` {subtypeField,defaultSubtypeCode,subtypes[]} (`:536`); per-subtype field overrides (`:589`)
- `MetadataV2AttributeRule` {name,type,fieldName,scriptExpression,triggeringEvents,errorMessage,isEnabled} (`:618`)

### Display hints — `MetadataV2ResourceDisplay` (`MetadataV2Graph.cs:344`)
`minScale` (`:347`), `maxScale` (`:351`), `defaultVisibility` (`:355`), `displayField` (`:362`), `queryable` (`:366`), `hasZ` (`:370`), `hasM` (`:373`).

### Editing hints — `MetadataV2ResourceEditing` (`MetadataV2Graph.cs:382`)
`globalIdField`, `creatorField`, `createdAtField`, `editorField`, `updatedAtField`, `canModify`, `supportsAttachments`, `supportsRelatedRecords` (`:389`–`:417`).

### Spatial — `MetadataV2ResourceSpatial` + refs (`...Domain/V2/MetadataV2Spatial.cs`)
- `spatialReference` {srid,crs,isGeographic} (`:14`)
- `geometryType` (`:112`), `bbox`/extent (`:116`), `primaryGeometryField` (`:126`)
- `supportedCrs[]` (`:136`), `storageCrs` (`:146`), `storageCrsCoordinateEpoch` (`:155`)

### Temporal — `MetadataV2ResourceTemporal` (`...Domain/V2/MetadataV2Temporal.cs:45`)
`startTimeField`, `endTimeField`, `trackIdField`, declared `extent` (`:47`–`:65`).

### Relationship — `MetadataV2Relationship` (`...Domain/V2/MetadataV2Relationship.cs:21`)
`id`, `name`, `description`, `relatedResourceId`, `role`, `cardinality`, `originField`, `destinationField`, `esriRelationshipId`.

### Style — `MetadataV2ResourceStyle` (`...Domain/V2/MetadataV2Style.cs:15`)
`title`, `abstract`, `legendUrl`, `styleVersion`, `encodings[]` (mapbox-style / sld / `esri-drawing-info` / `esri-image-renderer` / `3d-tiles-styling`) (`:50`). **This is where the renderer/drawing-info and popup styling live.**

### Service — `MetadataV2Service` (`MetadataV2Graph.cs:560`)
`serviceType` (`:576`), `route` (`:582`), `publicationIds[]` (`:588`), `accessPolicy` (`:599`), `spatialReference` (output CRS) (`:608`), `protocols[]`/`enabledProtocols` (`:618`,`:630`), `options`, `settings` → `MetadataV2ServiceSettings` (`:654`), `status`.
- `MetadataV2ServiceSettings` (`:681`): `maxRecordCount`, `defaultRecordCount`, `maxImageWidth/Height`, `defaultDpi`, `maxFeaturesPerLayer`, `defaultFormat`, `supportedFormats[]`, `defaultTileMatrixSet`, `supportsAttachments`, `maxAttachmentSizeBytes`, `queryTimeoutMs`, `maxEditsPerTransaction`, `maxPayloadBytes`.

### Publication — `MetadataV2Publication` (`MetadataV2Graph.cs:743`)
`resourceId`, `serviceId`, `storageBindingId`, `publicationType`, `titleOverride` (`:782`), `identifier`/`layerIndex`/`path`, `isPrimary`, `supportedFormats[]`, `fieldAliases` (per-publication) (`:859`), `capabilities[]`, `options`.

### Storage binding / connection
`MetadataV2StorageBinding` (`:478`): `storageType`, `locator`, `storageLayerId`, `capabilities[]`. `MetadataV2Connection` (`:424`): `type`, `provider`, `endpoint`, `secretRef`, `options`.

### Server *write* endpoints that exist today
- `ServiceSettingsEndpoints.cs:43-67`: list, get settings, **PUT protocols**, **PUT mapserver**, **PUT access-policy**, **PUT timeinfo**, **PUT layers/{id}/metadata** (access-policy + timeinfo + raster mosaic). Models in `Honua.Server/Features/Admin/Models/ServiceSettingsModels.cs`.
- `AdminLayerFieldConfigurationEndpoints.cs:40-45`: GET/**PUT** layer fields (alias, coded-value domain, hidden) — request `LayerFieldConfigurationUpdate(name,alias,domain,hidden)` (`:94-100`).
- `AdminLayerStyleEndpoints.cs:38-44`: GET/**PUT** layer style.
- `AdminLayerAuthoringEndpoints.cs:38-43`: GET/**PUT popup-info**, GET/**PUT drawing-info**, GET/**PUT relationships**.
- `AdminSldStyleEndpoints.cs`, `AdminStyleSuggestionEndpoints.cs`: SLD + style suggestions.
- `LayerPublishingEndpoints.cs:41-78`: list/publish layer, set enabled, refresh extents, validate table. Publish request `LayerPublishRequest` (`Honua.Core/Features/Admin/Domain/LayerPublishingModels.cs:9`) also accepts `FieldDomains`, `Subtypes`, `AttributeRules` (`:79`,`:90`,`:99`) — but these are populated only by the Esri-import path, not by an interactive author.

---

## Bucket 1 — Configurable in the UI (author can set/change)

| Metadata field | Server contract | Console route / component |
|---|---|---|
| **Enabled protocols** (`Service.protocols`) | `PUT /api/v1/admin/services/{svc}/protocols` (`ServiceSettingsEndpoints.cs:51`) | `Pages/OperateServiceDetailPage.razor:239` (`SaveProtocolsAsync`); client `Services/HonuaServerServiceConfigurationOperation.cs` |
| **Service access — anonymous read/write** (`Service.accessPolicy.allowAnonymous*`) | `PUT .../access-policy` (`ServiceSettingsEndpoints.cs:59`) | `OperateServiceDetailPage.razor:261` (`SaveAccessAsync`) — **only the two anonymous flags** are bound |
| **Field coded-value domain** (`Field.domain`, codedValue) | `PUT /api/v1/admin/metadata/layers/{id}/fields` (`AdminLayerFieldConfigurationEndpoints.cs:45`) | `Pages/OperateLayerDetailPage.razor:206` (`SaveDomainAsync`); client `Services/...ConsoleLayerFieldsOperation`, contract `OperateAdminShims.cs:757` |
| **Canonical style body** (`ResourceStyle.encodings[]` — MapLibre + Esri drawingInfo) | `GET/PUT /ogc/styles/{styleId}` (ADR-0048/0002) | `Pages/StudioStyleEditorPage.razor` (dual-mode MapLibre/Esri editor); client `IHonuaOgcStylesClient` (`StyleCatalogShims.cs:81-95`) |
| **Layer publish core** (name, description, geometryColumn/type, SRID, primaryKey, field selection, enabled, service name) | `POST /api/v1/admin/.../layers` (`LayerPublishingEndpoints.cs:45`) | `Pages/OperatePublishLayerPage.razor`; command `Models/ServiceLayerPublishModels.cs:8` |
| **Layer enabled/disabled** (`Publication`/serving) | `PUT layers/{id}/enabled` (`LayerPublishingEndpoints.cs:55`) | publish flow / publishing page |

Note: the publish core covers only the fields on `LayerPublishRequest`; it does **not** let the author set domains, subtypes, or attribute rules interactively even though the request type can carry them.

---

## Bucket 2 — Display-only (shown but not editable)

| Metadata field | Where shown |
|---|---|
| **Field alias** (`Field.alias`) | `OperateLayerDetailPage.razor:98` — rendered in the fields table; the PUT contract supports alias (`OperateAdminShims.cs:1424`) but the page UI binds only the domain, never alias. |
| **Field type, name, domain summary** | `OperateLayerDetailPage.razor:96-107` (read-back). |
| **MapServer render settings** (maxImage W/H, defaultDpi, defaultFormat, defaultTransparent, maxFeaturesPerLayer) | Surfaced in the settings *response* model (`OperateAdminShims.cs:1700+`, `ServiceSettingsModels.cs:165`) and shown as runtime settings rows in `OperateServiceDetailPage.razor:39-47`; **no PUT call** from the console (server has `PUT .../mapserver`). |
| **Service access roles** (`AllowedRoles`, `AllowedWriteRoles`) | Read into the settings view and rendered as text (`HonuaServerOperateTransitionDataSource.cs:751-756`); the access-policy editor binds only anonymous flags. |
| **Time-info** (start/end/track fields) | Read model exists (`HonuaAdminTimeInfoResponse`, `OperateAdminShims.cs:1732`); Temporal viewer (`OperateTemporalPage.razor`) shows history/diff/rollback but never **sets** the time fields. |
| **Geometry type, SRID, extent, runtime status** | `OperateLayerDetailPage.razor:34`, layer detail panels; read-only. |
| **Resource validation / lifecycle / blast radius** | `OperateResourceDetailPage.razor:24-60` — entirely read-only; "edit tabs" are anchor links only. |
| **Per-slot style + popup override UI** | `OperateLayerStylePage.razor` renders a full style-override + popup-template form, but its data source is `UnsupportedOperateLayerStyleOverrideDataSource` (`Services/UnsupportedOperateLayerStyleOverrideDataSource.cs:13`), so read+save always return *missing-binding*. Effectively non-functional, not display-only — listed here because the controls are visible. |

---

## Bucket 3 — NOT configurable via the UI (THE GAP)

Ordered by how cheaply each could be closed (server contract already exists → highest priority).

### A. Server already exposes a write endpoint; console has no client wired

1. **Popup info (display field template / popupInfo)**
   - Server: `PUT /api/v1/admin/metadata/layers/{id}/popup-info` (`AdminLayerAuthoringEndpoints.cs:39`). Backs the FeatureServer/MapServer `popupInfo`/`displayField`.
   - Console: the *only* popup UI is the per-slot override on `OperateLayerStylePage.razor:88`, which is wired to the **Unsupported** source. No client calls `/popup-info`.
   - Why it matters: popups are the single most-requested layer-presentation knob; the server is ready and the UI shell already exists.

2. **Drawing info / renderer (Esri symbology)**
   - Server: `PUT .../layers/{id}/drawing-info` (`AdminLayerAuthoringEndpoints.cs:41`).
   - Console: the Studio style editor writes the *canonical* style over `/ogc/styles` (`StudioStyleEditorPage.razor`), but there is no per-layer drawing-info authoring wired to this admin endpoint. The per-slot style override that would target it is Unsupported.
   - Why it matters: layer-scoped renderer overrides without touching the canonical style.

3. **Relationships (origin/destination, cardinality, esriRelationshipId)**
   - Server: model `MetadataV2Relationship.cs:21`; `GET/PUT .../layers/{id}/relationships` (`AdminLayerAuthoringEndpoints.cs:43`).
   - Console: no relationships editor anywhere (grep for `relationship` in console hits only temporal/SDK shims). The task brief expected a "relationships admin setter" — it does not exist in the console.
   - Why it matters: related-records / `$expand` / FeatureServer relationshipQuery depend on this; fully unauthorable.

4. **Time-info (startTimeField / endTimeField / trackIdField)**
   - Server: `MetadataV2ResourceTemporal.cs:45`; `PUT .../services/{svc}/timeinfo` and `PUT .../layers/{id}/metadata` (`ServiceSettingsEndpoints.cs:63,67`; request `UpdateTimeInfoRequest` `ServiceSettingsModels.cs:96`).
   - Console: read-only in the Temporal viewer; no setter.
   - Why it matters: time-enabling a layer is a server-supported PUT today with zero console coverage.

5. **MapServer render settings** (maxImage W/H, defaultDpi, defaultFormat, defaultTransparent, maxFeaturesPerLayer)
   - Server: `PUT .../services/{svc}/mapserver` (`ServiceSettingsEndpoints.cs:55`; `UpdateMapServerSettingsRequest` `ServiceSettingsModels.cs:206`).
   - Console: shown as read-only runtime rows; no editor.

6. **Service & layer access by ROLE** (`AllowedRoles`, `AllowedWriteRoles`)
   - Server: `UpdateAccessPolicyRequest` supports role lists (`ServiceSettingsModels.cs:86-90`); same PUT already wired for anonymous flags.
   - Console: editor binds only `allowAnonymous` / `allowAnonymousWrite` (`OperateServiceDetailPage.razor:120-126`); role arrays are display-only. Closing this is a pure UI change — no new server contract.

7. **Per-layer (resource) access policy + raster mosaic merge strategy**
   - Server: `PUT .../services/{svc}/layers/{id}/metadata` (`ServiceSettingsEndpoints.cs:67`; `UpdateLayerMetadataRequest`/`UpdateRasterMosaicRequest` `ServiceSettingsModels.cs:132,156`).
   - Console: no layer-scoped access-policy or mosaic editor.

8. **Field alias & visibility (hidden)**
   - Server: same fields PUT already wired for domains (`AdminLayerFieldConfigurationEndpoints.cs:94` carries alias + hidden).
   - Console: the fields PUT contract includes `Alias`/`Hidden` (`OperateAdminShims.cs:1424-1429`) but `OperateLayerDetailPage.razor` only authors the domain. Alias is display-only; hidden is unauthorable. Closing this is UI-only.

### B. Stored & served, but NO server write endpoint AND no UI (deeper gap — would need a server contract too)

9. **Display hints** — `minScale`, `maxScale`, `defaultVisibility`, `displayField`, `queryable`, `hasZ`/`hasM` (`MetadataV2ResourceDisplay`, `MetadataV2Graph.cs:344`). Served on FeatureServer/MapServer layer JSON; no admin PUT, no UI. Visibility scale ranges and default visibility are common operator asks.

10. **Editor-tracking & edit capability** — `globalIdField`, `creator/editor/created/updatedAtField`, `canModify`, `supportsAttachments`, `supportsRelatedRecords` (`MetadataV2ResourceEditing`, `MetadataV2Graph.cs:382`). No authoring path.

11. **Subtypes** (`MetadataV2Subtypes`, `MetadataV2Common.cs:536`) — only importable from Esri (`LayerPublishRequest.Subtypes`); no interactive authoring or admin PUT.

12. **Attribute rules** (calculation/constraint/validation Arcade rules, `MetadataV2AttributeRule`, `MetadataV2Common.cs:618`) — Esri-import only; no UI.

13. **Field-domain RANGE type, defaultValue, mergePolicy/splitPolicy** — console authors only `codedValue` domains (`OperateAdminShims.cs:1399-1402`); range domains, per-field default values, and merge/split policy are unauthorable.

14. **CRS authoring** — `supportedCrs[]`, `storageCrs`, `storageCrsCoordinateEpoch`, service output `spatialReference` (`MetadataV2Spatial.cs:136-155`, `MetadataV2Graph.cs:608`). SRID is set once at publish; the supported-CRS list and output CRS are not authorable.

15. **Discovery / catalog metadata** — `keywords`, `themes`, `language`, `license`, `attribution`/copyright, `publisher`, `contactPoint`, `links[]`, `tags`/`labels`/`annotations`, `title`/`description` post-publish (`MetadataV2ObjectMetadata`, `MetadataV2Common.cs:34`). These drive OGC API Records / STAC / DCAT / Esri documentInfo output. None are editable after publish; description/title are publish-time only.

16. **Service settings caps** — `maxRecordCount`, `defaultRecordCount`, `maxFeaturesPerLayer`, `queryTimeoutMs`, `maxEditsPerTransaction`, `maxPayloadBytes`, `supportedFormats`, `defaultFormat`, `defaultTileMatrixSet`, attachment caps (`MetadataV2ServiceSettings`, `MetadataV2Graph.cs:681`). Only the MapServer subset is even read back; none are editable.

17. **Publication-level overrides** — `titleOverride`, per-publication `fieldAliases`, `capabilities`, `supportedFormats`, `isPrimary` (`MetadataV2Publication`, `MetadataV2Graph.cs:782-866`). Not authorable.

18. **Permanent filter** (`MetadataV2PermanentFilter`, `MetadataV2Graph.cs:245`) — server-enforced query filter; the console has a `AdminLayerFilterConfigurationEndpoints` on the server side but no console editor wired for it.

19. **3D — extrusion & symbology3D** (`MetadataV2Graph.cs:274,285`) — no UI.

20. **Lifecycle status** (`MetadataV2Status` lifecycle Draft→Published) (`MetadataV2Common.cs:206`) — shown on the resource detail page, not settable.

---

## Prioritized top gaps worth closing

Ranked by value ÷ effort. The first cluster is cheap because the **server PUT already exists** — only a console `Server*` client + page wiring is needed.

1. **Field alias & hidden** *(UI-only; server PUT already wired)* — extend `OperateLayerDetailPage` to bind `alias`/`hidden` on the existing `/fields` PUT. Smallest possible win.
2. **Service/layer access by role** *(UI-only)* — bind `AllowedRoles`/`AllowedWriteRoles` on the existing access-policy PUT.
3. **Popup info** — wire a `Server*` client to `PUT .../layers/{id}/popup-info` and replace the Unsupported per-slot popup source. UI shell already exists in `OperateLayerStylePage`.
4. **Time-info (start/end/track)** — wire `PUT .../timeinfo` (or `/layers/{id}/metadata`); add a setter to the Temporal/layer surface.
5. **Drawing info / per-layer renderer** — wire `PUT .../layers/{id}/drawing-info`; replace the Unsupported per-slot style source.
6. **Relationships admin** — wire `GET/PUT .../layers/{id}/relationships`; net-new editor, but the server is ready.
7. **MapServer render settings** — wire `PUT .../mapserver`; the values are already read back, just not writable.
8. **Discovery metadata (keywords/license/attribution/contact/description) post-publish** — high product value for catalog output, but needs a *new* server write endpoint (no admin PUT today) plus UI.
9. **Display hints (min/max scale, default visibility, display field)** — needs a new server contract + UI.

Items 1, 2, 3, 4, 5, 7 require **no new server work** — the contracts in `ServiceSettingsEndpoints.cs` / `AdminLayerAuthoringEndpoints.cs` / `AdminLayerFieldConfigurationEndpoints.cs` are already shipped and untouched by the console. That is the clearest, lowest-risk way to close the gap.
