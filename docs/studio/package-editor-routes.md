# Studio Package Editor Routes

Status: implemented for `honua-console#39` with stable Console mock lifecycle refs.

The first Console-native Studio editor set lives in the shared Razor shell library and is served by the same Blazor Web and future native host surface as the rest of Console.

The `/studio` entry page links to every route below. Each editor is a projection over the package-family contract, not a separate Console-owned DTO.

## Routes

| Route | Package family | Editor coverage |
| --- | --- | --- |
| `/studio/query` | `query.package` | Source binding, predicate builder, generated SQL/filter readout, parameters, map/table preview, save-as-content lifecycle. |
| `/studio/analysis` | `analysis.package` | Plan card, parameters, output schema, compute estimate, DAG/pipeline preview, job/result artifact review. |
| `/studio/map` | `map.package` | Layer stack, filters, style, popup, legend, basemap, extent, interactions, publish/share/embed/rollback review. |
| `/studio/dashboard` | `dashboard.package` | Data bindings, layout, Vega-Lite chart spec editor, map panels, tables, filters, version pinning, responsive preview. |
| `/studio/report` | `report.package` | Narrative sections, data bindings, Vega-Lite chart spec editor, maps, tables, export/refresh policy, responsive preview. |
| `/studio/form` | `form.package` | Fields, groups, validation, domains, conditional visibility, attachments, privacy, submit target, required offline/sync policy review. **Server-bound to honua-server#1184 (`honua-console#57`)** — a dedicated builder, not the mock simulator below. |
| `/studio/app` | `app.package` | Pages, components, navigation, bindings, actions, permissions, preview/reopen, share/embed policy, versioned reopened edits. |

## Backend Boundary

`/studio/form` is now its own server-bound surface (`honua-console#57`): `StudioFormBuilderPage` binds the honua-server form package lifecycle (`honua-server#1184`) through `IStudioFormPackageDataSource` over the `Honua.Console.Contracts` shim (`IHonuaFormPackageClient`). It authors fields, groups, validation, domains, conditional visibility, attachments, and privacy, then enforces an explicit reviewed offline/sync policy and a validated submit target before publish. When no server base address is configured it renders a missing-binding state — never mock form data (Console Patterns Charter section 11).

### `/studio/form` server contract

The builder maps editor state to and from the server-owned `honua.form-package.v1` document; no `studio-package-mock/v1` ref is involved. The `IHonuaFormPackageClient` shim (`src/Honua.Console.Contracts/FormPackageShims.cs`) speaks the real lifecycle:

| Operation | Verb + path |
| --- | --- |
| List packages | `GET /api/v1/admin/forms/packages` |
| Create draft | `POST /api/v1/admin/forms/packages` |
| Get current version | `GET /api/v1/admin/forms/packages/{formId}` |
| List / get versions | `GET /api/v1/admin/forms/packages/{formId}/versions[/{version}]` |
| Update draft (`If-Match` ETag) | `PUT /api/v1/admin/forms/packages/{formId}/versions/{version}` |
| Validate / publish / reopen | `POST /api/v1/admin/forms/packages/{formId}/versions/{version}/{validate\|publish\|reopen}` |
| Offline/sync policy | `GET /api/v1/forms/packages/{formId}/offline-policy` (runtime, not admin) |

The binding is enabled by `Honua:Server:BaseUrl` (or `HONUA_SERVER_BASE_URL`); without an absolute http(s) base address Console registers `UnsupportedStudioFormPackageDataSource` and every action returns a missing-binding state. The forms endpoints return the contract DTO directly (no `{success,data,message}` wrapper) and use optimistic concurrency — an existing draft sends its server `ETag` on `PUT`, a new one posts a create. Endpoint failures surface as a shared `StudioFormCapabilityState` (the Operate capability-state pattern) rather than exceptions: **missing binding** (no base address), **missing permission** (401/403 server RBAC), **unsupported** (404/405/501 or unexpected body), **conflict** (409/412/428 ETag / missing `If-Match`), **rejected** (400 — run validation), and **unavailable** (transport/empty body).

Publish is gated Console-side on top of the server's own publish validation; the builder offers publish only when a title and at least one field exist, a submit-target service is configured (AC#3), the offline/sync policy has been explicitly reviewed and — when offline use is enabled — at least one sync transport is on (AC#2), and server validation has run for the open version with no errors (AC#3). Because validation runs against the *saved* version, the builder tracks the editor against the content signature captured at load/save: any later edit marks the draft dirty, which disables Validate (save first) and re-gates publish until the draft is saved and re-validated — so a stale "valid" result can never publish content that has since changed.

The remaining per-editor package families (`honua-console#52`–`#56`, `#58`) are still out of scope for backend binding and use a single local lifecycle model in `StudioPackageLifecycleSimulator`. As of `honua-console#61` the editor's validate/preview actions surface a **missing-binding** state rather than reporting mock validation success, so the editors never imply mock refs are valid runtime package data.

The shared `/studio` shell already binds the honua-server package lifecycle (`honua-server#1180`/`#1181`) through `IStudioPackageLifecycleClient`; when the remaining per-editor backend projections land, the simulator boundary should be replaced behind the same editor model instead of introducing a second Console package schema.

## Mock Response Contract

The package inspector renders the temporary `studio-package-mock/v1` projection. It is a UI mock contract for this Console slice only; it must not become the server or SDK wire schema.

> **`/studio/form` is server-bound and does not use this mock contract** (see [Backend Boundary](#backend-boundary) above). The `form.package` family is retained below only as the Studio directory family descriptor and the `studio-package-mock/v1` lifecycle test fixture — it no longer backs a live editor route.

Top-level fields:

- `schema_version`: currently `studio-package-mock/v1`.
- `package_type`: one of `query.package`, `analysis.package`, `map.package`, `dashboard.package`, `report.package`, `form.package`, or `app.package`.
- `content_type`: the published content family (`query`, `analysis`, `map`, `dashboard`, `report`, `form`, or `app`).
- `title`, `summary`, and `data_bindings`: editable draft values.
- `current_version`: the current mock content version id (`vN`).
- `publication_intent`: `visibility`, `embed_policy`, and `rollback_target`.
- `editor`: the family-specific projection described below.

Family-specific editor payloads:

| Package family | `editor` fields |
| --- | --- |
| Query | `predicate`, `parameter`, `generated_sql`, `result_limit` |
| Analysis | `operation`, `distance`, `worker_profile`, `output_schema` |
| Map | `basemap`, `layer_style`, `popup_fields`, `initial_extent` |
| Dashboard / Report | `chart_standard = "vega-lite/v5"`, `chart_title`, `measure`, `dimension`, `version_pin`, `vega_lite_spec` |
| Form | `field_group`, `required_field`, `domain`, `submit_target`, `offline_sync_policy`, `offline_policy_reviewed`, `attachment_policy` |
| App | `pages`, `components`, `action`, `permission`, `reopened_edit_policy = "create_new_content_version"` |

Lifecycle operations update a `StudioPackageLifecycleSnapshot` with:

- `item_id`, `current_version`, `published_version`, `reopened_from_version`, and `rollback_from_version`.
- `published`, `shared`, and `embedded` booleans.
- Evidence entries with stable prefixes: `content-version.create`, `content-version.read`, `publication.create`, `share-access.update`, `embed-token.mint`, and `rollback.create`.

Share, embed, and rollback require `published = true` and a non-zero `published_version`; the mock must not represent shared, embedded, or rolled-back draft-only content. Form publish is disabled until a non-empty offline/sync policy is selected and the review flag is set. Reopened app edits create a new current content version without mutating the published version. Dashboard and report chart projections use Vega-Lite v5.

## Acceptance Notes

- Map lifecycle coverage is pinned by `GeneratedMapLifecycleSupportsPublishShareEmbedAndRollback`.
- Dashboard and report chart standard coverage is pinned by `DashboardAndReportChartsUseVegaLite`.
- Form publish gating in the retained mock-catalog family is pinned by `FormPublishRequiresOfflinePolicyReview`. The live server-bound `/studio/form` builder (`honua-console#57`) is verified separately by `Honua.Console.IntegrationTests` (`StudioFormBuilder*`, `StudioFormPackage*`), not by this mock contract.
- Reopened app version behavior is pinned by `ReopenedAppEditsCreateNewContentVersionsWithoutMutatingPublishedVersion`.
- Save, reopen, edit, and publish smoke coverage across all seven package families is pinned by `StableMockLifecycleCoversSaveReopenEditPublishForEveryPackageFamily`.
