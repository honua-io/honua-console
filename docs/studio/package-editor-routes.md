# Studio Package Editor Routes

Status: implemented for `honua-console#39` with stable Console mock lifecycle refs.

The first Console-native Studio editor set lives in the shared Razor shell library and is served by the same Blazor Web and future native host surface as the rest of Console.

## Routes

| Route | Package family | Editor coverage |
| --- | --- | --- |
| `/studio/query` | `query.package` | Source binding, predicate builder, generated SQL/filter readout, parameters, map/table preview, save-as-content lifecycle. |
| `/studio/analysis` | `analysis.package` | Plan card, parameters, output schema, compute estimate, DAG/pipeline preview, job/result artifact review. |
| `/studio/map` | `map.package` | Layer stack, filters, style, popup, legend, basemap, extent, interactions, publish/share/embed/rollback review. |
| `/studio/dashboard` | `dashboard.package` | Data bindings, layout, Vega-Lite chart spec editor, map panels, tables, filters, version pinning, responsive preview. |
| `/studio/report` | `report.package` | Narrative sections, data bindings, Vega-Lite chart spec editor, maps, tables, export/refresh policy, responsive preview. |
| `/studio/form` | `form.package` | Fields, groups, validation, domains, conditional visibility, attachments, privacy, submit target, required offline/sync policy review. |
| `/studio/app` | `app.package` | Pages, components, navigation, bindings, actions, permissions, preview/reopen, share/embed policy, versioned reopened edits. |

## Backend Boundary

Backend package lifecycle implementation is still out of scope for this ticket. The UI uses a single stable mock lifecycle model in `StudioPackageLifecycleSimulator` to represent the future honua-server and honua-sdk-dotnet content-version, publication, share, embed, and rollback APIs.

When the backend SDK projections land, the mock boundary should be replaced behind the same editor model instead of introducing a second Console package schema.

## Acceptance Notes

- Map lifecycle coverage is pinned by `GeneratedMapLifecycleSupportsPublishShareEmbedAndRollback`.
- Dashboard and report chart standard coverage is pinned by `DashboardAndReportChartsUseVegaLite`.
- Form publish gating is pinned by `FormPublishRequiresOfflinePolicyReview`.
- Reopened app version behavior is pinned by `ReopenedAppEditsCreateNewContentVersionsWithoutMutatingPublishedVersion`.
- Save, reopen, edit, and publish smoke coverage across all seven package families is pinned by `StableMockLifecycleCoversSaveReopenEditPublishForEveryPackageFamily`.
