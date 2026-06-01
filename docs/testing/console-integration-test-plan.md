# Honua Console — End-to-End Operation→Output Integration Test Plan

Status: PLAN (analysis only; no product code changed)
Repos: `honua-io/honua-console` @ trunk `c8b2c18`, `honua-io/honua-server` @ trunk `c5b03723`
Author surface scanned: `src/Honua.Console.Shell/Services/*`, `src/Honua.Console.Contracts/*Shims.cs`, `src/Honua.Console.Shell/Pages/*`, `tests/Honua.Console.IntegrationTests/*`, `tests/Honua.Console.Native.Core.Tests/*`, `.github/workflows/console-nightly.yml`, and honua-server `src/Honua.Server/Features/Admin/*`, `src/Honua.Protocols.GeoServices/*`.

---

## 1. Goal & Principle

**Verify every console OPERATION produces the correct OUTPUT against a real honua-server.** When an operator configures / creates / publishes something in the console, the test must assert that it *actually landed and is configured correctly on the server*, and that the console *re-renders the new state* (never a missing-binding placeholder or fabricated demo data).

Round-trip contract: **config in → correct state out.**

Five binding rules every test in this suite MUST obey:

1. **Live honua-server, never a mock.** Boot the real server + PostGIS via Testcontainers (`HonuaServerTestcontainer`). In-memory/`Unsupported*` datasources are out of scope here — they have their own unit tests.
2. **Assert server-side state through a DIFFERENT read API than the one the operation went through.** This is the single most important rule and the biggest current gap (see §4). Today every live test seeds via a datasource and then asserts the *same datasource's projection* re-reads it — that proves the round-trip of one code path, not that the server is correctly configured. The new tests must, e.g., publish a layer through the console operation and then independently `GET /rest/services/{service}/FeatureServer/{layer}/query` to prove features are queryable with the right fields/SRID/extent.
3. **Assert console reflection too.** After the operation, render the relevant page (bUnit) and assert the new state appears — and that it is NOT the missing-binding surface.
4. **Run in the nightly lane** (`.github/workflows/console-nightly.yml`) which sets `HONUA_CONSOLE_RUN_LIVE_SERVER_TESTS=true` and pins the server image. All facts are `[SkippableFact]` so PR CI (no Docker) skips cleanly. Opt-in locally via the documented env (`HONUA_CONSOLE_RUN_LIVE_SERVER_TESTS`, `HONUA_CONSOLE_SERVER_IMAGE`, `HONUA_CONSOLE_ADMIN_API_KEY`, …).
5. **Evidence out.** Emit per-operation evidence (request, server-read response, assertions) to `artifacts/test-results` / `smoke-evidence/`, consistent with `ConsoleEndToEndSmokeEvidence`.

---

## 2. Harness Plan

### 2.1 Reuse the existing single live server
- **Server boot:** `tests/Honua.Console.IntegrationTests/HonuaServerTestcontainer.cs` already boots `postgis/postgis:16-3.4` + the pinned honua-server image on a shared Docker network, honoring `ConsoleTrustIntegrationOptions` (image, port, scheme, health path, env, admin key). Keep one container per test collection (xUnit `[Collection]` fixture) to amortize the ~minute boot, exactly like `PublishingWorkspaceFixture` / `StudioPackageLifecycleFixture` / `ShareAccessFixture`.
- **Dev-auth bypass:** nightly already sets `HONUA_DEV_AUTH=true;HONUA_DEV_AUTH_ALLOW_BYPASS=true;HONUA_REGISTER_TEST_INFRASTRUCTURE=true` so admin-gated routes are reachable; RBAC-enforcement tests (§5.1) need a *second* options profile that does NOT bypass, or a scoped low-privilege key.

### 2.2 New shared component — the **Admin Verification Client** (the missing piece)
Add `ServerStateVerifier` (a thin typed `HttpClient` wrapper) to the integration test project. It reads server state back through the canonical *server* read APIs — independent of the console datasource under test — and exposes typed assertions:
- `FeatureServerLayerMetadataAsync(service, layerId)` → `GET /rest/services/{service}/FeatureServer/{layerId}` (fields, geometryType, SRID/wkid, extent, capabilities).
- `FeatureServerQueryAsync(service, layerId, where, outFields)` → `GET .../FeatureServer/{layerId}/query` (feature count, attribute names/types, returned SRID).
- `AdminServiceSettingsAsync(serviceName)` → `GET /api/v1/admin/services/{serviceName}/settings`.
- `AdminLayersAsync(connectionId)` → `GET /api/v1/admin/connections/{id}/layers`.
- `CatalogContentAsync(id)` / `CatalogSearchAsync(q)` → `GET /api/v1/console/content/{id}` + `/search`.
- `PublicationAsync(id)` / `WorkflowPublicationsAsync()` / `ShareLinkAsync(id)` — for the other families.
- STAC/OGC reads (`/collections/{id}`, STAC item) for the share/open-data family.

This client is the verification oracle for rule #2. It is the central new build artifact.

### 2.3 Seed / teardown
- **Seed** through the real create POSTs (as `PublishingWorkspaceLiveServerTests.SeedPublicationAsync` already does), or — for layer publishing — seed a PostGIS table directly (the server connection points at the same `postgres` container) then exercise the console publish operation against it.
- **Teardown:** container disposal per collection; within a collection, use unique GUID-suffixed slugs/names (existing convention) so tests don't collide. Where the server supports delete/unpublish, exercise it as its own operation test and as cleanup.

### 2.4 Where they run
- Project: `tests/Honua.Console.IntegrationTests` (bUnit + Testcontainers already wired) and, for the source-build Operate/GitOps lanes, `tests/Honua.Console.Native.Core.Tests`.
- Lane: `console-nightly.yml` `live-integration` job (already runs both projects with the opt-in env). New collections inherit the lane automatically. Add a step-summary row per operation family.

---

## 3. Test Matrix (operation → output)

Legend — Coverage: **COVERED** (live round-trip exists), **PARTIAL** (live test exists but asserts only the same-path projection / render, not independent server state), **GAP** (no live operation→output test, or the console operation does not yet POST at all). Priority: P0 (flagship / highest value), P1, P2.

### Family A — Service-Layer Configuration & Publishing  *(FLAGSHIP — worked in full in §3.A)*
| Operation | Console method/page | Inputs | Server-side output + verifying API | Console reflection | Coverage | Pri |
|---|---|---|---|---|---|---|
| Quick-publish layer (Service→Layer→Review) | `PublishWizardWorkspace.razor` Quick mode `OnFinish` | connection, table, layer name, SRID, fields, capabilities | layer registered: `POST /api/v1/admin/connections/{id}/layers`; queryable via `GET /rest/services/{svc}/FeatureServer/{layer}/query`; metadata via `GET .../FeatureServer/{layer}` | publishing/services page shows the new layer slot live | **GAP** (wizard is UI scaffolding only: `OnFinish() => _published = true`, POSTs nothing) | **P0** |
| Author-first publish into a service slot | `PublishWizardWorkspace.razor` Author mode (Target→Compatibility→Slot→Fields→Projection→Access→Review) | source resource, slot, field map, projection, access | new Publication + slot bound; verify via `GET .../FeatureServer/{layer}` and `/query` | review card / matrix renders the slot | **GAP** (UI scaffolding only) | **P0** |
| Toggle layer enabled / disabled | (page TBD — server `PUT /admin/connections/{id}/layers/{layerId}/enabled`) | layerId, enabled | `GET /admin/connections/{id}/layers` shows enabled flag; `/query` 200 vs 4xx | layers page badge | **GAP** | P1 |
| Refresh layer extents | server `POST /admin/connections/{id}/layers/extents/refresh` | connectionId | `GET .../FeatureServer/{layer}` extent matches data bbox | layer detail extent | **GAP** | P1 |
| Edit service settings (protocols / mapserver / access-policy / timeinfo / layer metadata) | OperateServiceDetail (currently read-only) | service settings payloads | `GET /api/v1/admin/services/{svc}/settings` reflects change; capabilities flip on `/rest/services` | service detail re-renders | **GAP** (console reads settings only; no PUT wired) | P1 |
| Validate a candidate table before publish | server `POST /admin/connections/{id}/tables/validate` | table ref | validation result with field/geometry/SRID inference | wizard step shows inferred schema | **GAP** | P1 |

### Family B — Content Publication lifecycle (map/dashboard/report publications)
| Operation | Console method | Server output + verifying API | Console reflection | Coverage | Pri |
|---|---|---|---|---|---|
| Create publication | `HonuaServerStudioReportPublicationDataSource.CreateAsync` → `POST /api/v1/console/publications` | publication appears in catalog `GET /api/v1/console/content/search` with right visibility/route; `GET /publications/{id}` | publishing matrix / catalog renders it | **PARTIAL** (`PublishingWorkspaceLiveServerTests` seeds + reads same path; no independent catalog/route read) | P0 |
| Republish (advance revision) | `HonuaServerPublishingWorkspaceDataSource.RepublishAsync` → `POST /publications/{id}/republish` | active revision = 2 via independent `GET /publications/{id}/versions/{sel}` | matrix active-revision badge | **PARTIAL** (asserts via same datasource) | P1 |
| Rollback active pointer | `RollbackAsync` → `POST /publications/{id}/rollback` | active revision back to 1; rollback class | review card `rolled-back` | **PARTIAL** | P1 |
| Set publication policy/visibility | `PATCH /publications/{id}/policy` | catalog visibility/sharing changes; public route resolves/404s accordingly | policy chip | **GAP** | P1 |

### Family C — Studio authoring → publish (package draft→validate→publish→content)
Covers map/dashboard/report/app/form/query/analysis/workflow. Routes in `StudioPackageShims.cs`, `FormPackageShims.cs`, `StudioWorkflowShims.cs`, `AnalysisContentShims.cs`.
| Operation | Console method/page | Server output + verifying API | Console reflection | Coverage | Pri |
|---|---|---|---|---|---|
| Create draft | `POST /api/v1/studio/package-drafts` | `GET /package-drafts/{id}` returns draft | builder shows draft ref | **PARTIAL** (`StudioPackageLifecycleIntegrationTests` asserts session projection + render, not catalog landing) | P1 |
| Update draft | `PUT /package-drafts/{id}` | re-GET shows edits | builder re-renders | PARTIAL | P1 |
| Validate draft | `POST /package-drafts/{id}/validate` | validation items returned | validation panel | PARTIAL | P1 |
| Cut content version | `POST /package-drafts/{id}/content-versions` | `GET /content-items/{id}/versions` lists it | version list | **GAP** (no independent content-item read) | P1 |
| Publish-request | `POST /content-items/{id}/versions/{v}/publish-requests` | **content appears in catalog** with correct visibility/route (`GET /console/content/search`); FeatureServer/STAC route live for map/app | catalog tile + publish status | **GAP** (the round-trip "published content is now in the catalog and reachable" is unproven) | **P0** |
| Reopen / Rollback-request | `.../reopen`, `/content-items/{id}/rollback-requests` | version state transitions | builder state | **GAP** | P2 |
| Form publish | `POST /admin/forms/packages/{id}/versions/{v}/publish` | `GET /forms/packages/{id}/offline-policy` + catalog form item | form builder published badge | **PARTIAL** (`StudioFormBuilderIntegrationTests`) | P1 |
| Analysis run/preview/estimate | `POST /analysis/content/items/{id}/versions/{v}/runs|preview|estimate` | run artifact `GET /analysis/artifacts/{id}`; job failure read | analysis builder result panel | **PARTIAL** (`StudioAnalysisBuilderIntegrationTests`) | P1 |
| Workflow publish + run | `POST /console/workflow-packages/{id}/versions/{v}/publish`, `/workflow-publications/{id}/runs` | `GET /console/workflow-publications` lists publication; run row appears | workflow editor/AI page | **PARTIAL** (`StudioWorkflowPackageIntegrationTests`) | P1 |

### Family D — Share (public links / embeds / open-data / access tier)
Routes in `ShareAccessShims.cs`.
| Operation | Console method | Server output + verifying API | Console reflection | Coverage | Pri |
|---|---|---|---|---|---|
| Raise access tier | `UpdateAccessTierAsync` → `PUT /content/{id}/share/access` | item public; **anonymous `GET /content/{id}` or public route resolves**; STAC/DCAT open-data entry appears | share page IsPublic | **PARTIAL** (`ShareAccessLiveServerTests` asserts same projection; no anonymous fetch) | P1 |
| Mint public link | `MintPublicLinkAsync` → `POST /content/{id}/share/link` | **token actually grants access**: anonymous request with token → 200; without → 403 | token row | **PARTIAL** (no anonymous-access proof) | **P0** |
| Revoke public link | `RevokePublicLinkAsync` → `DELETE /content/{id}/share/link/{tokenId}` | token no longer grants access (anonymous → 403) | token gone | **PARTIAL** | P1 |
| Set / mint embed | `SetEmbedAsync`/`MintEmbedTokenAsync` → `PUT|POST /content/{id}/share/embed` | embed token resolves embed render; redacted in projection | embed panel | **PARTIAL** | P1 |
| Open-data / STAC-DCAT / export | (share open-data) | STAC `/collections/{id}` + DCAT doc present for public items | open-data page | **GAP** | P2 |

### Family E — Operate: temporal, observability, connections, services
| Operation | Console method | Server output + verifying API | Console reflection | Coverage | Pri |
|---|---|---|---|---|---|
| Execute temporal rollback | `ITemporalCapabilityClient.ExecuteRollbackAsync` | **as-of query before/after differs**: `/query` with time param reflects rolled-back state; checkpoint list updated | temporal page operation result | **PARTIAL** (`OperateTemporalLiveServerTests` — verify it reads back via as-of `/query`, not just plan) | **P0** |
| Resolve replica conflicts | `ResolveConflictsAsync` | conflict queue empties via independent `GetReplicaConflictQueueAsync` server read; resolved values land | temporal/replica panel | **PARTIAL** | P1 |
| Connections / resources / services / settings / version / capabilities / license / api-keys / oidc | `HonuaServerOperateTransitionDataSource` (all GET) | n/a — read-only surfaces | render-not-missing | **COVERED-as-read** (`OperateTransitionLiveServerTests`) — read-only, no output to assert | P2 |
| Observability: overview/events/logs/alerts/rules/jobs/investigations | `HttpConsoleOperateObservabilityClient` (all GET) | read-only | render-not-missing | **COVERED-as-read** (`OperateObservabilityTestcontainersTests`) | P2 |

### Family F — Operate GitOps releases
| Operation | Console method | Status |
|---|---|---|
| List / detail release proposals | `HttpConsoleGitOpsReleaseClient` — **GET only** | **COVERED-as-read** (`GitOpsReleaseLiveServerTests`). NOTE: release/rollback/promote *mutations* are NOT wired in the console today (interface exposes only `Get*` methods). When mutation operations land, add round-trip tests asserting metadata/release state via `GET /api/v1/admin/metadata/...`. **GAP (operation does not exist yet).** |

### Family G — Catalog (browse/search/detail) + Catalogs discovery
| Operation | Console method | Server output + verifying API | Console reflection | Coverage | Pri |
|---|---|---|---|---|---|
| Create content (seed) + search + detail | `HonuaServerConsoleCatalogClient` → `POST /console/content`, `GET .../search`, `/{id}` | item searchable + detail with capabilities | catalog page renders it | **COVERED** (`CatalogServerBindingIntegrationTests` — best existing round-trip; still single-path) | P2 |
| Action check | `POST /console/actions/check` | allowed actions match RBAC | action buttons | PARTIAL | P2 |
| Catalogs discovery endpoints | `HonuaServerCatalogDiscoveryDataSource` — GET only (`/console/catalog-endpoints/...`) | read-only registry | discovery page renders | **COVERED-as-read** (`CatalogsDiscoveryPageRenderTests`) | P2 |

### Family H — RBAC / Access binding
| Operation | Console method | Status |
|---|---|---|
| Load roles / members overview | `HonuaServerRbacAccessDataSource` — **GET only** (`/console/access/{ws}/roles`, `/members`) | **COVERED-as-read** (`OperateAccessPageRenderTests`). RBAC *invite / role-assignment mutations* are NOT wired in the console (datasource exposes only `LoadOverviewAsync`/`LoadMembershipAsync`). **GAP (operation does not exist yet)** — when invite/assign lands, assert member+scope via `GET /console/access/{ws}/members`. |

### Family I — Esri content imports
| Operation | Status |
|---|---|
| Esri import (#100/#101/#102/#104) | No dedicated live datasource/POST in `Services/` today (UI/mockup surfaces only). **GAP (operation not wired to server).** Defer round-trip tests until an import POST + server ingest endpoint exists; then verify the imported layer is queryable (same oracle as Family A). |

### Family J — Environment trust / mTLS
| Operation | Console method | Status |
|---|---|---|
| Validate client certificate | `POST /api/v1/admin/security/client-certificates/validate` | **COVERED** (`ConsoleTrustValidationIntegrationTests` / `HonuaServerMtlsFixture`) — validation output asserted | P2 |

---

## 3.A WORKED EXAMPLE — Service-Layer Publish Round-Trip (FLAGSHIP, P0)

**Operation:** Operator opens the publishing workspace, Quick mode, and publishes a PostGIS table as a queryable service layer (Service → Layer → Review).

**Precondition / seed:** create a PostGIS table `parcels` in the shared `postgres` container with known columns (`id int`, `name text`, `area_m2 double`, `geom geometry(Polygon, 3857)`) and 3 rows whose bbox is known, registered under an admin connection.

**Console operation under test (to be wired — currently GAP):**
`PublishWizardWorkspace` Quick mode collects {connectionId, table=`parcels`, layerName, SRID=3857, outFields, capabilities=[Query]} and on finish issues `POST /api/v1/admin/connections/{id}/layers` (server `LayerPublishingEndpoints`), returning `Location: /api/v1/admin/connections/{id}/layers/{layerId}`.

**Server-side OUTPUT assertions (via `ServerStateVerifier`, independent read APIs):**
1. **Layer registered:** `GET /api/v1/admin/connections/{id}/layers` includes the new layer with `enabled=true`, name, source table.
2. **Service metadata correct:** `GET /rest/services/{service}/FeatureServer/{layerId}` returns:
   - `fields[]` == {id:esriFieldTypeInteger, name:esriFieldTypeString, area_m2:esriFieldTypeDouble} (+ OBJECTID),
   - `geometryType == esriGeometryPolygon`,
   - `extent.spatialReference.wkid == 3857`,
   - `extent` ≈ the seeded data bbox (within tolerance),
   - `capabilities` contains `Query`.
3. **Layer is queryable with correct data:** `GET /rest/services/{service}/FeatureServer/{layerId}/query?where=1=1&outFields=*&f=json` returns:
   - `features.length == 3`,
   - returned attribute keys == the configured outFields,
   - attribute value types match the field types,
   - `spatialReference.wkid == 3857`,
   - a `where=area_m2>X` filter returns the expected subset (proves the field is real and filterable).
4. **Settings reflect the layer:** `GET /api/v1/admin/services/{service}/settings` lists the layer with the configured protocols/metadata.
5. **(If capabilities=[Query] only)** an edit attempt is rejected — proves capability config is enforced (ties into §5.1).

**Console-side reflection assertions (bUnit):**
6. Render `OperateLayersPage` / `OperateServiceDetailPage` (bound to the live `IOperateTransitionDataSource`); assert the new layer appears with its name/SRID/extent and is NOT the missing-binding surface.
7. Render the publishing workspace; assert the review/matrix shows the published slot ("layer slot is now live").

**Negative / idempotency companions:**
8. Re-publishing the same table is idempotent or yields a deterministic conflict (assert exact behavior).
9. Bad config (unknown SRID, missing geometry column) → `POST .../layers` returns field-level validation errors and the wizard surfaces them (no fabricated success).

This single scenario is the template every other family copies: **operation → independent server read of the resulting state → console re-render.**

---

## 4. Why most existing suites are PARTIAL (the core gap)

Every current live test follows: *seed/mutate via datasource → assert the SAME datasource's projection → assert page render*. Examples:
- `PublishingWorkspaceLiveServerTests`: republish/rollback asserted via the same `HonuaServerPublishingWorkspaceDataSource` return value; never an independent catalog/route read proving the publication is reachable at its slug.
- `ShareAccessLiveServerTests`: mint/revoke asserted via the reloaded share projection; never an anonymous request proving the token actually grants/denies access.
- `StudioPackageLifecycleIntegrationTests`: asserts the authoring *session* projection + markup; never that the published content landed in the catalog with the right visibility/route.
- `CatalogServerBindingIntegrationTests` is the closest to correct (seed→search→detail→render) but is still single-client.

These prove the console↔server contract for one code path. They do **not** prove the server is *configured correctly* (rule #2). The plan's net-new value is the `ServerStateVerifier` oracle + independent-read assertions layered onto these, plus closing the true GAPs (service-layer publish, publish→catalog landing, token-grants-access).

---

## 5. Cross-Cutting Tests

### 5.1 RBAC enforcement (forbidden ops blocked)
- Boot a second auth profile (no dev-bypass / low-privilege key). Attempt each mutating operation (publish layer, create publication, mint share link, temporal rollback) and assert the server returns 401/403 AND the console renders the shared forbidden/unavailable section status (per the Operate state vocabulary), not a fabricated success.
- Conversely assert allowed scopes succeed. Cross-check `POST /console/actions/check` against the actual allow/deny.

### 5.2 Missing-binding when unconfigured (no fabrication)
- With NO `HONUA_SERVER_BASE_URL`, every live route renders the explicit missing-binding surface and performs no network call. Assert per Charter §11 / Conventions ("missing-binding is a first-class state"). (Extends existing render tests to confirm zero fabrication across all families.)

### 5.3 Validation / error outputs (bad config rejected with field errors)
- Ties into the in-flight client+server validation initiative (task #70). For each create/configure op, submit invalid input (bad SRID, missing required field, duplicate slug) and assert: server returns structured field-level errors, the console binds them to the offending fields, and NO partial/ghost resource is created (verify via `ServerStateVerifier` that nothing landed).

### 5.4 Idempotency / round-trip fidelity (configure → read back → equals)
- For each configurable resource, configure → read back via the server API → assert deep equality of the round-tripped config (fields, SRID, extent, capabilities, visibility, policy). Re-applying identical config is a no-op (no duplicate, stable revision).

### 5.5 Version / contract drift (pinned nightly)
- Nightly pins the server image (`HONUA_CONSOLE_SERVER_IMAGE`). Add a contract-drift fact: assert `GET /api/v1/admin/version` + `/capabilities` match the pinned expectations, and that the shim route constants in `Honua.Console.Contracts/*Shims.cs` still resolve (200/expected-4xx) against the live server — catches server route renames before they hit users.

---

## 6. Prioritized Build Waves (one PR per wave)

**Wave 1 — Flagship service-layer publish round-trip + the verifier oracle (P0).**
- Build `ServerStateVerifier` (the FeatureServer/admin/catalog/share/STAC read client).
- Wire the console quick-publish/author-first operation to `POST /admin/connections/{id}/layers` (this is a product gap — coordinate; the test PR may need a minimal wire-up or a feature flag).
- Implement §3.A end-to-end (seed PostGIS → publish → independent `/query` + metadata asserts → page render).
- Add the layer-publish negative/idempotency companions.
- *Highest value: closes the single most important GAP and ships the reusable oracle.*

**Wave 2 — Publish→catalog landing & token-grants-access (P0).**
- Studio publish-request → independent catalog `/search` + route reachability assertions (Family C publish-request).
- Content publication create → catalog visibility/route assertions (Family B create).
- Share mint/revoke → anonymous-access proof (token grants 200 / revoked 403); access-tier → anonymous content fetch.
- *Converts the biggest PARTIAL clusters into true round-trips.*

**Wave 3 — Temporal rollback & replica conflict round-trips (P0/P1).**
- Temporal `ExecuteRollbackAsync` → as-of `/query` before/after differs; checkpoint list updated.
- `ResolveConflictsAsync` → conflict queue empties; resolved values land.

**Wave 4 — Studio family content-landing depth (P1).**
- For map/dashboard/report/app/form/query/analysis/workflow: cut-version → `content-items/{id}/versions` independent read; form offline-policy; analysis artifact; workflow publication+run rows. Layer onto existing builder integration tests.

**Wave 5 — Service settings + layer enable/extents + publication policy (P1).**
- Requires the corresponding console mutation surfaces to exist (currently read-only). Service-settings PUTs, layer enabled/extents, `PATCH /publications/{id}/policy` → verify via settings/layers/catalog reads.

**Wave 6 — Cross-cutting hardening (P1/P2). DELIVERED.**
- RBAC-forbidden matrix, validation/field-error matrix, idempotency/fidelity matrix, version/contract-drift fact, missing-binding completeness. Add nightly step-summary rows per family.
- Shipped as `RbacEnforcementCrossCuttingTests`, `ValidationFieldErrorsCrossCuttingTests`,
  `IdempotencyFidelityCrossCuttingTests`, `VersionContractDriftCrossCuttingTests`, and
  `MissingBindingCompletenessCrossCuttingTests` (the last is a pure-render no-Docker sweep that also runs in
  PR CI), all on the shared `CrossCuttingFixture` + `CrossCuttingSeeder`. `ServerStateVerifier` gained
  `ProbeAnonymousMutationStatusAsync` (the RBAC 401/403-vs-404 discriminator) and `GetServerContractAsync`
  (the independent version/contract read), and the drift fact emits `ContractDriftEvidence` so the pinned
  `:nightly` contract is recorded in the run artifacts. **This completes the integration-test plan (W1–W6).**

**Deferred (operations not yet wired — track as product gaps, not test gaps):** GitOps release/rollback/promote mutations (Family F), RBAC invite/role-assignment mutations (Family H), Esri import POST (Family I). Add round-trip tests when the operations land.

---

## 7. Summary of Counts

Operation families: **10** (A Service-layer publishing, B Content publications, C Studio authoring→publish, D Share, E Operate temporal/observability/connections, F GitOps, G Catalog/discovery, H RBAC, I Esri import, J Trust/mTLS).

Distinct mutating/configuring operations enumerated: **~38**.
- **COVERED / COVERED-as-read:** ~10 (catalog seed-search-detail round-trip; trust cert-validate; the read-only Operate/observability/GitOps-read/RBAC-read/discovery-read surfaces).
- **PARTIAL** (live test exists, same-path only — needs independent server-state read): ~14 (publication create/republish/rollback, studio draft/validate/form/analysis/workflow, share access/mint/revoke/embed, temporal rollback/replica).
- **GAP** (no operation→output test, or operation not wired to POST): ~14 (service-layer quick/author publish, layer enable/extents, service settings PUT, table validate, publication policy, studio cut-version + publish-request catalog landing, studio reopen/rollback, share open-data/STAC, plus deferred GitOps/RBAC/Esri mutations).

The flagship GAP — **the service-layer publish round-trip** — is the worked example in §3.A and Wave 1.

Plan file: `/home/mike/.cache/console-integration-test-plan.md`
