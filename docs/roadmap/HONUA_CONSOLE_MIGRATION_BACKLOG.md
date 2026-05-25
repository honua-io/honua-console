# Honua Console Migration Backlog

Status: filed 2026-05-23.

Portal freeze state: **none** (soft freeze begins when the first `honua-console#4` implementation PR opens after owner acknowledgement; hard freeze when `honua-console#9` enters review - see [`PORTAL_FREEZE_POLICY.md`](../migration/PORTAL_FREEZE_POLICY.md)).

Decision source: [ADR-0001: Unified Honua Console Runtime](../adr/0001-unified-honua-console-runtime.md).

Coordination contracts (binding on all child tickets):

- [Console Patterns Charter](../migration/CONSOLE_PATTERNS_CHARTER.md) - routing, RBAC, error/empty/loading surfaces, perf budgets, smoke conventions, file layout.
- [`honua-portal` Freeze And Retirement Policy](../migration/PORTAL_FREEZE_POLICY.md) - soft/hard freeze gates, exception path, retirement trigger.
- [SDK Shim Policy](../migration/SDK_SHIM_POLICY.md) - per-language .NET and browser shim boundaries while `honua-sdk-dotnet#166` and `honua-sdk-js#225` land; removed under `honua-console#7`.
- [Console Backend Capability Backlog](CONSOLE_BACKEND_CAPABILITY_BACKLOG.md) - backend/server/devops/sdk capability crosswalk required behind the design handoff and one deploy surface.
- [Console UI Implementation Backlog](CONSOLE_UI_IMPLEMENTATION_BACKLOG.md) - design-artifact breakdown into buildable UI slices, route targets, backend dependencies, and smoke expectations.

IA source: [Honua Console Route Map, RBAC, and Navigation](../console-route-map.md). Migration tickets `#4`, `#5`, `#6`, `#7`, and `#9` cite specific sections of this map for URL shapes, gates, empty states, and smoke evidence — see `console-route-map.md` §12 for the per-ticket section index.

## Objective

Port current `honua-portal` logic into `honua-console` and consolidate Studio, Catalog, Operate, and Share into one deployable Honua runtime.

This backlog intentionally separates three decisions:

- Product surface: one top-level Honua Console.
- Deployment runtime: one URL/origin, session model, RBAC model, metadata/content model, audit trail, and upgrade path.
- Repository layout: `honua-console` is the new web shell repo; `honua-portal` retires only after parity.

## Console Backlog

### Coordinating Epic

- [honua-console#1](https://github.com/honua-io/honua-console/issues/1): Epic: Honua Console migration and one deployable artifact.
- Planning artifact: [Console UI Implementation Backlog](CONSOLE_UI_IMPLEMENTATION_BACKLOG.md).

### P0 Blockers

- [honua-console#2](https://github.com/honua-io/honua-console/issues/2): Scaffold Blazor Web Console shell and shared Razor component library.
- [honua-console#3](https://github.com/honua-io/honua-console/issues/3): Define Console IA route map RBAC and navigation. Artifact: [docs/console-route-map.md](../console-route-map.md).
- [honua-console#6](https://github.com/honua-io/honua-console/issues/6): Integrate legacy Admin as transitional Operate surface. IA: route-map §1, §5 (Admin disposition map), §6.5, §11. See the [Legacy Admin Route Disposition](../migration/legacy-admin-route-disposition.md) and [Operate Embed Contract](../operate/embed-contract.md) landed for #6.

### P1 Migration And Runtime Work

- [honua-console#4](https://github.com/honua-io/honua-console/issues/4): Port Catalog Viewer Saved Maps Share Embed and Open Data from `honua-portal`. IA: route-map §1, §3 rows 5–11 and row 14 (`/data` to Catalog filter), §5.4, §6.3/6.4/6.6/6.7, §7, §8, §9, §10.
- [honua-console#5](https://github.com/honua-io/honua-console/issues/5): Port Honua Studio app-builder and generated-app lifecycle. IA: route-map §1, §3 rows 12–13, §5.2 RETIRE, §6.2, §7, §9, §10.
- [honua-console#7](https://github.com/honua-io/honua-console/issues/7): Wire Console to shared metadata content package and RBAC contracts. IA: route-map §4 (full RBAC reference), §6 per-route gates, §11 (`honua-server#1162`, `honua-sdk-dotnet#166`, `honua-sdk-js#225`).
- [honua-console#8](https://github.com/honua-io/honua-console/issues/8): Integrate Console with single deployable artifact and preview pipeline.
- [honua-console#9](https://github.com/honua-io/honua-console/issues/9): Console parity smoke: publish service to catalog to Studio artifact to share embed. IA: route-map §10 (smoke evidence map), §3 rows with smoke labels. Harness lives at [`smoke/parity/`](../../smoke/parity/); see [docs/smoke/parity.md](../smoke/parity.md).

### P1 AI GIS Studio Work

- [honua-console#19](https://github.com/honua-io/honua-console/issues/19): Define Studio information model for NL GIS artifacts and publishing.
- [honua-console#16](https://github.com/honua-io/honua-console/issues/16): Publish maps dashboards reports and apps from Studio.
- [honua-console#17](https://github.com/honua-io/honua-console/issues/17): Studio unified GP and ETL workflow editor.

### P1 GitOps Metadata Publishing Work

- Define GitOps metadata publishing information model and visualization design:
  - [GitOps Metadata Publishing Information Model](../architecture/gitops-metadata-publishing-information-model.md)
  - [GitOps Metadata Publishing Visualization Design](../architecture/gitops-metadata-publishing-visualization-design.md)
- [honua-console#22](https://github.com/honua-io/honua-console/issues/22): Visualize GitOps metadata release workflow in Console Operate.

### P2 Temporal Data History Work

- Define optional temporal data viewer information model:
  - [Temporal Data Viewer Information Model](../architecture/temporal-data-viewer-information-model.md)
- [honua-console#23](https://github.com/honua-io/honua-console/issues/23): Optional temporal data viewer for as-of diff attribution and rollback.

### P1 Operate Observability Work

- Define Operate observability information model:
  - [Operate Observability Information Model](../architecture/operate-observability-information-model.md)
- [honua-console#41](https://github.com/honua-io/honua-console/issues/41): Operate observability event viewer alerts realtime rules and jobs workspace.

### P1 Operate Transition Work

- [honua-console#36](https://github.com/honua-io/honua-console/issues/36): Native Operate transition routes for connections, data resources, services, layers, and operator settings. IA: route-map §1, §2.2, §6.5. Contract: [Native Operate Transition Surface](../operate/native-transition-surface.md). Legacy Admin rows remain under `/operate/legacy/*` until SDK-backed parity smoke retires each successor.

### P2 Native Console Work

- [honua-console#26](https://github.com/honua-io/honua-console/issues/26): Optional MAUI Blazor Hybrid native Console host.

### Cleanup

- [honua-console#10](https://github.com/honua-io/honua-console/issues/10): Freeze and retire `honua-portal` after Console parity. The freeze trigger, per-surface coverage, and per-repo retirement sequence are defined in two artifacts in this repo:
  - [Portal Parity Checklist](../migration/PORTAL_PARITY_CHECKLIST.md) — one row per Portal surface mapped to its Console destination route, owning ticket, status, and parity evidence. This is the gate the freeze decision reads.
  - [Portal Retirement Playbook](../migration/PORTAL_RETIREMENT_PLAYBOOK.md) — freeze trigger, per-repo retirement sequence, retention decision (archive read-only), and announcement path.

  Physical retirement happens via four bounded single-repo child tickets in `honua-portal`, `honua-server-admin`, and `honua-devops`. Those tickets are enumerated in the playbook and are intentionally filed outside this ticket, gated on honua-console#8 and honua-console#9.

## External Owner Dependencies

- [honua-server#1162](https://github.com/honua-io/honua-server/issues/1162): Console metadata v2 content and RBAC API baseline.
- [honua-sdk-js#225](https://github.com/honua-io/honua-sdk-js/issues/225): Console SDK contracts for metadata content packages dashboards reports and apps.
- [honua-sdk-js#226](https://github.com/honua-io/honua-sdk-js/issues/226): MCP QGIS and Studio parity for generated map dashboard report and app packages.
- [honua-sdk-js#227](https://github.com/honua-io/honua-sdk-js/issues/227): Temporal history capability SDK contracts for Console MCP QGIS and Studio.
- [honua-sdk-js#228](https://github.com/honua-io/honua-sdk-js/issues/228): Disconnected replica and sync conflict SDK contracts.
- [honua-sdk-js#229](https://github.com/honua-io/honua-sdk-js/issues/229): Operate observability alerts realtime rules and job viewer SDK contracts.
- [honua-sdk-dotnet#166](https://github.com/honua-io/honua-sdk-dotnet/issues/166): Console .NET client contracts for Blazor Web and MAUI hosts.
- [honua-server-admin#96](https://github.com/honua-io/honua-server-admin/issues/96): Prepare legacy Admin for Honua Console Operate transition.
- [honua-devops#55](https://github.com/honua-io/honua-devops/issues/55): Build one deployable artifact for Honua Console server and legacy Admin transition.
- [honua-devops#56](https://github.com/honua-io/honua-devops/issues/56): Add Honua Console CI release promotion and preview environment pipeline.
- [honua-server#360](https://github.com/honua-io/honua-server/issues/360): Geoprocess framework comparative research and Honua target model.
- [honua-server#361](https://github.com/honua-io/honua-server/issues/361): GeoETL spatial extract-transform-load pipelines.
- [honua-server#682](https://github.com/honua-io/honua-server/issues/682): GeoETL competitor evaluation and product strategy.
- [honua-server#721](https://github.com/honua-io/honua-server/issues/721): Geoprocessing canonical process contract and result package.
- [honua-server#724](https://github.com/honua-io/honua-server/issues/724): Geoprocessing orchestration layer for chaining, scheduling, and workflow DAGs.
- [honua-server#351](https://github.com/honua-io/honua-server/issues/351): GitOps manifest apply, dry run, prune, drift, and approval workflows.
- [honua-server#515](https://github.com/honua-io/honua-server/issues/515): GitOps drift detection and manifest rollback.
- [honua-server#992](https://github.com/honua-io/honua-server/issues/992): GitOps config promotion and production rollback gaps.
- [honua-server#1163](https://github.com/honua-io/honua-server/issues/1163): Metadata GitOps semantic release package and environment bindings.
- [honua-server#1164](https://github.com/honua-io/honua-server/issues/1164): Metadata GitOps compatibility prevalidation with data script coverage.
- [honua-server#1165](https://github.com/honua-io/honua-server/issues/1165): Metadata GitOps release operation lifecycle and rollback API.
- [honua-server#1166](https://github.com/honua-io/honua-server/issues/1166): Temporal data history API as-of query diff attribution and rollback.
- [honua-server#1167](https://github.com/honua-io/honua-server/issues/1167): Disconnected replica conflict review API and named replica metadata.
- [honua-server#1168](https://github.com/honua-io/honua-server/issues/1168): Console Operate observability event query API for telemetry logs alerts and investigations.
- [honua-server#1169](https://github.com/honua-io/honua-server/issues/1169): Realtime alert rule and geofence configuration APIs for Console Operate.
- [honua-server#1170](https://github.com/honua-io/honua-server/issues/1170): Job runner observability API for Console job viewer.
- [honua-server#1171](https://github.com/honua-io/honua-server/issues/1171): Native Console mTLS client authentication and multi-environment trust profiles.
- [honua-server#371](https://github.com/honua-io/honua-server/issues/371): Versioned editing named versions reconcile/post and multi-user concurrent editing.
- [honua-server#393](https://github.com/honua-io/honua-server/issues/393): Geofencing and spatial alerts event triggers on feature entry exit and threshold breach.
- [honua-server#339](https://github.com/honua-io/honua-server/issues/339): Real-time feature streaming WebSocket/SSE subscriptions with spatial filters.
- [honua-server#681](https://github.com/honua-io/honua-server/issues/681): Durable worker and job orchestration substrate for GeoETL and geoprocessing.
- [honua-server#512](https://github.com/honua-io/honua-server/issues/512): Admin control-plane API expansion for operational monitoring.
- [honua-server#978](https://github.com/honua-io/honua-server/issues/978): Admin observability realtime and OTLP status contract.
- [honua-server#504](https://github.com/honua-io/honua-server/issues/504): Audit event model and storage.
- [honua-server#507](https://github.com/honua-io/honua-server/issues/507): Audit coverage matrix and middleware instrumentation.
- [honua-server#350](https://github.com/honua-io/honua-server/issues/350): Audit logging immutable trail with SIEM export.
- [honua-server#509](https://github.com/honua-io/honua-server/issues/509): SIEM export and operator access.
- [honua-devops#13](https://github.com/honua-io/honua-devops/issues/13): Desired-state schemas.
- [honua-devops#14](https://github.com/honua-io/honua-devops/issues/14): `honua-gitops` engine.
- [honua-devops#17](https://github.com/honua-io/honua-devops/issues/17): Safe release orchestration.
- [honua-devops#18](https://github.com/honua-io/honua-devops/issues/18): ServiceBundle reconciliation.
- [honua-devops#57](https://github.com/honua-io/honua-devops/issues/57): Round-trip metadata GitOps PR workflow for Console and AI DevOps.
- [honua-devops#58](https://github.com/honua-io/honua-devops/issues/58): Console-facing AI DevOps GitOps release workflow API.
- [honua-devops#5](https://github.com/honua-io/honua-devops/issues/5): SLO enforcement dashboards alerting error-budget burn and release gates.
- [honua-server-admin#89](https://github.com/honua-io/honua-server-admin/issues/89): Redesign dashboard operations and observability around real server health.

## Order Of Operations

1. Finalize Console IA/RBAC and scaffold the Blazor Web Console shell plus shared Razor component library.
2. Land the server Metadata v2/content/RBAC baseline and .NET/JS SDK projections.
3. Port Catalog, Viewer, Share, and Open Data from Portal into Console.
4. Port Studio app-builder and generated-app lifecycle into Console.
5. Define the Studio information model for NL GIS artifacts and publishing.
6. Add Studio publishing for maps, dashboards, reports, and apps.
7. Add the unified GP/ETL editor that publishes workflow definitions to Honua batch/job-runner execution and eligible GP/process service endpoints.
8. Add GitOps metadata publishing round trip: semantic environment bindings, compatibility prevalidation, Git PR operation, CI/GitOps status, and rollback visualization.
9. Add Operate observability: per-server telemetry status, event viewer, logs, alerts, realtime/geofence rule configuration, jobs viewer, investigations, and AI DevOps evidence summaries.
10. Add optional temporal data viewer support for as-of reads, diffs, attribution, disconnected sync conflict review, and governed rollback when data sources expose temporal history.
11. Add optional MAUI Blazor Hybrid native Console host with multi-environment profiles, native gRPC streaming, and optional mTLS/client-certificate support.
12. Make legacy Admin available as a transitional Operate surface and hide duplicate builder routes.
13. Build the single deployable artifact and preview/release pipeline.
14. Pass the cross-surface smoke: publish service -> catalog item -> Studio generated artifact -> share/embed.
15. Freeze and retire old Portal deployment paths.

## Parity Gate

Do not retire `honua-portal` or separate Admin deployment paths until:

- Console can run the current catalog item -> viewer -> saved map -> share/embed path.
- Console can run the Studio prompt -> clarification -> spec/plan -> apply -> preview -> edit -> publish/reopen proof path.
- Console can persist and reopen Studio-generated query, analysis, map, dashboard, report, form, app, workflow, GP, and ETL packages through server-owned content/version/package contracts.
- Console can publish maps, dashboards, reports, apps, batch workflows, and eligible GP/process services from Studio using shared server/SDK contracts.
- Console serves open-data, STAC, and DCAT publication through the unified Console deploy surface.
- The unified GP/ETL editor can dry-run and publish definitions to the existing Honua job runner with execution history, logs, artifacts, and provenance.
- Console can propose, prevalidate, commit, monitor, promote, and roll back metadata/service/layer releases through GitOps using semantic resource identities across environments.
- Console can inspect optional temporal datasets through as-of views, diffs, actor attribution, feature timelines, and governed rollback operations where the backend declares support.
- Console can inspect per-server telemetry, logs, alerts, audit/security events, jobs, releases, temporal operations, and sync conflicts through a unified Operate event viewer.
- Native Console can manage multiple environments and optionally use mTLS/client certificates where the server requires stronger operator trust.
- The single deployable artifact serves `/studio`, `/catalog`, `/share`, and `/operate` from one origin.
- Server-authored RBAC/entitlement checks gate route and item actions.
- Metadata v2/content item/provenance data is shared across operator and builder workflows.
- Cross-surface smoke evidence is captured in CI or release promotion, covering: publish service -> catalog item -> Studio artifact -> share/embed, **plus** open-data publication and unauthenticated embed rendering. (Scope clarification: the current portal exercises both; `honua-console#9` must too, or regressions in those paths only surface after portal retirement.)
- Every `EMBED` row in the [Legacy Admin Route Disposition](../migration/legacy-admin-route-disposition.md) has been replaced or formally retired per its retirement gate.

## Portal Freeze And Retirement

Two-gate policy lives in [`PORTAL_FREEZE_POLICY.md`](../migration/PORTAL_FREEZE_POLICY.md). Summary:

- **Soft freeze** (bug-fix-only on `honua-portal`) starts when the first [`honua-console#4`](https://github.com/honua-io/honua-console/issues/4) implementation PR opens after owner acknowledgement. Bug fixes during soft freeze are paired with a Console follow-up against the matching child ticket.
- **Hard freeze** (no commits on `honua-portal`) starts when [`honua-console#9`](https://github.com/honua-io/honua-console/issues/9) enters review.
- **Retirement** of `honua-portal` is owned by [`honua-console#10`](https://github.com/honua-io/honua-console/issues/10) and gated on `#4`, `#5`, `#7`, `#8`, `#9`, and the parity gate above.

Update the freeze-state line at the top of this file when a gate flips.

## Open Coordination Decisions (Assigned)

Surfaced during the epic design pass; decisions are folded into the listed owner tickets so no parallel decision happens at port time:

- **Design system / token layer.** Decision deferred to [`honua-console#2`](https://github.com/honua-io/honua-console/issues/2): build a fresh Razor token/component layer in `Honua.Console.Components` (matches the .NET-first direction) vs. adopt an existing Razor component library to accelerate scaffolding. Visual reference is `honua-portal/src/ui`, but that codebase is not the long-term token source. If `#2` chooses "new token layer," file `honua-console#11` ahead of `#4`/`#5`.
- **Auth bridging for transitional legacy Admin.** Folded into [`honua-console#6`](https://github.com/honua-io/honua-console/issues/6). Strong preference for server-owned session (no Console-side bridge), conditional on [`honua-server#1162`](https://github.com/honua-io/honua-server/issues/1162) sizing it. Fallback is Console-issued token, which requires [`honua-server-admin#96`](https://github.com/honua-io/honua-server-admin/issues/96) to accept it.
- **`/operate` route ownership during transition.** Decided in [`honua-console#6`](https://github.com/honua-io/honua-console/issues/6): legacy Admin URLs redirect into `/operate/legacy/*`. Whether the implementation is edge reverse-proxy or in-Console host iframe is left to `#6`, but deep links from the legacy surface must continue to resolve under the Console origin.
- **SDK shim policy.** Resolved by [`SDK_SHIM_POLICY.md`](../migration/SDK_SHIM_POLICY.md). Per-language single-boundary shims: `.NET` shims live in `src/Honua.Console.Contracts/SdkShims.cs`; browser/embed shims live in `src/Honua.Console.Web/wwwroot/interop/sdk-shims.ts`. This document's "Active Shims" ledger is updated in the same PR. Cleanup is owned by [`honua-console#7`](https://github.com/honua-io/honua-console/issues/7).
- **Parity smoke scope.** Clarified above: [`honua-console#9`](https://github.com/honua-io/honua-console/issues/9) covers open-data publication and unauthenticated embed rendering in addition to the publish -> catalog -> Studio -> share/embed loop.
