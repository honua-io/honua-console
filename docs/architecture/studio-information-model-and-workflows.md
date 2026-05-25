# Honua Studio Information Model And Workflows

Status: draft

Owner surface: Honua Console Studio

Related backlog:

- [honua-console#1](https://github.com/honua-io/honua-console/issues/1): Console migration epic.
- [honua-console#5](https://github.com/honua-io/honua-console/issues/5): Studio app-builder lifecycle.
- [honua-console#7](https://github.com/honua-io/honua-console/issues/7): Shared metadata/content/RBAC contracts.
- [honua-console#16](https://github.com/honua-io/honua-console/issues/16): Studio publishing.
- [honua-console#17](https://github.com/honua-io/honua-console/issues/17): Unified GP/ETL editor.
- [honua-console#40](https://github.com/honua-io/honua-console/issues/40): Studio unified GP/ETL workflow editor UI.

## Purpose

Honua Studio needs one information model for natural-language GIS development across maps, dashboards, reports, forms, apps, spatial analysis, and GP/ETL workflows.

The goal is not to create separate builders with separate schemas. The goal is one authoring model where a user can ask for a map, dashboard, field form, report, app, query, analysis, ETL pipeline, or GP service, inspect the generated package, edit it, preview it, and publish it as a versioned Console content item.

The same package contracts must work from:

- Honua Studio in Console.
- Honua MCP tools used from Claude, GPT, Codex, or other clients.
- QGIS plugin workflows.
- Generated apps and embedded experiences.
- Operator flows in Console Operate.

## Authoritative Contract Sources

This document is a contract brief for follow-up implementation, not a separate Console schema. When fields or behavior conflict, implementation must resolve toward these sources:

- `honua-server` owns Metadata v2, workspace policy, content items, content versions, package persistence, package validation, publication records, job-runner execution, RBAC, audit, lineage, provenance, and service endpoints.
- `honua-sdk-js` owns browser-safe TypeScript projections, client request/response types, package helper APIs, generated-app runtime adapters, and MCP/QGIS-safe contract exports derived from server contracts.
- `honua-console` owns Studio authoring workflows, editor projections, preview orchestration, publish review UI, Catalog/Share/Operate navigation, and route-level use of server-authored RBAC decisions.
- `honua-portal` is a source for current Catalog, Share, Open Data, and Studio proof behavior until parity is accepted. It is not authoritative for new artifact models.
- `honua-server-admin` is a source for legacy operator workflow behavior during the Operate transition. It is not authoritative for builder package schemas.
- MCP clients, QGIS plugins, generated apps, and embeds consume server/SDK contracts. They must not define parallel artifact models.

## Design Principles

- Server owns truth: permissions, validation, content items, versions, publishing, job execution, audit, provenance, and service endpoints are server-owned.
- Studio owns authoring: natural-language interpretation, clarification, package editing, previews, and publication review live in Studio.
- Packages are portable: generated artifacts are typed package documents, not hidden UI state.
- Publishing is explicit: preview and saved draft are not the same as published content.
- Execution is queued: analysis, GP, ETL, scheduled work, and batch publication run through Honua's job runner rather than a browser-only runtime.
- Charts use standards: Vega-Lite remains the dashboard/report chart spec layer.
- Visual editors are projections: forms, maps, dashboards, and workflows can have UI editors, but the saved contract is the source of truth.

## Contract Ownership Matrix

| Object or surface | Owner | Contract rule |
| --- | --- | --- |
| Workspace | Server-owned | Defines tenant boundary, policy, feature flags, RBAC scope, retention, and job execution limits. Console and SDK read projections only. |
| Content item | Server-owned | Canonical Catalog record for discoverability, ACLs, lineage, sharing, embedding, invocation, and current version pointer. |
| Content version | Server-owned | Immutable package snapshot plus dependency, validation, rollback, and provenance evidence. |
| Studio project | Studio-owned authoring aggregate | Console owns the authoring experience and draft grouping; server persists identifiers, permissions, and links to produced content items. |
| Conversation / provenance | Split ownership | Studio owns prompts, clarifications, assumptions, and model/tool context while drafting. Server owns audit-grade provenance once attached to packages, content versions, publications, and job runs. |
| Package | Server contract with SDK projection | Server owns canonical schema, persistence, and validation. SDK owns browser-safe types and helpers. Studio editors modify package drafts through those contracts. |
| Data binding | Server contract with SDK projection | Server validates permissions, lineage, source versions, field mappings, CRS, refresh policy, and service capability. SDK exposes safe references for browser, MCP, QGIS, and generated apps. |
| Publication | Server-owned | Server creates routes, visibility state, embed policy, service policy, schedule policy, rollback policy, and invocation metadata. |
| Job run | Server-owned | Server/job runner owns execution state, queue, logs, artifacts, metrics, failures, audit, and provenance for analysis, GP, ETL, scheduled, and batch work. |
| SDK package projections | SDK-owned | Generated or hand-maintained TypeScript projections must track server contracts and be shared by Console, generated apps, MCP, and QGIS integrations. |
| UI editor state | UI-projection-only | Inspectors, canvases, layout handles, wizard steps, selection state, local preview state, and unsaved UI affordances are not persisted as canonical artifact data. |

## Core Object Model

### Workspace

Tenant or organization boundary for users, groups, roles, content, jobs, connectors, and publishing policy.

Key fields:

- `workspace_id`
- `name`
- `default_crs`
- `publishing_policy`
- `retention_policy`
- `feature_flags`

### Content Item

Catalog record for anything that can be discovered, versioned, permissioned, shared, embedded, or invoked.

Content item types should include:

- `dataset`
- `layer`
- `table`
- `view`
- `query`
- `analysis`
- `map`
- `dashboard`
- `report`
- `form`
- `app`
- `workflow`
- `gp_service`
- `etl_pipeline`
- `job_definition`
- `connector`
- `template`

Key fields:

- `item_id`
- `workspace_id`
- `type`
- `title`
- `summary`
- `owner`
- `created_at`
- `updated_at`
- `current_version_id`
- `acl`
- `tags`
- `lineage`
- `provenance`
- `publication_state`

### Content Version

Immutable version record for a content item's package, metadata, dependencies, and validation evidence.

Key fields:

- `version_id`
- `item_id`
- `version_number`
- `package_type`
- `package_schema_version`
- `package_ref`
- `dependency_refs`
- `validation_status`
- `created_by`
- `created_at`
- `change_note`
- `rollback_from_version_id`

### Studio Project

Authoring workspace that can contain multiple drafts and outputs before publication. A project may produce one or more content items.

Key fields:

- `project_id`
- `workspace_id`
- `title`
- `conversation_id`
- `draft_refs`
- `source_context_refs`
- `target_audience`
- `status`

### Conversation / Provenance

Prompt and clarification record for AI-assisted authoring. This is not the artifact; it is provenance and authoring context.

Key fields:

- `conversation_id`
- `provenance_id`
- `messages`
- `clarifications`
- `accepted_assumptions`
- `model_runs`
- `tool_calls`
- `source_context_refs`
- `generated_package_refs`
- `decision_log`
- `audit_refs`

### Package

Typed artifact document that can be validated, previewed, published, reopened, and edited.

Package families:

- `query.package`
- `analysis.package`
- `map.package`
- `dashboard.package`
- `report.package`
- `form.package`
- `app.package`
- `workflow.package`
- `publication.package`

Every package should carry:

- `schema_version`
- `package_type`
- `title`
- `summary`
- `data_bindings`
- `parameters`
- `dependencies`
- `publication_intent`
- `warnings`
- `provenance`

### Workflow Package Editor Projection

Implementation note for `honua-console#40`: Console exposes the first
Studio workflow editor at `/studio/workflows/new` and
`/studio/workflows/{draftId}`. The editor is a Blazor projection over a
replaceable `IStudioWorkflowPackageClient` adapter, seeded in memory until
the server and SDK workflow contracts land. The adapter models the
server-owned boundaries Console must call rather than inventing a durable
Console schema:

- `workflow.package` draft graph with source, transform, sink, success,
  and failure edges.
- Parameter, schedule, worker profile, retry/failure behavior, output
  schema, and publication intent edits.
- Dry-run job response with sample rows, logs, artifacts, and output
  schemas.
- Content item version save response for package persistence.
- Publication response for batch workflow, scheduled job, or eligible
  process endpoint with parameter validation.
- Operate job/event evidence links for dry-run and publish jobs.

The current adapter methods map to the expected server/SDK boundary as
follows:

| Adapter call | Contract surface | Required response notes |
| --- | --- | --- |
| `ListNodeDefinitionsAsync` | Node registry projection | Returns node `type`, `category`, `label`, `summary`, and declared input/output ports. Current categories are `source`, `transform`, and `sink`. |
| `CreateDraftAsync` / `GetDraftAsync` | `workflow.package/v1` draft | Returns draft identity, package/content metadata, graph nodes/edges, parameters, schedule, worker profile, retry policy, publication intent, output schemas, warnings, and validation issues. |
| `SaveVersionAsync` | Content-version save | Returns `contentItemId`, `versionId`, `versionNumber`, `packageType=workflow.package`, `contentItemType=workflow`, `contract=content-version/v1 + workflow.package/v1`, and validation issues. |
| `DryRunAsync` | `workflow-dry-run/v1` job | Returns `jobId`, `jobKind=workflow_dry_run`, `status`, `sampleRows`, logs, artifacts, output schemas, `/operate/jobs/{jobId}`, and `/operate/events?jobId={jobId}`. |
| `PublishAsync` | `workflow-publication/v1` | Selects the current saved version when the draft is unchanged and saves unsaved package edits as a new content version before publication. Queued responses return `publicationId`, content item/version ids, `jobId`, `jobKind=batch_publication`, `status`, publication `mode`, optional `invocationEndpoint`, validation issues, parameter validation, and Operate evidence links. |
| `GetJobEvidenceAsync` | Operate job/event projection | Returns job kind/status, draft/content/version ids, logs, artifacts, output schemas, evidence URLs, and creation time, or `null` for missing job evidence. |

Publication modes are `batch-workflow`, `scheduled-job`, and
`process-endpoint`. An invocation endpoint is requested when the mode is
`process-endpoint` or the draft's explicit endpoint flag is set; eligible
publications may expose
`/api/workspaces/{workspaceId}/workflows/{routeSlug}/invoke` when
parameter validation succeeds. Supported parameter types are `string`,
`date`, `number`, `boolean`, and `geometry`; invalid parameter contracts,
missing source/transform/sink graph coverage, missing failure routing,
missing scheduled cron expressions, and missing output schemas are
package validation errors that block publication and do not queue a job.
A blocked publication response still carries the saved content
item/version ids, publication mode, `status=blocked`, validation issues,
and parameter validation, but it omits the publication id, job id, job
kind, Operate evidence URLs, and invocation endpoint.

The focused smoke command is `npm run smoke:workflow`; it records
dry-run -> save version -> publish -> Operate monitor evidence under the
same owning-layer taxonomy as the Console parity smoke.

### Data Binding

Portable reference from an artifact to a dataset, layer, query, workflow output, job artifact, or service endpoint.

Key fields:

- `binding_id`
- `source_item_id`
- `source_version_id`
- `source_layer_id`
- `source_query`
- `field_mappings`
- `spatial_reference`
- `refresh_policy`
- `permission_requirements`

### Publication

Server-owned release record that turns a versioned package into something routable, shareable, embeddable, invokable, or scheduled.

Key fields:

- `publication_id`
- `item_id`
- `version_id`
- `publication_mode`
- `route`
- `visibility`
- `embed_policy`
- `service_policy`
- `schedule_policy`
- `runtime_target`
- `job_definition_id`
- `latest_job_run_id`
- `rollback_policy`
- `published_by`
- `published_at`

### Job Run

Execution record for analysis, ETL, GP, validation, publishing, export, or scheduled work.

Job kinds must include:

- `analysis_preview`
- `analysis_run`
- `gp_service_invocation`
- `etl_pipeline_run`
- `workflow_dry_run`
- `manual_job`
- `scheduled_job`
- `event_job`
- `batch_publication`
- `export`

Key fields:

- `job_id`
- `job_kind`
- `definition_item_id`
- `definition_version_id`
- `status`
- `trigger`
- `queue`
- `worker_profile`
- `started_at`
- `finished_at`
- `logs_ref`
- `artifacts`
- `metrics`
- `errors`
- `provenance`

## Package Families

### Console Implementation Slice

`honua-console#39` implements the first Console-native package editor slice for `query.package`, `analysis.package`, `map.package`, `dashboard.package`, `report.package`, `form.package`, and `app.package` at `/studio/query`, `/studio/analysis`, `/studio/map`, `/studio/dashboard`, `/studio/report`, `/studio/form`, and `/studio/app`.

The slice uses the temporary `studio-package-mock/v1` inspector and `StudioPackageLifecycleSimulator` lifecycle evidence until honua-server and honua-sdk-dotnet expose the content-version, publication, share, embed, and rollback APIs. Those mock refs are documented in [Studio Package Editor Routes](../studio/package-editor-routes.md) and do not change the server-owned package schema guidance below.

### Query Package

Represents natural-language to spatial query output.

Minimum shape:

- input layers or tables
- generated filters
- generated spatial predicates
- selected fields
- sort and limit rules
- output geometry mode
- validation notes
- preview query plan

Examples:

- "Show parcels within 500 feet of a hydrant."
- "Find permits issued in the last 90 days inside the flood zone."

### Analysis Package

Represents an executable spatial analysis plan.

Minimum shape:

- input bindings
- operations
- parameters
- output schema
- result package definition
- job runner requirements

Examples:

- buffer
- overlay
- dissolve
- join
- routing
- enrichment
- suitability scoring

### Map Package

Represents a saved map, not just a viewport.

Minimum shape:

- basemap reference
- layer stack
- layer styles
- filters
- popups
- labels
- bookmarks
- initial camera/extent
- interactions
- selected analysis or query outputs

### Dashboard Package

Represents a dashboard with linked map and chart state.

Minimum shape:

- data bindings
- Vega-Lite chart specs
- map panels
- filters
- selectors
- KPI cards
- cross-filter interactions
- refresh policy
- layout regions

### Report Package

Represents a reproducible analytical or operational report.

Minimum shape:

- narrative sections
- data bindings
- Vega-Lite chart specs
- map snapshots or live maps
- tables
- export settings
- refresh policy

### Form Package

Represents a field or business data collection form.

Minimum shape:

- target layer, table, workflow, or service
- fields
- geometry capture mode
- validation rules
- required conditions
- calculated values
- conditional visibility
- repeats or related records
- attachment policy
- offline policy
- submit action

The form designer should support Survey123-like workflows without becoming a separate product. A form is a Console content item that can be embedded in an app, attached to a map, or used as an input surface for a workflow or GP service.

### App Package

Represents an assembled user-facing application.

Minimum shape:

- routes
- pages
- panels
- map/dashboard/form/report components
- data bindings
- actions
- navigation
- permissions
- theme tokens
- publication settings

The app package composes other package families instead of copying their internals.

### Workflow Package

Represents GP/ETL workflow authoring.

Minimum shape:

- sources
- transforms
- sinks
- process steps
- parameters
- schedules
- event triggers
- worker profile
- dry-run policy
- artifact policy
- rollback policy
- publication mode

Workflow publication modes:

- `manual_job`: user-triggered queued execution.
- `scheduled_job`: cron or calendar-based batch execution.
- `event_job`: event-triggered execution.
- `gp_service`: parameterized service endpoint backed by the canonical geoprocessing runtime.
- `etl_pipeline`: reusable ETL definition backed by the job runner.

### Publication Package

Represents the publish request, review state, and release instruction for one or more versioned packages. It does not replace the server-owned publication record; it is the portable package that Studio, MCP, QGIS, and generated-app workflows can validate before the server creates or updates publication state.

Minimum shape:

- target content item and version refs
- publication mode
- route and slug intent
- visibility and embed policy
- service, schedule, or job policy
- dependency validation evidence
- warning acknowledgements
- release note
- rollback target
- execution plan for job-backed publication

## Studio Authoring Workflow

### 1. Start From Intent

The user can start from natural language, an existing content item, a dataset, a map, a dashboard, a form, or a workflow.

Studio classifies intent into one or more artifact targets:

- query
- analysis
- map
- dashboard
- report
- form
- app
- workflow
- service

### 2. Gather Context

Studio gathers allowed context through server/SDK APIs:

- available layers, tables, services, and views
- metadata summaries
- schemas and field stats
- geometry types and CRS
- permissions
- existing maps, forms, dashboards, and workflows
- service capabilities
- job runner capabilities

### 3. Clarify

Studio asks only for missing decisions that affect correctness, permissions, or publication:

- target dataset or service
- geography or extent
- output type
- schedule or manual run
- visibility
- form submission target
- chart measure/dimension ambiguity
- workflow sink or result item

### 4. Draft Package

Studio generates a package document plus a human-readable plan.

The draft should show:

- what will be created
- data used
- assumptions
- warnings
- required permissions
- execution or publication path

### 5. Validate

Server validates the package before preview or publish.

Validation should check:

- schema validity
- permissions
- missing fields
- unsupported operations
- CRS and geometry compatibility
- service limits
- job runner capability
- publication policy
- dependency availability

### 6. Preview

Preview is type-specific:

- query: result sample and query plan
- analysis: sample run or dry-run
- map: live map preview
- dashboard: live chart/map preview
- report: report preview
- form: fill/test submission preview
- app: route and interaction preview
- workflow: dry-run graph, sample artifacts, logs, rejected rows

### 7. Edit

The editor modifies the package, not hidden UI state.

Editor projections:

- map layer/style inspector
- dashboard chart/layout inspector
- form field/rules inspector
- app composition inspector
- workflow graph/table inspector
- JSON/package inspector for advanced users

### 8. Publish

Publish creates or updates a content item version and publication record.

Publish review should show:

- title and summary
- item type
- dependencies
- visibility
- embed policy
- service or job publication mode
- schedule
- warnings
- version note
- rollback target

### 9. Operate

After publication, Console links the artifact to operational state:

- usage
- jobs
- logs
- failures
- artifacts
- service health
- provenance
- audit trail
- permissions

## Publishing Behavior

Publishing always creates or updates a server-owned content item version before exposing routes, embeds, services, schedules, or job definitions. Studio can draft and review the publish request, but the server owns the final publication record and any execution.

| Artifact | Package family | Published item | Publication behavior | Execution path |
| --- | --- | --- | --- | --- |
| Query | `query.package` | `query` or `view` content item | Saves the query plan, dependency bindings, permission requirements, preview evidence, and optional routable query/view endpoint. | Server validates and executes previews. Large materializations, exports, or refreshes use job runs. |
| Analysis | `analysis.package` | `analysis` content item plus optional result item | Saves the analysis definition, parameters, result package definition, validation evidence, and lineage to produced results. | Preview, dry-run, run, and publication-time materialization route through Honua's job runner. |
| Map | `map.package` | `map` content item | Saves basemap, layers, styles, filters, popups, labels, bookmarks, interactions, extent, dependencies, share route, and embed policy. | Browser renders the map from server/SDK bindings; any dependent analysis or materialized refresh follows the related job run. |
| Dashboard | `dashboard.package` | `dashboard` content item | Saves linked map/chart state, Vega-Lite specs, filters, selectors, KPI cards, refresh policy, route, and embed policy. | Browser renders interactive views from SDK projections; scheduled refresh or materialized aggregates use job runs. |
| Report | `report.package` | `report` content item | Saves narrative sections, maps, Vega-Lite specs, tables, export settings, refresh policy, route, and embed/export policy. | Live reports render from bindings; scheduled generation, batch export, and heavy refresh use job runs. |
| Form | `form.package` | `form` content item | Saves fields, rules, geometry capture, attachments, offline policy, submit action, direct route, and embed policy. | Form submissions go to server endpoints. Submissions that trigger workflows, GP services, ETL, or batch actions create job runs. |
| App | `app.package` | `app` content item | Saves routes, pages, panels, component references, actions, navigation, permissions, theme tokens, stable routes, and embed policy. | Generated-app runtime consumes SDK projections. App actions that invoke analysis, GP, ETL, scheduled, or batch work create job runs. |
| Workflow | `workflow.package` | `workflow`, `gp_service`, `etl_pipeline`, or `job_definition` content item | Saves sources, transforms, sinks, parameters, triggers, schedules, worker profile, dry-run policy, artifact policy, rollback policy, and publication mode. | Manual jobs, scheduled jobs, event jobs, GP service invocations, ETL pipelines, dry-runs, and batch runs route through Honua's job runner. |
| Publication | `publication.package` | Publication record for a versioned content item | Captures release instructions, route, visibility, embed/service/schedule policy, rollback target, validation evidence, and acknowledged warnings. | Batch publication and any release step requiring analysis, GP, ETL, scheduled, or batch work creates job runs. |

The browser is never the authoritative execution runtime for analysis, GP, ETL, scheduled, or batch work. Console submits validation, preview, publish, and run requests to server/SDK APIs and follows the resulting `Job Run` state.

## Response Contract Expectations

Console should treat server/SDK responses as projections over the shared information model, not as local Console-only DTOs.

Validate, preview, publish, and run responses should consistently return:

- stable `workspace_id`, `item_id`, `version_id`, `publication_id`, `job_id`, and `provenance_id` refs when those objects exist
- package type and package schema version
- validation status, warnings, errors, missing permissions, unsupported service metadata, and unsupported package binding details
- dependency and data-binding refs instead of duplicated embedded source records
- preview refs or samples for synchronous preview paths
- `job_id` and job status refs for analysis, GP, ETL, scheduled, batch, export, heavy refresh, and asynchronous preview paths
- route, visibility, embed, service, schedule, rollback, and invocation policy refs for publish responses

## Current Console Package Shell Slice

The first Console implementation uses the shared Razor component library
for both the Blazor Web host and MAUI Blazor Hybrid host. The shell is
mounted at `/studio`, `/studio/proof`,
`/studio/drafts?source=<kind>&id=<itemId>`, and
`/studio/apps/:itemId/preview` so entry, legacy proof, source-scoped
draft, and generated-app preview paths resolve to one authoring surface.
Those route parameters are accepted for compatibility, but the current
slice does not yet hydrate a server-backed source package or create
content versions. The Console-owned `studio-authoring-shell/v1`
projection is intentionally a stable in-memory mock until the server
package lifecycle API and SDK helpers are connected.

The shell keeps the generated output visible as a package at all times:
workflow selection produces a typed package draft, ambiguous prompts add
structured clarification questions instead of applying hidden
assumptions, and the inspector exposes assumptions, data bindings,
warnings, validation, and provenance. Draft, Preview, Saved version, and
Published states are represented as distinct lifecycle descriptors so
the UI and smoke evidence can assert the state transition path without
creating server-owned content versions prematurely.

Current projection shape:

- `StudioAuthoringContract.Name = "studio-authoring-shell"`.
- `StudioAuthoringContract.Version = "v1"`.
- `StudioAuthoringContract.PackageSchemaVersion = "package-shell/v1"`.
- `StudioAuthoringSession` carries workflow options, the selected
  workflow id, the current prompt, open clarification questions, the
  active package snapshot, and recent projects.
- Workflow options currently cover `map.package`, `dashboard.package`,
  `report.package`, `form.package`, `app.package`, `query.package`,
  `analysis.package`, and `workflow.package` slices for workflow, GP
  service, and ETL authoring.
- `StudioPackageSnapshot` carries the contract name/version, package ref,
  package type, schema version, title, summary, lifecycle state,
  assumptions, data binding summaries, warnings, validation items, and
  provenance events.
- `StudioClarificationQuestion` and `StudioClarificationChoice` are the
  structured response surface for ambiguous prompts. Accepting one
  answer removes that pending assumption, updates bindings and
  provenance, and keeps any remaining clarification as a validation
  blocker.
- Preview, Save Version, and Publish controls stay blocked while any
  clarification remains open. The service transition method also returns
  the draft unchanged in that case, so tests cover the UI affordance and
  the response contract.
- Lifecycle transitions preserve the `PackageRef` and append provenance;
  they do not create server-owned content versions or publication
  records in this slice.

This projection is a Console-owned authoring shell response contract, not
the canonical server package schema. When the server lifecycle API and
`honua-sdk-dotnet` package projections land, Console should map this
shell state onto the SDK types rather than keeping parallel DTOs.

Console surfaces should use the same error and empty-state patterns for missing items, missing permissions, unsupported service metadata, and unsupported package bindings across Studio, Catalog, Share, and Operate.

## Required User Journeys

### Natural Language To Query Or Analysis

1. User asks a spatial question or requests an analytical output.
2. Studio identifies candidate layers, fields, CRS, predicates, operations, and result shape.
3. Studio drafts a query or analysis package and shows assumptions, permission requirements, and validation warnings.
4. Server validates the package. Query preview returns a sample/query plan; analysis preview creates a dry-run job when needed.
5. User publishes a saved query, view, analysis definition, or result item.
6. Analysis execution, materialization, scheduled refresh, and batch export are visible as job runs in Operate.

### Natural Language To Map

1. User asks for a map.
2. Studio finds candidate datasets and clarifies ambiguity.
3. Studio drafts a query or map package.
4. User previews and edits layer style, filters, popups, labels, and extent.
5. User publishes a saved map content item.
6. User can share, embed, reopen in Studio, or compose it into an app.

### Natural Language To Dashboard

1. User asks for a dashboard.
2. Studio identifies metrics, dimensions, geographies, and refresh policy.
3. Studio drafts dashboard package with Vega-Lite chart specs and map panels.
4. User previews linked filters and map/chart interactions.
5. User publishes a dashboard content item.

### Natural Language To Report

1. User asks for a report.
2. Studio identifies the narrative sections, maps, tables, metrics, charts, export format, and refresh policy.
3. Studio drafts a report package with data bindings, Vega-Lite chart specs, maps, tables, and export settings.
4. User previews the report and resolves missing permissions, unsupported bindings, or refresh warnings.
5. User publishes a report content item.
6. Scheduled report generation, batch export, and heavy refresh are visible as job runs in Operate.

### Natural Language To Form

1. User asks for a field or data collection form.
2. Studio determines the target layer, table, workflow, or service.
3. Studio drafts a form package with fields, validation, attachments, geometry capture, and submit action.
4. User tests the form against sample submissions.
5. User publishes the form as a content item.
6. The form can be embedded in an app, opened directly, or used as a workflow/service input.

### Natural Language To App

1. User asks for an app or experience.
2. Studio composes maps, dashboards, forms, reports, and actions into an app package.
3. User previews routes and interactions.
4. User publishes an app content item with stable routes and share/embed policy.

### Natural Language To GP/ETL Workflow

1. User asks for a workflow, ETL pipeline, scheduled job, or GP service.
2. Studio identifies source data, transforms, parameters, sink/result, worker needs, and schedule.
3. Studio drafts a workflow package.
4. Server validates the package against GeoETL/geoprocessing/job-runner contracts.
5. User dry-runs the workflow and reviews logs, artifacts, and rejected rows.
6. User publishes as a manual job, scheduled batch job, event job, ETL pipeline, or GP service.
7. Console Operate shows execution history, logs, artifacts, health, and audit trail.

## Boundary With Server Admin

Server Admin/Operate UI flows can own:

- connectors
- service configuration
- worker profiles
- queues
- schedules
- identity/RBAC
- deployment health
- audit and observability
- approval gates

Studio should own:

- authoring intent
- generated package review
- artifact editing
- preview
- publish review
- reopen/edit flows

The shared contract between them is the content item, package, publication, and job model. Admin should not have a separate metadata model for the same artifacts.

## Follow-Up Implementation Ticket Boundaries

Follow-up implementation should stay bounded by repository ownership so every surface adopts the same model instead of inventing a local variant:

- `honua-server`: define Metadata v2 fields, content version persistence, canonical package schemas, validation endpoints, publication records, job-runner integration, RBAC checks, provenance, audit, and rollback APIs.
- `honua-sdk-js`: expose server-derived package DTOs, validators/helpers, client methods for validate/preview/publish/run, generated-app runtime adapters, and MCP/QGIS-safe exports.
- `honua-console`: implement Studio project and conversation UX, package editors as projections, previews, publish review, Catalog/Share/Operate links, and route-level RBAC use without duplicating server DTOs.
- MCP/QGIS integrations: use SDK/server package contracts for draft, validate, preview, publish, and run flows; do not define separate map/dashboard/report/app/workflow artifacts.
- Generated apps and embeds: consume content item, content version, publication, package, data binding, and job-run projections through SDK APIs.

## MVP Backlog Slices

1. Define package schemas for query, analysis, map, dashboard, report, form, app, workflow, and publication.
2. Add Studio project/conversation/provenance records that link prompts to generated packages.
3. Add package validation endpoints in server/SDK for preview and publish.
4. Implement query, analysis, map, dashboard, and report publishing through content items.
5. Implement form package authoring with target layer/table/workflow binding.
6. Implement workflow package authoring and dry-run through the job runner.
7. Implement publish review and rollback for all package families.
8. Add cross-surface smoke tests from Studio publish to Catalog, Share, Operate, and MCP/QGIS access.
