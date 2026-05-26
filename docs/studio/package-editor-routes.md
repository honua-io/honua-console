# Studio Package Editor Routes

Status: implemented for `honua-console#39` with stable Console mock lifecycle refs; the **analysis** editor
was retrofit to a real honua-server binding in `honua-console#53` (the other six remain on the mock lifecycle).

The first Console-native Studio editor set lives in the shared Razor shell library and is served by the same Blazor Web and future native host surface as the rest of Console.

The `/studio` entry page links to every route below. Each editor is a projection over the package-family contract, not a separate Console-owned DTO.

## Routes

| Route | Package family | Editor coverage |
| --- | --- | --- |
| `/studio/query` | `query.package` | Source binding, predicate builder, generated SQL/filter readout, parameters, map/table preview, save-as-content lifecycle. |
| `/studio/analysis` | analysis content (`savedQuery` / `analysisPackage`) | **Server-bound** (`honua-console#53`): plan card, DAG/pipeline view, declared output schema, saved-query preview, analysis-package job submit/rerun, job + result-artifact panel, downstream binding hand-off. Bound to honua-server `#1182`; **not** the mock simulator below. |
| `/studio/map` | `map.package` | Layer stack, filters, style, popup, legend, basemap, extent, interactions, publish/share/embed/rollback review. |
| `/studio/dashboard` | `dashboard.package` | Data bindings, layout, Vega-Lite chart spec editor, map panels, tables, filters, version pinning, responsive preview. |
| `/studio/report` | `report.package` | Narrative sections, data bindings, Vega-Lite chart spec editor, maps, tables, export/refresh policy, responsive preview. |
| `/studio/form` | `form.package` | Fields, groups, validation, domains, conditional visibility, attachments, privacy, submit target, required offline/sync policy selection and review. |
| `/studio/app` | `app.package` | Pages, components, navigation, bindings, actions, permissions, preview/reopen, share/embed policy, versioned reopened edits. |

## Backend Boundary

The **analysis** editor is carved out of the shared mock editor as of `honua-console#53`: `/studio/analysis`
renders the dedicated `StudioAnalysisBuilder` bound to the honua-server **Analysis Content API**
(`honua-server#1182`, sitting on the closed geoprocessing engine `#681`/`#721`/`#724`) through
`IStudioAnalysisContentClient` in `Honua.Console.Contracts`. When no absolute server base address is configured
it falls back to `UnsupportedStudioAnalysisContentClient` (a named missing-binding surface), never an in-memory
analysis client. Three slivers have no server contract and render explicit "unavailable" states rather than a
mock: pre-submit compute/cost estimate, analysis-package dry-run/preview (saved-query preview is backed), and
natural-language→plan compilation — each tracked as a bounded honua-server follow-on.

The remaining six per-editor families (`honua-console#52`, `#54`–`#58`: query, map, dashboard, report, form,
app) are still out of scope for backend binding and use a single local lifecycle model in
`StudioPackageLifecycleSimulator`. As of `honua-console#61` their validate/preview actions surface a
**missing-binding** state rather than reporting mock validation success, so the editors never imply mock refs
are valid runtime package data.

The shared `/studio` shell already binds the honua-server package lifecycle (`honua-server#1180`/`#1181`) through `IStudioPackageLifecycleClient`; when the per-editor backend projections land, the simulator boundary should be replaced behind the same editor model instead of introducing a second Console package schema.

## Mock Response Contract

The package inspector renders the temporary `studio-package-mock/v1` projection for the six simulator-backed
editors only (analysis is server-bound — see Backend Boundary). It is a UI mock contract for this Console slice
only; it must not become the server or SDK wire schema.

Top-level fields:

- `schema_version`: currently `studio-package-mock/v1`.
- `package_type`: one of `query.package`, `map.package`, `dashboard.package`, `report.package`, `form.package`, or `app.package`.
- `content_type`: the published content family (`query`, `map`, `dashboard`, `report`, `form`, or `app`).
- `title`, `summary`, and `data_bindings`: editable draft values.
- `current_version`: the current mock content version id (`vN`).
- `publication_intent`: `visibility`, `embed_policy`, and `rollback_target`.
- `editor`: the family-specific projection described below.

Family-specific editor payloads:

| Package family | `editor` fields |
| --- | --- |
| Query | `predicate`, `parameter`, `generated_sql`, `result_limit` |
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
- Form publish gating is pinned by `FormPublishRequiresOfflinePolicyReview`.
- Reopened app version behavior is pinned by `ReopenedAppEditsCreateNewContentVersionsWithoutMutatingPublishedVersion`.
- Save, reopen, edit, and publish smoke coverage across all seven package families is pinned by `StableMockLifecycleCoversSaveReopenEditPublishForEveryPackageFamily`.
- The server-bound analysis builder is pinned by `StudioAnalysisBuilderRenderTests` (Docker-free: plan/DAG render, honest unavailable states, missing-binding surface), `StudioAnalysisContentClientTests` (shim error mapping + envelope-free deserialization), and `StudioAnalysisContentIntegrationTests` (Testcontainers: seed an analysis-content item on a live honua-server and assert the builder renders the live plan; skips when Docker/opt-in is unavailable).
