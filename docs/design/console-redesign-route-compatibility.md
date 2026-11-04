# Console redesign: phase 0–1 route compatibility

Planning baseline: `origin/trunk` at `0ea1f168757e12f167d9220ca41022d928c642fc`.
This is the chrome, accessibility and Home slice. The amended redesign review
controls scope; the [route map](../console-route-map.md) and
[Patterns Charter](../migration/CONSOLE_PATTERNS_CHARTER.md) retain their auth,
sharing and capability contracts. The source inventory below resolves stale
builder descriptions in the route map. This document specifies acceptance,
not a claim that the redesign has shipped.

Companions: [identities and mutations](console-redesign-identities.md),
[endpoints and owners](console-redesign-capabilities.md),
[journey acceptance](console-redesign-acceptance.md).

## Phase 1 navigation

Every target is an existing route. No new redirect, editor namespace, universal
search, context tree, empty Actions menu, or replacement object page ships here.
The Studio entry may be labelled “Studio”; it must not conceal Processes.
All destination reads retain server authorization; a visible link is not a grant.

| Entry | Current destination retained in phases 0 and 1 | Availability / source owner |
|---|---|---|
| Home | `/` | `ConsoleHomePage`; connected work queue or first-run setup |
| Data & Layers | `/operate/data` | `OperateDataPage`; server binding / per-section result |
| Connections | `/operate/connections` | `OperateConnectionsPage`; existing operator read |
| Services | `/operate/services` → `/operate/data?view=services` | `OperateDataRedirects`; preserve existing alias |
| Layers | `/operate/layers` | `OperateLayersPage`; dedicated list, not a redirect |
| Studio | `/studio` | `StudioPage`; `Studio:EmbedJsStudio` |
| Processes | `/studio/processes` | `ProcessesWorkspacePage`; same embed setting |
| Approvals | `/inbox` (also `/approvals`) | `ApprovalInboxPage`; source-specific read/decision grants |
| Activity | `/operate/observability` | `OperateObservabilityPage`; existing read results |
| Health | `/operate/health` | `OperateHealthPage`; snapshot result |
| Findings (Home link) | `/operate/copilot` | `OperateCopilotPage`; deterministic finding result |
| Geoprocessing | `/operate/geoprocessing` | `OperateGeoprocessingPage`; actual job reads |
| Metrics | `/operate/metrics` | `OperateMetricsPage`; existing data/capability state |
| Deploy | `/operate/deploy` | `OperateDeployPage`; mutation grants unchanged |
| Metadata releases | `/operate/releases` | `OperateReleasesPage`; open by package ID; no invented list |
| Catalog | `/catalog` | `CatalogPage`; persistent-catalog gate and product mode |
| Share | `/share/public` | `SharePage`; persistent-catalog gate and product mode |
| Operate | `/operate` | `OperatePage`; existing operator boundary |
| Access / members (existing tools) | `/operate/access`, `/operate/access/members` | Existing role/member pages; server RBAC; no `/access/*` migration |
| Settings (existing tools) | `/operate/settings` | `OperateSettingsPage`; no `/settings` migration |
| Environment | `/environments` | Profile routes; preserve witness-mode visibility |
| Support | `/support` | `SupportPage`; support-binding state |
| First-run actions | `/setup/local`, `/environments/new` | `LocalSetupPage`, `EnvironmentProfileNewPage`; retained on Home |
| Add data / new map (Home actions) | `/operate/data/new`, `/studio` | Existing flow and embed boundaries; no unsupported New service/Invite CTA |

Navigation evidence: [ConsoleLayout](../../src/Honua.Console.Shell/Layout/ConsoleLayout.razor),
[ConsoleRouteMap](../../src/Honua.Console.Shell/Models/ConsoleRouteMap.cs).
Page names in the tables are under `src/Honua.Console.Shell/Pages/` unless noted.

## Deep-link compatibility

| Route family | Phase 0–1 disposition | Identity / query / history acceptance |
|---|---|---|
| `/maps/{MapId}` | Keep content-map viewer (`MapViewerPage`) | Content ID; authorized anonymous `?token=` reads retained; never an editor |
| `/maps/new?from=...` | Keep `MapDraftPage` | Authenticated catalog-source hydration and continuation unchanged |
| `/maps/published/{**route}` | Keep Web publication endpoint and `PublishedMapDocument` | Studio publication route, independently rendered; no operator chrome/auth substitution |
| `/embed/maps/{MapId}` | Keep shellless viewer | Frozen path; eligible reads stay 200; bearer only in `#embedToken=`; query token rejection retained |
| `/catalog/{IdOrSlug}` | Keep detail | Preserve filters, tab, validated public-link token behavior; signed-in read takes precedence |
| `/share/public`, `/public`, both `/.../items/{IdOrSlug}` aliases | Keep collection/detail aliases | Frozen eligible item paths remain 200; no redesign redirects; public open-data eligibility unchanged |
| `/studio`, `/studio/proof`, `/studio/drafts`, `/studio/apps/{ItemId}/preview` | Keep `StudioPage` aliases | Preserve source/item/revision query and Studio lifecycle; no blanket `/studio/*` redirect |
| `/studio/processes`, `/studio/styles*`, `/studio/automations*`, `/studio/workflows/{DraftId}`, `/studio/form`, `/studio/report` | Keep current owners | Processes and live authoring remain distinct; no builder resurrection |
| Retired builder paths | Keep `RetiredStudioBuilderPage` | Guidance to Studio/Processes; no live replacement editor |
| `/operate/resources`, `/operate/resources/new`, `/operate/services`, `/operate/alerts` | Keep exact `OperateDataRedirects` behavior | Replace-history navigation to data, add-data, services-view, or observability `#alerts`; this implementation constructs the target and does not forward arbitrary original query/hash |
| `/operate/resources/{ResourceId}`, import and import/task | Keep dedicated pages | Never swallowed by the resources-list alias; file/task identity retained |
| `/operate/layers/{ResourceId}` and metadata/filter/style/relationships children | Keep dedicated pages | Canonical resource or unambiguous admin integer; ambiguous IDs show choices, unknown IDs show missing state |
| `/operate/services/{ServiceName}/settings` | Keep settings page | Service name remains opaque route input; no fabricated connection parent or slug from display name |
| `/inbox?proposalId=...`, `/approvals` | Keep approval owner | Source and proposal ID immutable; no auto-approval or navigation-triggered mutation |
| `/operate/deploy`, `/operate/releases/{SelectedReleaseId}` | Keep separate owners | Deploy operation ID is not server version or GitOps package ID; no `/releases/{version}` decision route |
| `/operate/events/{SelectedEventId}`, `/operate/alerts/{SelectedAlertId}`, `/operate/jobs/{SelectedJobRunId}` | Keep observability owner | Missing requested ID cannot fall through to a different event/alert/job; events remain events |
| `/operate/events?jobId=...`, `/operate/geoprocessing/{SelectedJobRunId}` | Keep evidence routes | Server-issued job ID only; workflow-run ID is not a job ID |
| Auth, environment, support, remaining Operate routes | Keep current owner and route | Same read grants, safe return URLs, unknown-object states and product mode boundaries |

All unchanged direct links must retain their complete allowed query and fragment
on reload and browser Back/Forward. Existing explicit aliases retain the behavior
above; phases 0–1 introduce no new lossy mapping. Test reserved literal paths
(`new`, `import`, `rules`) before parameter routes. Do not guess an ID from a label,
decode and re-encode route inputs twice, or infer a renamed/deleted object.
A future editor namespace would be `/studio/maps/{draftId}`, but no such route is
implemented or advertised in this slice.

## Baseline route owners

The following source-derived inventory is unchanged in both phases. Every template
was read from the baseline's `@page` declarations; it is not a promise that every
server capability is present. Web auth/BFF endpoints are covered in the endpoint
matrix. Shell route uniqueness and direct-load tests remain mandatory.

| Baseline page owner | Route templates retained |
|---|---|
| [ApprovalInboxPage](../../src/Honua.Console.Shell/Pages/ApprovalInboxPage.razor) | `/inbox`, `/approvals` |
| [AuthSignInPage](../../src/Honua.Console.Shell/Pages/AuthSignInPage.razor) | `/auth/signin` |
| [CatalogDetailPage](../../src/Honua.Console.Shell/Pages/CatalogDetailPage.razor) | `/catalog/{IdOrSlug}` |
| [CatalogItemEditorPage](../../src/Honua.Console.Shell/Pages/CatalogItemEditorPage.razor) | `/operate/catalogs/{Key}/items/{ItemId}` |
| [CatalogPage](../../src/Honua.Console.Shell/Pages/CatalogPage.razor) | `/catalog` |
| [CatalogsEndpointDetailPage](../../src/Honua.Console.Shell/Pages/CatalogsEndpointDetailPage.razor) | `/operate/catalogs/{Key}` |
| [CatalogsListPage](../../src/Honua.Console.Shell/Pages/CatalogsListPage.razor) | `/operate/catalogs` |
| [ConsoleHomePage](../../src/Honua.Console.Shell/Pages/ConsoleHomePage.razor) | `/` |
| [DatasetImportTaskPage](../../src/Honua.Console.Shell/Pages/DatasetImportTaskPage.razor) | `/operate/resources/import/task` |
| [EmbedMapPage](../../src/Honua.Console.Shell/Pages/EmbedMapPage.razor) | `/embed/maps/{MapId}` |
| [EnvironmentProfileDetailPage](../../src/Honua.Console.Shell/Pages/EnvironmentProfileDetailPage.razor) | `/environments/{ProfileId}` |
| [EnvironmentProfileNewPage](../../src/Honua.Console.Shell/Pages/EnvironmentProfileNewPage.razor) | `/environments/new` |
| [EnvironmentProfilesPage](../../src/Honua.Console.Shell/Pages/EnvironmentProfilesPage.razor) | `/environments` |
| [ImportEsriDashboardPage](../../src/Honua.Console.Shell/Pages/ImportEsriDashboardPage.razor) | `/operate/import/esri/dashboard` |
| [ImportEsriInstantAppPage](../../src/Honua.Console.Shell/Pages/ImportEsriInstantAppPage.razor) | `/operate/import/esri/instant-app` |
| [ImportEsriNotebookPage](../../src/Honua.Console.Shell/Pages/ImportEsriNotebookPage.razor) | `/operate/import/esri/notebook` |
| [ImportEsriWebMapPage](../../src/Honua.Console.Shell/Pages/ImportEsriWebMapPage.razor) | `/operate/import/esri/web-map` |
| [ImportEsriWizardPage](../../src/Honua.Console.Shell/Pages/ImportEsriWizardPage.razor) | `/operate/import/esri` |
| [ImportStoryMapPage](../../src/Honua.Console.Shell/Pages/ImportStoryMapPage.razor) | `/operate/import/esri/storymap` |
| [LocalSetupPage](../../src/Honua.Console.Shell/Pages/LocalSetupPage.razor) | `/setup/local` |
| [MapDraftPage](../../src/Honua.Console.Shell/Pages/MapDraftPage.razor) | `/maps/new` |
| [MapViewerPage](../../src/Honua.Console.Shell/Pages/MapViewerPage.razor) | `/maps/{MapId}` |
| [NotFoundPage](../../src/Honua.Console.Shell/Pages/NotFoundPage.razor) | `/not-found` |
| [OmniPromptConsolePage](../../src/Honua.Console.Shell/Pages/OmniPromptConsolePage.razor) | `/operate/ai` |
| [OperateAccessMembersPage](../../src/Honua.Console.Shell/Pages/OperateAccessMembersPage.razor) | `/operate/access/members` |
| [OperateAccessRolesPage](../../src/Honua.Console.Shell/Pages/OperateAccessRolesPage.razor) | `/operate/access` |
| [OperateAlertRuleCreatePage](../../src/Honua.Console.Shell/Pages/OperateAlertRuleCreatePage.razor) | `/operate/alerts/rules/new` |
| [OperateAlertRuleDetailPage](../../src/Honua.Console.Shell/Pages/OperateAlertRuleDetailPage.razor) | `/operate/alerts/rules/{RuleId}` |
| [OperateAlertRulesPage](../../src/Honua.Console.Shell/Pages/OperateAlertRulesPage.razor) | `/operate/alerts/rules` |
| [OperateConnectionDetailPage](../../src/Honua.Console.Shell/Pages/OperateConnectionDetailPage.razor) | `/operate/connections/{ConnectionId}`, `/operate/connections/{ConnectionId}/diagnostics` |
| [OperateConnectionNewPage](../../src/Honua.Console.Shell/Pages/OperateConnectionNewPage.razor) | `/operate/connections/new` |
| [OperateConnectionsPage](../../src/Honua.Console.Shell/Pages/OperateConnectionsPage.razor) | `/operate/connections` |
| [OperateCopilotPage](../../src/Honua.Console.Shell/Pages/OperateCopilotPage.razor) | `/operate/copilot` |
| [OperateDataFlowPage](../../src/Honua.Console.Shell/Pages/OperateDataFlowPage.razor) | `/operate/data/new` |
| [OperateDataPage](../../src/Honua.Console.Shell/Pages/OperateDataPage.razor) | `/operate/data` |
| [OperateDataRedirects](../../src/Honua.Console.Shell/Pages/OperateDataRedirects.razor) | `/operate/resources`, `/operate/services`, `/operate/resources/new`, `/operate/alerts` |
| [OperateDeployPage](../../src/Honua.Console.Shell/Pages/OperateDeployPage.razor) | `/operate/deploy` |
| [OperateEventsPage](../../src/Honua.Console.Shell/Pages/OperateEventsPage.razor) | `/operate/events` |
| [OperateGeoprocessingPage](../../src/Honua.Console.Shell/Pages/OperateGeoprocessingPage.razor) | `/operate/geoprocessing/{SelectedJobRunId}` |
| [OperateHealthPage](../../src/Honua.Console.Shell/Pages/OperateHealthPage.razor) | `/operate/health` |
| [OperateImportFilePage](../../src/Honua.Console.Shell/Pages/OperateImportFilePage.razor) | `/operate/resources/import` |
| [OperateImportServicePage](../../src/Honua.Console.Shell/Pages/OperateImportServicePage.razor) | `/operate/import/service` |
| [OperateLayerDetailPage](../../src/Honua.Console.Shell/Pages/OperateLayerDetailPage.razor) | `/operate/layers/{ResourceId}` |
| [OperateLayerMetadataPage](../../src/Honua.Console.Shell/Pages/OperateLayerMetadataPage.razor) | `/operate/layers/{ResourceId}/metadata` |
| [OperateLayerRelationshipsPage](../../src/Honua.Console.Shell/Pages/OperateLayerRelationshipsPage.razor) | `/operate/layers/{ResourceId}/relationships` |
| [OperateLayerStylePage](../../src/Honua.Console.Shell/Pages/OperateLayerStylePage.razor) | `/operate/layers/{ResourceId}/style` |
| [OperateLayersPage](../../src/Honua.Console.Shell/Pages/OperateLayersPage.razor) | `/operate/layers` |
| [OperateMetricsPage](../../src/Honua.Console.Shell/Pages/OperateMetricsPage.razor) | `/operate/metrics` |
| [OperateObservabilityPage](../../src/Honua.Console.Shell/Pages/OperateObservabilityPage.razor) | `/operate/observability`, `/operate/events/{SelectedEventId}`, `/operate/alerts/{SelectedAlertId}`, `/operate/jobs/{SelectedJobRunId}` |
| [OperatePage](../../src/Honua.Console.Shell/Pages/OperatePage.razor) | `/operate` |
| [OperatePublishLayerPage](../../src/Honua.Console.Shell/Pages/OperatePublishLayerPage.razor) | `/operate/publishing/quick` |
| [OperatePublishingPage](../../src/Honua.Console.Shell/Pages/OperatePublishingPage.razor) | `/operate/publishing` |
| [OperateReleaseWitnessPage](../../src/Honua.Console.Shell/Pages/OperateReleaseWitnessPage.razor) | `/operate/release-witness` |
| [OperateReleasesPage](../../src/Honua.Console.Shell/Pages/OperateReleasesPage.razor) | `/operate/releases`, `/operate/releases/{SelectedReleaseId}` |
| [OperateResourceDetailPage](../../src/Honua.Console.Shell/Pages/OperateResourceDetailPage.razor) | `/operate/resources/{ResourceId}` |
| [OperateScenesPage](../../src/Honua.Console.Shell/Pages/OperateScenesPage.razor) | `/operate/scenes`, `/operate/scenes/{SceneId}` |
| [OperateSensorThingsPage](../../src/Honua.Console.Shell/Pages/OperateSensorThingsPage.razor) | `/operate/sensors`, `/operate/sensors/datastreams/{DatastreamId:long}` |
| [OperateServiceDetailPage](../../src/Honua.Console.Shell/Pages/OperateServiceDetailPage.razor) | `/operate/services/{ServiceName}/settings` |
| [OperateSettingsPage](../../src/Honua.Console.Shell/Pages/OperateSettingsPage.razor) | `/operate/settings` |
| [OperateSyncPage](../../src/Honua.Console.Shell/Pages/OperateSyncPage.razor) | `/operate/sync` |
| [OperateVersionConflictsPage](../../src/Honua.Console.Shell/Pages/OperateVersionConflictsPage.razor) | `/operate/versions/{ServiceId}/{VersionGuid}/conflicts` |
| [OperateVersionsPage](../../src/Honua.Console.Shell/Pages/OperateVersionsPage.razor) | `/operate/versions` |
| [ProcessesWorkspacePage](../../src/Honua.Console.Shell/Pages/ProcessesWorkspacePage.razor) | `/studio/processes` |
| [RetiredStudioBuilderPage](../../src/Honua.Console.Shell/Pages/RetiredStudioBuilderPage.razor) | `/studio/workflows/new`, `/studio/query`, `/studio/analysis`, `/studio/form/ai`, `/studio/map`, `/studio/report/ai`, `/studio/app`, `/studio/dashboard` |
| [ShareManagePage](../../src/Honua.Console.Shell/Pages/ShareManagePage.razor) | `/share/manage` |
| [SharePage](../../src/Honua.Console.Shell/Pages/SharePage.razor) | `/share`, `/share/public`, `/public` |
| [SharePublicItemPage](../../src/Honua.Console.Shell/Pages/SharePublicItemPage.razor) | `/share/public/items/{IdOrSlug}`, `/public/items/{IdOrSlug}` |
| [StudioAutomationEditorPage](../../src/Honua.Console.Shell/Pages/StudioAutomationEditorPage.razor) | `/studio/automations/new`, `/studio/automations/{DraftId}` |
| [StudioAutomationVersionsPage](../../src/Honua.Console.Shell/Pages/StudioAutomationVersionsPage.razor) | `/studio/automations/{DraftId}/versions` |
| [StudioAutomationsPage](../../src/Honua.Console.Shell/Pages/StudioAutomationsPage.razor) | `/studio/automations` |
| [StudioFormBuilderPage](../../src/Honua.Console.Shell/Pages/StudioFormBuilderPage.razor) | `/studio/form` |
| [StudioPage](../../src/Honua.Console.Shell/Pages/StudioPage.razor) | `/studio`, `/studio/proof`, `/studio/drafts`, `/studio/apps/{ItemId}/preview` |
| [StudioReportBuilderPage](../../src/Honua.Console.Shell/Pages/StudioReportBuilderPage.razor) | `/studio/report` |
| [StudioStyleEditorPage](../../src/Honua.Console.Shell/Pages/StudioStyleEditorPage.razor) | `/studio/styles`, `/studio/styles/{StyleId}` |
| [StudioWorkflowEditorPage](../../src/Honua.Console.Shell/Pages/StudioWorkflowEditorPage.razor) | `/studio/workflows/{DraftId}` |
| [SupportPage](../../src/Honua.Console.Shell/Pages/SupportPage.razor) | `/support` |
| [SupportTicketDetailPage](../../src/Honua.Console.Shell/Pages/SupportTicketDetailPage.razor) | `/support/tickets/{TicketId}` |
| [UnsavedChangesGuardDemoPage](../../src/Honua.Console.Shell/Pages/UnsavedChangesGuardDemoPage.razor) | `/studio/_dev/unsaved-changes-demo` |
