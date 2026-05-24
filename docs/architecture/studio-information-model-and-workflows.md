# Honua Studio Information Model And Workflows

Status: draft

Owner surface: Honua Console Studio

Related backlog:

- [honua-console#1](https://github.com/honua-io/honua-console/issues/1): Console migration epic.
- [honua-console#5](https://github.com/honua-io/honua-console/issues/5): Studio app-builder lifecycle.
- [honua-console#7](https://github.com/honua-io/honua-console/issues/7): Shared metadata/content/RBAC contracts.
- [honua-console#16](https://github.com/honua-io/honua-console/issues/16): Studio publishing.
- [honua-console#17](https://github.com/honua-io/honua-console/issues/17): Unified GP/ETL editor.

Related implementation notes:

- [Studio Publishing Contract And Usage](studio-publishing-contract-and-usage.md)

## Purpose

Honua Studio needs one information model for natural-language GIS development across maps, dashboards, reports, forms, apps, spatial analysis, and GP/ETL workflows.

The goal is not to create separate builders with separate schemas. The goal is one authoring model where a user can ask for a map, dashboard, field form, report, app, query, analysis, ETL pipeline, or GP service, inspect the generated package, edit it, preview it, and publish it as a versioned Console content item.

The same package contracts must work from:

- Honua Studio in Console.
- Honua MCP tools used from Claude, GPT, Codex, or other clients.
- QGIS plugin workflows.
- Generated apps and embedded experiences.
- Operator flows in Console Operate.

## Design Principles

- Server owns truth: permissions, validation, content items, versions, publishing, job execution, audit, provenance, and service endpoints are server-owned.
- Studio owns authoring: natural-language interpretation, clarification, package editing, previews, and publication review live in Studio.
- Packages are portable: generated artifacts are typed package documents, not hidden UI state.
- Publishing is explicit: preview and saved draft are not the same as published content.
- Execution is queued: analysis, GP, ETL, scheduled work, and batch publication run through Honua's job runner rather than a browser-only runtime.
- Charts use standards: Vega-Lite remains the dashboard/report chart spec layer.
- Visual editors are projections: forms, maps, dashboards, and workflows can have UI editors, but the saved contract is the source of truth.

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

### Conversation

Prompt and clarification record for AI-assisted authoring. This is not the artifact; it is provenance and authoring context.

Key fields:

- `conversation_id`
- `messages`
- `clarifications`
- `accepted_assumptions`
- `model_runs`
- `tool_calls`
- `source_context_refs`
- `generated_package_refs`

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
- `warnings`
- `provenance`

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
- `route`
- `visibility`
- `embed_policy`
- `service_policy`
- `schedule_policy`
- `rollback_policy`
- `published_by`
- `published_at`

### Job Run

Execution record for analysis, ETL, GP, validation, publishing, export, or scheduled work.

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

Console ticket [honua-console#16](https://github.com/honua-io/honua-console/issues/16) implements the first Console-scoped publish path with a fixture `StudioPublishingClient` boundary. The fixture path is intentionally limited to the Console worktree: maps use the SDK `HonuaMapPackage` type, generated apps use the SDK `AppPackage` projection, and dashboard/report package shapes remain fixture projections until the shared SDK/server contracts land. The current route and response contract is documented in [Studio Publishing Contract And Usage](studio-publishing-contract-and-usage.md). The UI and smoke coverage exercise Studio draft or preview -> publish review -> versioned Catalog item -> preview -> Share/embed -> reopen in Studio without a generation call. Production persistence, RBAC enforcement, dependency closure, rollback semantics, and dashboard/report wire DTOs remain server/SDK-owned follow-ons rather than Console-local protocol copies.

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

## Required User Journeys

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

## MVP Backlog Slices

1. Define package schemas for query, map, dashboard, form, app, publication, and workflow.
2. Add Studio project/conversation/provenance records that link prompts to generated packages.
3. Add package validation endpoints in server/SDK for preview and publish.
4. Implement map publishing and dashboard/report publishing through content items.
5. Implement form package authoring with target layer/table/workflow binding.
6. Implement workflow package authoring and dry-run through the job runner.
7. Implement publish review and rollback for all package families.
8. Add cross-surface smoke tests from Studio publish to Catalog, Share, Operate, and MCP/QGIS access.
