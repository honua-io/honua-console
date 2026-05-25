# Console UI Implementation Backlog

Status: planning draft.

Decision source: [ADR-0001: Unified Honua Console Runtime](../adr/0001-unified-honua-console-runtime.md).

Design sources:

- [Console Canvas Handoff](../design-handoff/console-canvas/README.md)
- [Workflow Catalog](../design-handoff/workflow-catalog.md)
- [UI Surface Briefs](../design-handoff/ui-surface-briefs.md)
- [Console Route Map](../console-route-map.md)
- [Console Backend Capability Backlog](CONSOLE_BACKEND_CAPABILITY_BACKLOG.md)

Companion planning artifact:

- [Design Artifact Work Breakdown Matrix](DESIGN_ARTIFACT_WORK_BREAKDOWN.md)

## Purpose

This backlog breaks the Console design handoff into implementable UI slices. It is intentionally more detailed than the existing GitHub epics, but still higher-level than individual component tasks.

The split follows product workflows instead of artboard count. A single ticket may reference several artboards when they are one user journey. A large artboard may appear in more than one ticket when it contains separate buildable concerns.

Use the [Design Artifact Work Breakdown Matrix](DESIGN_ARTIFACT_WORK_BREAKDOWN.md) before filing child implementation work. It maps each design artifact to routes, contracts or mocks, reusable components, required states, and child issue splits.

## Non-Negotiable Constraints

- One deployable artifact: no separate Portal, Admin, or Studio product surface.
- Server-owned truth: UI does not invent package, permission, publication, job, release, event, temporal, or sync state.
- Real-server integration, no standing mocks: server-owned data binds to a real honua-server (via `honua-sdk-dotnet` or the shim boundary); in-memory clients are transient scaffolding only, never a merged data source; every server-backed slice's Definition of Done is a Testcontainers integration test against honua-server. See [Console Patterns Charter §11](../migration/CONSOLE_PATTERNS_CHARTER.md).
- Shared package contracts: Console, MCP, QGIS, generated apps, and embeds use the same server/SDK contract family.
- Every route has loading, empty, warning, blocked, partial success, error, permission denied, and filtered-empty handling.
- AI is advisory or authoring assistance only; governed operations require explicit user action and evidence.
- AI mapmaking and analysis target ArcGIS Pro-style capability breadth through natural-language planning, package generation, validation, and governed execution. Console must not clone ArcGIS Pro desktop UI surfaces; it should expose equivalent GIS intent through Honua packages, capability registry coverage, execution adapters, and interoperability behaviors.

## Implementation Milestones

| Milestone | Goal | Primary issues |
| --- | --- | --- |
| M0 | Console shell, route guards, design primitives, data-client seams | `honua-console#2`, `#3`, `#7` |
| M1 | Portal parity inside Console: Catalog, viewer, Share, open data, embeds | `honua-console#4`, `#8`, `#9`, `#10` |
| M2 | Operate/Admin transition: connections, resources, services, settings, jobs/events | `honua-console#6`, `#24` |
| M3 | Studio authoring MVP: packages, preview, editors, publish/reopen | `honua-console#5`, `#16` |
| M4 | Advanced authoring: forms, GP/ETL, workflows, MCP/QGIS parity | `honua-console#17`, `honua-sdk-js#226` |
| M5 | GitOps, temporal, sync conflicts, AI DevOps, native Console | `honua-console#22`, `#23`, `#24`, `#26` |

## Foundation Slices

### UI-001: Unified Console Shell And Navigation

Parent: `honua-console#2`, `honua-console#3`

Design refs:

- `console-canvas/shell.jsx`
- `console-canvas/screens-overview.jsx`
- `docs/console-route-map.md`

Routes:

- `/`
- `/studio`
- `/catalog`
- `/share/public`
- `/operate`
- `/groups`

Build:

- Primary nav for Studio, Catalog, Share, Operate.
- Workspace/environment switcher with route-aware environment visibility.
- Global search affordance, job indicator, alert indicator, notifications/inbox entry.
- Route guard projection for auth, anonymous share access, RBAC, edition, entitlement, and feature flags.
- Transitional `/operate/legacy/<path>` container remains separate from final Operate replacement screens.

Backend dependencies:

- Session/workspace projection.
- License/edition projection.
- RBAC/entitlement projection from `honua-server#1162`.

Acceptance:

- A signed-in member sees Studio, Catalog, Share, and workspace routes.
- An operator/admin sees Operate.
- Anonymous share/embed/public routes render without shell chrome where required.
- Route denial uses the shared unavailable/permission surface, not a blank redirect loop.

### UI-002: Console Design Primitives And State Vocabulary

Parent: `honua-console#2`

Design refs:

- `console-canvas/shell.jsx`
- `console-canvas/field-state.jsx`
- `console-canvas/screens-fields-hifi.jsx`
- `console-canvas/screens-settings-states.jsx`

Build:

- Shared Razor component equivalents for `TopBar`, `Sidebar`, `PageHead`, `Tabs`, `Toolbar`, `Stepper`, `Button`, `Badge`, `Callout`, `FieldRow`, `FieldGroup`, `ScopeChip`, and `MapPreview` host slots.
- States gallery mapped into reusable page-level and inline components.
- Scope chips for resource, publication, service, server, environment, and system.

Backend dependencies:

- None for static shell states; later slices bind to real state contracts.

Acceptance:

- Every workflow slice can use the same visual vocabulary for input, discovered, calculated, system, admin, override, revert, warning, blocked, and permission states.
- Shared components are responsive at desktop and tablet widths without text overlap.

### UI-003: Console Data Client, Capability Manifest, And Policy Surface

Parent: `honua-console#7`

Design refs:

- [Backend Capability Backlog](CONSOLE_BACKEND_CAPABILITY_BACKLOG.md)
- `console-canvas/screens-settings-states.jsx`

Build:

- Client seams for server capability manifest, workspace policy, package family support, temporal support, realtime support, native transport support, and per-environment limits.
- Standard request state model: loading, stale, refreshing, permission denied, unavailable capability, blocked validation, partial success.
- Typed SDK shim ledger updates per [SDK Shim Policy](../migration/SDK_SHIM_POLICY.md).

Backend dependencies:

- Metadata/content/RBAC baseline in `honua-server#1162`.
- Capability manifest backend ticket proposed in [Backend Capability Backlog](CONSOLE_BACKEND_CAPABILITY_BACKLOG.md).

Acceptance:

- UI can hide, disable, or explain unsupported package/temporal/sync/realtime/native capabilities from server data.
- No screen hardcodes feature availability from edition alone.

### UI-004: Cross-Surface Search, Inbox, Jobs, And Alert Indicators

Parent: `honua-console#2`, `honua-console#24`

Design refs:

- `console-canvas/shell.jsx`
- `console-canvas/screens-event-viewer.jsx`
- `console-canvas/screens-activity.jsx`

Build:

- Header search placeholder and command entry routing.
- Header job/alert counters that deep-link into filtered Operate views.
- Notification/inbox route placeholder for human gates, approvals, assigned alerts, and review requests.

Backend dependencies:

- Event/job/alert summaries from `honua-server#1168`, `#1169`, `#1170`.

Acceptance:

- Counters are policy-filtered and environment-aware.
- Header links preserve current workspace/environment context.

## Catalog, Share, And Portal-Parity Slices

### UI-010: Catalog Search, Type Strip, And Unified Content Table

Parent: `honua-console#4`

Design refs:

- `console-canvas/screens-catalog-console.jsx` (`CatalogList`)
- `docs/console-route-map.md` §3 rows 8, 10, 14

Routes:

- `/catalog`
- `/catalog?type=map`

Build:

- Catalog search/list with the route-map query contract: `q`, `type`, `tag`, `owner`, `visibility`, `sort`, `cursor`.
- Content type strip for datasets, services, layers, documents, maps, dashboards, reports, forms, apps, workflows, analyses, GP services, ETL pipelines, connectors, templates.
- Status/version/validation/publication columns.

Backend dependencies:

- Content item list contract from `honua-server#1162`.
- SDK projection from `honua-sdk-dotnet#166` for the Blazor shell.

Acceptance:

- Existing Portal catalog queries map to Console without unknown query params; `visibility` maps to the SDK/server `sharing` field and `sharing` is not a public URL key.
- User can open content detail, map viewer, Studio edit, and Share actions based on policy.

### UI-011: Catalog Item Detail, Versions, Lineage, Bindings, And Usage

Parent: `honua-console#4`, `honua-console#7`

Design refs:

- `console-canvas/screens-catalog-console.jsx` (`ContentItemDetail`, `ContentItemUsage`)

Routes:

- `/catalog/:idOrSlug`

Build:

- Overview, versions, lineage, bindings, publication, permissions, activity tabs.
- Usage/risk view before editing or retiring a resource.
- Public-link `?token=` support for anonymous-capable catalog items.
- Detail tab state uses `tab=overview|versions|lineage|bindings|publication|permissions|activity|usage`.

Backend dependencies:

- Content item, content version, lineage, publication, and permission contracts.

Acceptance:

- Authenticated and anonymous public-link reads follow the route map.
- Anonymous reads hide Studio, Share, and permissions details; signed-in reads do not propagate stale public-link tokens into action URLs.

### UI-012: Map Viewer, Embed Route, And Share-Link Compatibility

Parent: `honua-console#4`, `honua-console#9`

Design refs:

- `console-canvas/map-preview.jsx`
- route map rows 7 and 11

Routes:

- `/maps/:mapId`
- `/maps/new`
- `/embed/maps/:mapId`

Build:

- Interactive map viewer host for Catalog, Studio, and Share targets.
- Iframe-safe embed route with `chrome`, `legend`, `zoom`, `extent`, and `#embedToken=` contract.
- Public-link `?token=` support for `/maps/:mapId`.
- Authenticated `/maps/new?from=:itemId` draft hydration from supported service/layer catalog items.

Backend dependencies:

- Saved map package read and share/embed authorization.

Acceptance:

- Existing Portal share snippets continue to resolve.
- Embed route omits full Console shell, only exposes allowed controls, permits tokenless public embeddable maps, and rejects query-string bearer tokens.

### UI-013: Share Surface, Public Links, Embeds, Open Data, And Exports

Parent: `honua-console#4`, `honua-console#16`

Design refs:

- `console-canvas/screens-share.jsx`

Routes:

- `/share`
- `/share/public`
- `/public`
- `/share/public/items/:idOrSlug`
- `/public/items/:idOrSlug`

Build:

- Share home with shared items, traffic, visibility, embed status, and open-data status.
- Per-item public link, embed snippet, token links, expiration, and traffic panels.
- Current #34 slice lists and serves public open-data service/layer/document items at the collection and detail aliases; the full open-data page editor and DCAT/schema.org evidence remain in the broader Share work.
- Scheduled export list and export detail hooks.

Backend dependencies:

- Share/access policy, publication, open-data eligibility, export jobs.
- Unified Console runtime serves open data, STAC, and DCAT.

Acceptance:

- Public/private state is unambiguous.
- Open-data item pages remain anonymous-capable only when eligibility and policy allow.
- Export jobs link to Operate job details.

## Studio Authoring Slices

### UI-020: Studio Home, Project Shell, Prompt, Clarification, And Package Inspector

Parent: `honua-console#5`

Design refs:

- `console-canvas/screens-studio.jsx` (`StudioHome`, `StudioMapAI`)
- `console-canvas/screens-studio-form-workflow.jsx`
- `console-canvas/screens-studio-rest.jsx`

Routes:

- `/studio`
- `/studio/proof`

Build:

- Workflow picker for map, dashboard, report, form, app, query, analysis, workflow, GP/ETL.
- Recent projects and drafts.
- Conversation/prompt panel with clarification controls.
- Package inspector showing assumptions, bindings, warnings, validation, and provenance.
- Capability-registry-backed planner that maps natural-language GIS intent to package families, tool/workflow candidates, parameters, environments, execution targets, and unsupported-capability explanations.

Backend dependencies:

- Studio package lifecycle API proposed in [Backend Capability Backlog](CONSOLE_BACKEND_CAPABILITY_BACKLOG.md).
- SDK package projections.
- GIS capability registry for ArcGIS Pro-style mapmaking, analysis, GP/ETL, and data-management intent.

Acceptance:

- Generated output is always inspectable as a package, not hidden UI state.
- Draft, preview, saved version, and published states are distinct.

### UI-021: Natural-Language To Spatial Query Builder

Parent: `honua-console#5`, `honua-console#16`

Design refs:

- `console-canvas/screens-studio-rest.jsx` (`StudioQueryBuilder`)
- [Workflow Catalog](../design-handoff/workflow-catalog.md) §1

Build:

- Source/layer/field binding picker.
- Visual predicate builder, spatial predicate summary, generated SQL/filter readout, parameter editor.
- Map/table preview and empty-result state.
- Save query as content item/version and use as input to map/dashboard/report/app/workflow.

Backend dependencies:

- Saved query package, validation, preview planning, permission, cost estimate, result persistence.

Acceptance:

- Ambiguous layer/field/CRS intent asks for clarification.
- Expensive or permission-blocked queries show actionable blockers before execution.

### UI-022: Natural-Language To Spatial Analysis Builder

Parent: `honua-console#16`, `honua-console#17`

Design refs:

- `console-canvas/screens-studio-rest.jsx` (`StudioAnalysisEditor`)
- [Workflow Catalog](../design-handoff/workflow-catalog.md) §2

Build:

- Analysis plan card with method, inputs, parameters, output schema, compute profile, runtime/cost estimate.
- DAG/pipeline view for multi-step analysis.
- Preview or submit job actions.
- Result artifact panel with rerun controls.

Backend dependencies:

- Analysis package API.
- Job runner execution and artifact contracts from `honua-server#681`, `#721`, `#724`, `#1170`.

Acceptance:

- Analysis output can become a content item, layer, report/dashboard input, or workflow input.
- Failed jobs show logs, failure classification, and rerun affordance.

### UI-023: Map Builder, Style, Popup, Layer, And Publish Flow

Parent: `honua-console#16`

Design refs:

- `console-canvas/screens-studio.jsx` (`StudioMapAI`, `StudioMapEditor`, `StudioMapPublish`)
- `console-canvas/screens-styling.jsx`
- `console-canvas/screens-styling-more.jsx`

Build:

- Map package editor with layers, filters, style, popup, legend, basemap, extent, interactions.
- MapLibre canonical style editing with generated SLD/Esri/QGIS sidecars where applicable.
- Publish review with dependency, visibility, route/share/embed, rollback, and provenance.

Backend dependencies:

- Map package lifecycle, style endpoint/contracts, publication registry.

Acceptance:

- A generated map can be saved, reopened, edited, published, shared, embedded, and rolled back.
- Style overrides clearly show whether they round-trip to the canonical style.

### UI-024: Dashboard And Report Builder With Vega-Lite

Parent: `honua-console#16`

Design refs:

- `console-canvas/screens-studio.jsx` (`StudioDashboardAI`, `StudioDashboardEditor`, `StudioDashboardPublish`)
- `console-canvas/screens-studio-rest.jsx` (`StudioReportEditor`)
- [Workflow Catalog](../design-handoff/workflow-catalog.md) §4

Build:

- Dashboard/report package editor with data bindings, layout slots, charts, map panels, tables, filters, and narrative.
- Vega-Lite chart editor with visual controls and raw spec inspection.
- Version pinning for embedded items.
- Responsive preview and publish review.

Backend dependencies:

- Dashboard/report package validators.
- Vega-Lite spec validation, preview data endpoint, aggregation/data binding contract.

Acceptance:

- Chart package stores Vega-Lite as the standard chart spec.
- Dashboard/report can be shared and embedded through the same publication model as maps/apps.

### UI-025: Form Builder And Field Workflow

Parent: `honua-console#16`

Status: implemented by `honua-console#57` as the server-bound `/studio/form` builder. The form package lifecycle binds to honua-server#1184 through the temporary `Honua.Console.Contracts` shim (`IHonuaFormPackageClient`) until `honua-sdk-dotnet#166` is available to `honua-console#7`; renders a missing-binding state when no server base address is configured.

Design refs:

- `console-canvas/screens-studio-form-workflow.jsx` (`StudioFormAI`, `StudioFormBuilder`)
- [Workflow Catalog](../design-handoff/workflow-catalog.md) §5

Build:

- Implemented in this slice: form package editor with fields, groups, validation, domains, conditional visibility, attachment rules, privacy rules, offline policy, and submit target.
- Implemented in this slice: save, reopen, server validation, and publish gating through the honua-server form package lifecycle.
- Deferred from the original design: desktop/tablet/mobile preview and optional app package publish review remain generated-app/runtime follow-ons.

Backend dependencies:

- Form package lifecycle/offline-policy API is satisfied for the builder by `honua-server#1184`; form submission, attachment ingestion, and audit runtime stay server-owned follow-ons outside this authoring slice.
- Back-office field workflow parity from `honua-server#1158` if it owns reusable contracts.

Acceptance:

- Offline/sync policy is explicit before publish.
- Submit target is configured, saved, and server-validated before publish; edits after validation require another save and validation pass.
- Published form packages declare server-owned submission, attachment, and offline policy; submission ingestion and audit evidence remain backend/runtime follow-ons.
- Published versions are terminal in the builder until reopened as a draft.

### UI-026: App Builder And Generated App Lifecycle

Parent: `honua-console#5`, `honua-console#16`

Design refs:

- `console-canvas/screens-studio-rest.jsx` (`StudioAppEditor`)

Routes:

- `/studio/apps/:itemId/preview`

Build:

- App package editor with pages, components, navigation, data bindings, actions, permissions, and share/embed policy.
- Generated app preview/reopen flow.
- Revision selector and publish controls.

Backend dependencies:

- App package lifecycle and generated app read/publish contract.

Acceptance:

- Legacy `/apps/:itemId/preview?revision=<n>` behavior has a Console equivalent.
- Reopened app edits create new content versions rather than mutating published state.

### UI-027: Unified GP / ETL Workflow Editor

Parent: `honua-console#17`

Design refs:

- `console-canvas/screens-studio-form-workflow.jsx` (`StudioWorkflowAI`, `StudioWorkflowEditor`)
- `console-canvas/screens-studio-rest.jsx` (`StudioAnalysisEditor`)
- [Workflow Catalog](../design-handoff/workflow-catalog.md) §7

Build:

- Workflow package graph editor with node palette, node inspector, input/output schema, schedule, worker profile, and failure edges.
- Dry-run panel with sample data, logs, artifacts, and output schema.
- Publish modes: batch workflow, scheduled job, eligible GP/process service endpoint.

Backend dependencies:

- Unified workflow package, node registry, GP/ETL execution, dry run, job-runner publication.
- `honua-server#360`, `#361`, `#682`, `#681`, `#721`, `#724`.

Acceptance:

- A workflow can be dry-run, versioned, published to job runner, and monitored in Operate jobs/events.
- Eligible workflows can expose an invocation endpoint with parameter validation.

### UI-028: Studio Collaboration, Comments, And Scoped RBAC Visualization

Parent: `honua-console#5`, `honua-console#7`

Design refs:

- `console-canvas/screens-collab-rbac.jsx`

Build:

- Presence/cursors/comments/artifact annotations as optional Studio collaboration layer.
- Comment thread drawer with feature/resource/package anchors.
- RBAC overview and scoped invite UX for workspace, environment, content, and time-limited grants.

Backend dependencies:

- Collaboration/comment persistence if retained for MVP.
- RBAC scope projection from `honua-server#1162`.

Acceptance:

- Collaboration features degrade cleanly when realtime/collab capability is unavailable.
- Invite actions show exact scope and expiration before submit.

## Operate, Admin, And Publishing Slices

### UI-030: Connections Workspace And Wizards

Parent: `honua-console#6`

Design refs:

- `console-canvas/screens-connections.jsx`

Routes:

- `/operate/connections`
- `/operate/connections/new`
- `/operate/connections/:id`
- `/operate/connections/:id/diagnostics`

Build:

- Connections list, detail, table browser, diagnostics, and add-connection wizard.
- Clear separation between persistent connections and one-time remote-service migration/import flows.

Backend dependencies:

- Connection list/detail/test/diagnostics APIs from server/admin transition.

Acceptance:

- Failed connection tests produce actionable, non-secret diagnostics.
- Secrets never render in client logs or screenshots.

### UI-031: Data Resources List And Detail

Parent: `honua-console#6`, `honua-console#7`

Design refs:

- `console-canvas/screens-resources.jsx`
- `console-canvas/screens-fields-hifi.jsx`
- `console-canvas/screens-settings-states.jsx`

Routes:

- `/operate/layers/:id`
- Future resource detail route if separated from layer route.

Build:

- Resource list table/grouped views.
- Resource detail tabs: overview, source, fields, metadata, publish, access, validation, presentation, advanced.
- Source variants for live table and migrated remote service.
- Field state vocabulary and override/revert semantics.

Backend dependencies:

- Metadata v2 resource graph, resource detail, field/domain/metadata/access/validation contracts.

Acceptance:

- User can see where a resource is used before editing.
- Resource edits clearly show blast radius and validation state.

### UI-032: Resource Presentation, Styles, Popups, And Slot Overrides

Parent: `honua-console#6`, `honua-console#16`

Design refs:

- `console-canvas/screens-styling.jsx`
- `console-canvas/screens-styling-more.jsx`
- `console-canvas/screens-settings-states.jsx` presentation artboards

Build:

- MapLibre canonical style editor host.
- OGC API Styles endpoint view with generated SLD/Esri Renderer/QGIS QML sidecars.
- Per-slot style overrides for Esri Renderer and WMS SLD with cannot-round-trip warnings.
- Style version history, diff, resync-confirm dialog, popup/template editor.

Backend dependencies:

- Style storage, generated sidecars, version history, publication slot style override contracts.

Acceptance:

- Canonical resource style and per-publication override are visually distinct.
- Resync warns exactly what override state will be discarded.

### UI-033: Resource Creation And Remote-Service Migration Wizards

Parent: `honua-console#6`

Design refs:

- `console-canvas/screens-wizards.jsx`

Build:

- Create resource from table.
- Create resource from file/FileGDB.
- Import remote service as one-time migration, not proxy/sync/mirror.
- Validation distinguishes blocker, warning, and missing-optional states.

Backend dependencies:

- Import/migration job APIs, file upload/import APIs, validation/job contracts.

Acceptance:

- Imported remote services become Honua-managed resources.
- Migration jobs deep-link to Operate job detail.

### UI-034: Services And Layers Workspace

Parent: `honua-console#6`

Design refs:

- `console-canvas/screens-services.jsx`

Routes:

- `/operate/services/:name/settings`
- `/operate/layers/:id`

Build:

- Services flat list and explorer tree.
- Service detail layers tab with publication slots.
- Runtime settings: identity, CRS/extent, query limits, capabilities, output formats, caching, access summary, catalog registration, schedule.
- Context actions: open resource, map preview, copy URL, add/unpublish layer.

Backend dependencies:

- Service/layer/slot runtime config APIs.
- Catalog registration contracts.

Acceptance:

- Services expose layers but do not own canonical resource metadata.
- Runtime changes show whether they require restart, republish, or immediate apply.

### UI-035: Publishing Workspace, Quick Publish, Author-First Publish, And Matrix

Parent: `honua-console#6`, `honua-console#16`

Design refs:

- `console-canvas/screens-quick-publish.jsx`
- `console-canvas/screens-publish-flow.jsx`
- `console-canvas/screens-services.jsx` (`PublishMatrixA`, `PublishMatrixB`)
- [Workflow Catalog](../design-handoff/workflow-catalog.md) §8

Routes:

- `/operate/publishing`

Build:

- Quick publish: service -> layer -> review.
- Author-first publish: target service -> layer/projection -> preview -> review.
- Publishing matrix across resources and formats.
- Publication review for service slots and catalog entries.

Backend dependencies:

- Publication, validation, service/layer slot, catalog registration, job, rollback contracts.

Acceptance:

- Review shows resource, slot, generated endpoints, catalog registration, policy, warnings, and rollback class.
- Publish produces content/publication/job/event/audit records.

### UI-036: Settings, Identity, CORS, License, API Keys, And Catalog Endpoints

Parent: `honua-console#6`

Design refs:

- `console-canvas/screens-settings-states.jsx`
- `console-canvas/screens-catalogs.jsx`

Routes:

- `/operate/identity/providers`
- `/operate/identity/status`
- `/operate/identity/diagnostics`
- `/operate/license`
- Settings route to be finalized under `/operate`

Build:

- Auth providers, API keys, CORS, license, about/server info surfaces.
- Catalog endpoint toggles for Esri catalog, OGC API Records, OData, STAC, DCAT.
- Standards mapping and catalog item presentation editor.

Backend dependencies:

- Admin settings, identity, license, CORS, API key, catalog endpoint APIs.

Acceptance:

- Turning off a catalog endpoint disables per-publication registration without deleting services.
- Settings changes show apply scope and restart requirement.

## Operate Observability, GitOps, Temporal, And AI DevOps Slices

### UI-040: Environment And Fleet Overview

Parent: `honua-console#24`

Design refs:

- `console-canvas/screens-environments.jsx` (`EnvironmentsList`, `EnvironmentDetail`)

Build:

- Environment cards for dev/staging/prod/customer targets.
- Drift/compatibility table across environments.
- Fleet task list with health, version, uptime, latency, CPU/memory, restart state.

Backend dependencies:

- Server telemetry status, environment binding, deploy/GitOps status, fleet/task health.

Acceptance:

- Unknown telemetry is rendered as unknown/unconfigured, not failure.
- Environment switching updates every Operate deep link.

### UI-041: Event Viewer, Logs, Raw Evidence, And Investigations

Parent: `honua-console#24`

Design refs:

- `console-canvas/screens-event-viewer.jsx`
- [Workflow Catalog](../design-handoff/workflow-catalog.md) §13

Build:

- Unified event timeline/table across logs, audit, jobs, alerts, releases, sync, data changes, telemetry.
- Filter builder for time, type, severity, environment, server, resource, actor, correlation id, trace id, job id, release id, replica id, change set id.
- Detail drawer with related objects, raw evidence, lifecycle, AI advisory.
- Investigation pinning and notes.

Backend dependencies:

- `honua-server#1168`, SIEM/log/evidence links, audit coverage.

Acceptance:

- AI summary never hides raw evidence.
- Every row can deep-link to a related resource/job/release/alert when available.

### UI-042: Alerts, Realtime Rules, Geofence Rules, And Delivery State

Parent: `honua-console#24`

Design refs:

- `console-canvas/screens-environments.jsx` (`AlertsList`)
- [Workflow Catalog](../design-handoff/workflow-catalog.md) §14

Build:

- Alert list/detail with acknowledge, suppress, resolve, assign, investigate.
- Rule list/detail for geofence, spatial filter, threshold, dwell, temporal window, job status, data quality, sync conflict, release/SLO.
- Geofence zone editor/selector hook, condition builder, delivery channel status, dead-letter/retry status.

Backend dependencies:

- `honua-server#1169`, `#393`, `#339`.
- Alert delivery and realtime capability contracts.

Acceptance:

- Invalid rules cannot be enabled.
- Delivery failures are visible and linked to evidence.

### UI-043: Jobs Viewer And Job Detail

Parent: `honua-console#24`

Design refs:

- `console-canvas/screens-activity.jsx`
- [Workflow Catalog](../design-handoff/workflow-catalog.md) §15

Build:

- Job list filtered by status, type, queue, actor, resource, time, environment.
- Job detail with stages, progress, logs, artifacts, metrics, failure classification, related events.
- Allowed actions: retry, cancel, pause/resume, approve, promote, rerun.

Backend dependencies:

- `honua-server#1170`.

Acceptance:

- Jobs launched from Studio, publishing, GitOps, temporal, alert delivery, import, and maintenance deep-link to the same detail surface.
- Disabled actions explain policy or state reason.

### UI-044: GitOps Metadata Release Proposal, CI Timeline, And Rollback

Parent: `honua-console#22`

Design refs:

- `console-canvas/screens-gitops-temporal-sync.jsx` (`GitOpsRelease`, `GitOpsCITimeline`)
- [Workflow Catalog](../design-handoff/workflow-catalog.md) §9

Build:

- Release proposal summary and semantic resource diff.
- Environment matrix and drift state.
- Compatibility preflight table and data script coverage.
- Git PR preview, CI/GitOps timeline, smoke/SLO watch, rollback summary.

Backend dependencies:

- `honua-server#1163`, `#1164`, `#1165`.
- `honua-devops#57`, `#58`.

Acceptance:

- Blockers prevent PR creation/deploy action.
- Rollback readiness and rollback window are visible before apply.

### UI-045: Temporal Data Viewer

Parent: `honua-console#23`

Design refs:

- `console-canvas/screens-gitops-temporal-sync.jsx` (`TemporalViewer`)
- [Workflow Catalog](../design-handoff/workflow-catalog.md) §10

Build:

- Capability state, retention policy, checkpoint selector, time scrubber.
- As-of map/table, now-vs-as-of comparison, diff summary, feature timeline entry points.
- Rollback review hook when policy/capability allows.

Backend dependencies:

- `honua-server#1166`.
- `honua-sdk-js#227`.

Acceptance:

- Unsupported temporal sources render a capability explanation, not an empty viewer.
- Rollback creates a governed job/checkpoint rather than erasing history.

### UI-046: Disconnected Sync Conflict Review

Parent: `honua-console#23`

Design refs:

- `console-canvas/screens-gitops-temporal-sync.jsx` (`SyncConflictsList`, `SyncConflictReview`)
- [Workflow Catalog](../design-handoff/workflow-catalog.md) §11

Build:

- Replica conflict queue with owner/device/base checkpoint/sync generation/status.
- Base/client/server comparison and field/geometry conflict markers.
- Resolution choices: accept client, keep server, merge fields, choose geometry, reject, defer.
- Batch resolution panel and audit/evidence panel.

Backend dependencies:

- `honua-server#1167`.
- `honua-sdk-js#228`.

Acceptance:

- Conflict resolution writes audit evidence and committed change set.
- Named replica terminology aligns with Esri sync concepts without forking the model.

### UI-047: AI DevOps Advisory Home And Brief Detail

Parent: `honua-console#24`

Design refs:

- `console-canvas/screens-native-aidevops.jsx` (`AIDevopsConsole`, `AIDevopsBrief`)

Build:

- AI DevOps home with briefs, affected resources, evidence count, suggested actions, confidence, and owner/status.
- Brief detail with evidence table, suggested actions, counterfactual, reasoning trace link.
- Operations are advisory until the user explicitly opens the governed workflow.

Backend dependencies:

- `honua-devops#58`.
- Event/release/job/alert evidence APIs.

Acceptance:

- AI cannot auto-apply remediation from this surface.
- Every recommendation has raw evidence links and target workflow deep links.

## Native Console Slices

### UI-050: Native Console First Run, Environment Profiles, mTLS, And Diagnostics

Parent: `honua-console#26`

Status: implemented by `honua-console#44` for the shared Blazor shell and native core. Server trust validation binds to honua-server#1171 through the temporary `Honua.Console.Contracts` shim until `honua-sdk-dotnet#166` is available to `honua-console#7`.

Design refs:

- `console-canvas/screens-native-aidevops.jsx` (`NativeHostFirstRun`, `NativeHostProfiles`)
- [Workflow Catalog](../design-handoff/workflow-catalog.md) §16

Build:

- Native first-run profile creation.
- Multi-environment profile list, connect/disconnect, last-seen, diagnostics.
- Transport indicators for browser HTTP, browser realtime, native gRPC, native mTLS.
- Certificate selection/status/warning flows.
- Server-bound client-certificate validation, trust-on-first-use server fingerprint pinning, and acknowledge/revalidate actions.

Backend dependencies:

- `honua-server#1171` client-certificate validation endpoint.
- `honua-sdk-dotnet#166` environment/trust projections; temporarily mirrored in `Honua.Console.Contracts`.

Acceptance:

- Web Console renders native-only capabilities as unsupported without taking a native dependency.
- Certificate changes block connection until acknowledged or revalidated.
- Environment profiles represent multiple managed servers with profile-scoped trust/session state.
- mTLS/trust behavior is asserted in the opt-in Testcontainers lane (`tests/Honua.Console.IntegrationTests`, `scripts/integration-trust-check.sh`).

## Filed GitHub Issues

These child issues are projected from Specifica items under `agent-delivery-spec/.specifica/`.

| Issue | Parent | Specifica slug |
| --- | --- | --- |
| [honua-console#33](https://github.com/honua-io/honua-console/issues/33) | `#2`, `#3`, `#7` | `console-shell-navigation-route-guards-and-state-primitives` |
| [honua-console#34](https://github.com/honua-io/honua-console/issues/34) | `#4`, `#9` | `console-catalog-search-detail-viewer-and-share-link-parity` |
| [honua-console#35](https://github.com/honua-io/honua-console/issues/35) | `#4`, `#16` | `console-share-public-links-embeds-open-data-stac-dcat-and-exports` |
| [honua-console#36](https://github.com/honua-io/honua-console/issues/36) | `#6` | `console-operate-connections-resources-services-settings-transition` |
| [honua-console#37](https://github.com/honua-io/honua-console/issues/37) | `#16` | `console-publishing-workspace-quick-publish-author-first-publish-and-matrix` |
| [honua-console#38](https://github.com/honua-io/honua-console/issues/38) | `#5` | `console-studio-package-shell-prompt-clarification-inspector` |
| [honua-console#39](https://github.com/honua-io/honua-console/issues/39) | `#16` | `console-studio-query-analysis-map-dashboard-report-form-app-editors` |
| [honua-console#40](https://github.com/honua-io/honua-console/issues/40) | `#17` | `console-studio-unified-gp-etl-workflow-editor` |
| [honua-console#41](https://github.com/honua-io/honua-console/issues/41) | `#24` | `console-operate-event-viewer-alerts-realtime-rules-jobs-investigations` |
| [honua-console#42](https://github.com/honua-io/honua-console/issues/42) | `#22` | `console-gitops-metadata-release-visualization-and-rollback` |
| [honua-console#43](https://github.com/honua-io/honua-console/issues/43) | `#23` | `console-temporal-viewer-and-disconnected-sync-conflict-review` |
| [honua-console#44](https://github.com/honua-io/honua-console/issues/44) | `#26` | `console-native-profiles-trust-diagnostics-and-mtls` |

Do not start child implementation that requires package, temporal, sync, GitOps, event, alert, job, or native contracts until its honua-server backend dependency is landed. A checked-in mock does not unblock it and is not an acceptable merged data source — every server-backed slice binds to a real honua-server with a Testcontainers integration test ([Console Patterns Charter §11](../migration/CONSOLE_PATTERNS_CHARTER.md)).
