# Workflow Catalog

Status: handoff draft.

This document lists the workflows designers should map into screens, states, and transitions.

## Global Workflow Pattern

Most Console workflows follow this shape:

```text
Select context -> Author/change -> Validate -> Preview -> Approve -> Execute/publish -> Monitor -> Review evidence -> Roll back/follow up
```

Common states:

- draft
- validating
- ready
- warning
- blocked
- approved
- running
- succeeded
- failed
- rolled back
- archived

## 1. AI Natural Language To Spatial Query

Entry points:

- Studio prompt.
- MCP client.
- QGIS plugin.
- Dataset/layer page "ask" action.

Flow:

1. User describes desired query.
2. System identifies source layers, fields, CRS, filters, joins, and spatial operations.
3. System asks clarifying questions if intent or data binding is ambiguous.
4. Studio produces a query package.
5. User inspects SQL/filter/spatial predicate summary.
6. User previews result on map/table.
7. User saves as content item or uses as input to map/dashboard/report/app/workflow.

Core UI states:

- no source selected
- ambiguous field/layer
- permission blocked
- expensive query warning
- preview ready
- result empty
- save/publish ready

Design outputs needed:

- prompt panel
- source/data binding picker
- clarification panel
- query package inspector
- map/table preview
- save as content item action

## 2. AI Natural Language To Analysis

Flow:

1. User asks for analysis outcome.
2. System identifies input layers, analysis method, parameters, output type, and compute profile.
3. Studio creates analysis package.
4. User reviews assumptions, parameters, cost/runtime estimate, output schema, and validation warnings.
5. User runs preview or submits job.
6. Job output becomes content item, layer, report, dashboard input, or app input.

Design outputs needed:

- analysis plan card
- parameter editor
- job submission review
- job progress and result artifact panel
- rerun with changed parameters

## 3. AI Map Builder

Flow:

1. User asks for a map or starts from selected content.
2. Studio creates map package with layers, styling, filters, popups, legend, basemap, interactions, and initial extent.
3. User previews and edits map.
4. User validates data bindings and permissions.
5. User saves version and publishes to Catalog/Share/App.

Design outputs needed:

- map preview
- layer list
- style editor
- filter and popup editor
- package warnings panel
- publish review

## 4. AI Dashboard / Report Builder

Flow:

1. User asks for dashboard/report.
2. Studio creates package with data bindings, layout slots, charts, map panels, tables, filters, and narrative.
3. Vega-Lite specs are generated for charts.
4. User edits chart specs through visual controls and can inspect raw Vega-Lite when needed.
5. User previews responsive states.
6. User publishes as dashboard/report content item and optionally shares/embed.

Design outputs needed:

- dashboard/report preview
- panel list
- data binding/field mapping inspector
- chart editor using Vega-Lite as the standard
- filter/linking configuration
- publish/share review

## 5. Form Builder / Survey-Style Field Workflow

Flow:

1. User asks for a form or starts from a layer/table.
2. Studio creates form package with fields, groups, validation, visibility rules, attachment rules, offline policy, and submit behavior.
3. User edits field order, labels, domains, required state, validation, conditional visibility, and mobile/offline behavior.
4. User previews desktop/mobile form.
5. User publishes to field workflow and/or app package.

Design outputs needed:

- form outline
- field/property inspector
- conditional logic editor
- validation preview
- offline/sync policy review
- publish review

## 6. AI App Builder

Flow:

1. User asks for an app experience or starts from map/dashboard/form/report.
2. Studio creates app package with pages, components, navigation, data bindings, actions, permissions, and share/embed policy.
3. User previews generated app.
4. User edits package via visual editor and inspectors.
5. User publishes app version.
6. User shares link/embed or reopens in Studio.

Design outputs needed:

- app page/navigation outline
- component inspector
- data/action binding inspector
- preview modes
- version/publish controls

## 7. Unified GP / ETL Editor

Flow:

1. User asks for a process, ETL pipeline, or GP service.
2. Studio creates workflow package with nodes, inputs, outputs, parameters, schedule, worker profile, and publication mode.
3. User edits graph and node parameters.
4. User dry-runs with sample data.
5. User reviews job plan, artifacts, logs, and output schema.
6. User publishes as batch workflow, scheduled job, or eligible GP/process service endpoint.

Design outputs needed:

- workflow graph
- node library
- node inspector
- dry-run panel
- output artifact/schema panel
- publish to job/GP service review

## 8. Package Publishing Review

Applies to maps, dashboards, reports, forms, apps, workflows, GP services, and ETL pipelines.

Flow:

1. User selects version to publish.
2. Console validates dependencies, permissions, schema, package contract, share policy, and route.
3. User reviews visibility, embed policy, service policy, schedule, rollback policy, and provenance.
4. User publishes.
5. Publication creates versioned content and operational events.
6. User monitors publication job and result.

Design outputs needed:

- publish checklist
- dependency and permission table
- route/share/embed controls
- rollback policy summary
- job/evidence panel

## 9. GitOps Metadata Publishing Across Environments

Flow:

1. User or AI DevOps creates metadata release proposal.
2. Console shows changed semantic resources.
3. User selects target environments.
4. Console compares current/dev/staging/prod bindings and drift.
5. Server runs compatibility preflight.
6. User attaches optional data scripts with before/after contracts.
7. Console blocks unresolved compatibility blockers.
8. Git PR operation is created.
9. CI/GitOps applies to lower environment, then promotes.
10. Console monitors release, smoke/SLO watch, and rollback readiness.

Design outputs needed:

- release proposal summary
- environment matrix
- semantic resource diff
- compatibility preflight table
- data script coverage table
- Git PR preview
- CI/GitOps timeline
- rollback summary

## 10. Temporal Data Viewer

Flow:

1. User opens temporal-capable layer/table.
2. Console shows capability state and retention policy.
3. User selects checkpoint or timestamp.
4. Console renders map/table as-of selected time.
5. User selects two checkpoints for diff.
6. Console shows added/removed/changed features.
7. User drills into feature timeline.
8. If policy allows, user requests rollback plan.
9. Rollback executes as governed job and creates a new checkpoint.

Design outputs needed:

- temporal capability state
- checkpoint selector/time scrubber
- as-of map/table
- diff summary and feature list
- feature timeline
- rollback review

## 11. Disconnected Sync Conflict Review

Flow:

1. Offline client synchronizes replica.
2. Server detects conflict.
3. Console lists conflicted replicas.
4. User opens conflict detail.
5. Console shows base/client/server comparison.
6. User chooses resolution: accept client, keep server, merge fields, choose geometry, reject, or defer.
7. Resolution creates audit event and committed change set.
8. User monitors sync/job outcome.

Design outputs needed:

- replica list
- conflict queue
- base/client/server comparison
- field-level and geometry conflict markers
- batch resolution panel
- audit/evidence panel

## 12. Operate Server Overview

Flow:

1. User selects environment/server.
2. Console shows health, version/build, telemetry/export status, current alerts, recent failed jobs/releases, and key metrics.
3. User drills into events, alerts, logs, jobs, releases, or investigations.

Design outputs needed:

- environment/server selector
- health and telemetry summary
- active alerts
- recent failures
- quick filters into event viewer

## 13. Event Viewer / Logs / Investigations

Flow:

1. User opens event viewer from server, job, alert, release, resource, or investigation.
2. User filters by time, type, severity, resource, actor, correlation id, trace id, job id, release id, replica id, or change set id.
3. User opens raw evidence or related object.
4. User pins events to investigation.
5. AI DevOps can summarize with evidence links.

Design outputs needed:

- dense event timeline/table
- filter builder
- event detail drawer
- raw evidence links
- investigation pinning and notes

## 14. Alerts And Realtime / Geofence Rules

Flow:

1. User opens Alerts or Realtime Rules.
2. User creates/edits rule: geofence, spatial filter, threshold, dwell, temporal, job, data quality, sync conflict, or release/SLO.
3. Console validates rule and delivery channels.
4. Rule starts evaluating.
5. Alerts fire, route to delivery channels, and appear in event viewer.
6. User acknowledges, suppresses, resolves, or links to investigation.

Design outputs needed:

- alert list/detail
- rule list/detail
- geofence zone editor/selector
- condition builder
- delivery channel status
- active incident and dead-letter view

## 15. Jobs Viewer

Flow:

1. User opens jobs from Operate, Studio, GitOps, temporal, alert, import, or publication flow.
2. Console lists jobs by status, type, queue, actor, resource, time, and environment.
3. User opens job detail.
4. Console shows stages, progress, logs, artifacts, metrics, failure classification, and related events.
5. User takes allowed action: retry, cancel, pause/resume, approve, promote, rerun.

Design outputs needed:

- job list
- status/queue/type filters
- stage/progress detail
- log/artifact panels
- action bar with policy-disabled states

## 16. Native Console Multi-Environment / mTLS

Flow:

1. User opens native Console.
2. User adds environment profile.
3. Console validates server URL and advertised capabilities.
4. User authenticates with account/RBAC.
5. If configured, user selects client certificate or OS certificate reference.
6. Console validates trust profile and server/client certificate state.
7. User switches between dev/staging/prod/customer environments.
8. Native Console uses full gRPC streaming where available.

Design outputs needed:

- environment profile list
- add/edit environment profile
- trust and certificate status
- connection diagnostics
- environment switcher
- transport capability indicators

