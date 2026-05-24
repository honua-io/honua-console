# Information Model Summary

Status: handoff draft.

This document condenses the main objects designers should understand. It is not a database schema. It names the concepts that need to appear in screen maps, object pages, tables, timelines, inspectors, and review flows.

## Object Map

```text
Workspace
  Environment*
    Server*
      Service*
        Layer/Table*
          TemporalSourceCapability?
          DisconnectedReplica*
          AlertRule*
      JobRun*
      OperationalEvent*
      Alert*
      Investigation*

ContentItem
  ContentVersion*
    Package
      DataBinding*
      ValidationEvidence*
  Publication*
  SharingPolicy?

StudioProject
  Conversation
  DraftPackage*
  PreviewState*
  PublishRequest?

MetadataReleasePackage
  ChangedResource*
  CompatibilityReport*
  DataScript*
  GitOperation
  ReleaseOperation
  RollbackPlan
```

`*` means many. `?` means optional capability.

## Cross-Cutting Concepts

### Workspace

The tenant or organization boundary for users, roles, content, jobs, environments, connectors, policies, and retention.

Design implications:

- Users should know which workspace they are in.
- Workspace policy affects publishing, sharing, retention, rollback, and alert actions.

### Environment

A runtime target such as dev, staging, production, customer-managed, or partner. Environments have server URLs, RBAC scope, GitOps targets, telemetry status, and optional native trust profiles.

Design implications:

- Environment selection must be prominent in Operate and publishing workflows.
- Cross-environment comparisons need clear labels and drift/compatibility states.
- Native Console may show certificate/trust state per environment.

### Semantic Resource

Stable logical identity for a service, layer, field, map, dashboard, app, form, workflow, GP service, or ETL pipeline across environments.

Design implications:

- Show semantic identity as the human-level object.
- Show physical IDs, URLs, table names, and secrets as environment bindings.

### Content Item

Catalog record for anything that can be discovered, versioned, permissioned, shared, embedded, invoked, or reused.

Common types:

- dataset, layer, table, view, query, analysis
- map, dashboard, report, form, app
- workflow, GP service, ETL pipeline, job definition
- connector, template

Design implications:

- Content item pages should expose current version, metadata, permissions, lineage, provenance, publication state, and related jobs/events.

### Content Version

Immutable version of a content item. Versions carry package references, dependencies, validation status, change notes, and rollback lineage.

Design implications:

- Users need version comparison, current version, draft version, and published version states.
- Version selection should appear in publishing, rollback, temporal, and GitOps flows.

### Package

Typed artifact document produced by Studio or a developer workflow. It is the saved contract behind visual editors.

Package families:

- query, analysis, map, dashboard, report, form, app, workflow, publication

Design implications:

- Visual editors are projections of package data.
- Users need a readable summary of package purpose, data bindings, parameters, warnings, dependencies, and validation results.

### Data Binding

Reference from a package to a dataset, layer, query, workflow output, job artifact, or service endpoint.

Design implications:

- Designers should show data sources and field mappings as first-class review items.
- Broken permissions or schema mismatch should be visible before publish.

### Publication

Server-owned release record that makes a content version routable, shareable, embeddable, invokable, scheduled, or public.

Design implications:

- Preview, draft, saved, and published are distinct.
- Publication review must show visibility, route, embed policy, service policy, schedule, and rollback policy.

### Job Run

Execution record for analysis, ETL, GP, validation, publishing, imports, exports, rollback, alert delivery, and maintenance.

Design implications:

- Jobs need list, detail, progress, stages, logs, artifacts, failure classification, and allowed actions.
- Jobs are linked from Studio, Operate, GitOps, temporal diff, rollback, alerts, and investigations.

### Operational Event

Normalized event row for a timeline across logs, alerts, audit, jobs, releases, sync, and data changes.

Design implications:

- Event viewer should filter by time, severity, type, environment, server, resource, actor, job, release, replica, change set, trace id, and request id.
- Events should deep-link to raw evidence.

### Alert

Actionable operational condition with severity, status, source, owner, evidence, runbook, affected resources, and AI summary.

Sources:

- SLO, health check, log rule, release gate, job, security, data quality, sync conflict.

Design implications:

- Alerts need acknowledge, suppress, resolve, assign/owner, evidence timeline, and investigation links.
- AI summaries must remain advisory and evidence-linked.

### Alert Rule

Configured realtime or scheduled rule that can create alerts.

Rule types:

- geofence, spatial filter, attribute threshold, dwell, temporal window, job status, data quality, sync conflict, release/SLO.

Design implications:

- Rule UI must show capability support, validation errors, enabled state, delivery channel health, cooldown, recent triggers, and dead-letter state.

### Temporal Source Capability

Declares whether a layer/table supports as-of viewing, history, diff, rollback, sync conflict review, replica tracking, and change tracking.

Design implications:

- Temporal UI starts with capability state: unsupported, as-of only, diff available, rollback available, conflict review available.
- Unsupported states should be clear, not treated as empty data.

### Temporal Checkpoint

Named or implicit point used for history browsing and diffing. Examples: timestamp, transaction, release, job, named checkpoint.

Design implications:

- Time scrubbers and checkpoint selectors should support both timestamp and named operational points.

### Temporal Diff

Comparison between two checkpoints showing added, removed, geometry-changed, attribute-changed, and unchanged context.

Design implications:

- Diff map/table should let users move from summary -> feature list -> field/geometry detail -> actor/source/reason.

### Disconnected Replica

Durable server-side record for an offline/disconnected dataset copy. Aligned with Esri Sync concepts.

Design implications:

- Replica list should show owner, device/client, base checkpoint, last sync, status, sync generation, and conflicts.

### Sync Conflict

Conflict between a disconnected edit and the current server state.

Design implications:

- Conflict review needs base/client/server comparison.
- Users need resolution choices: accept client, keep server, merge fields, choose geometry, reject client edit, defer.

### Metadata Release Package

Proposed GitOps metadata/service/layer/content change set across environments.

Design implications:

- Release review should show changed semantic resources, target environments, compatibility reports, data scripts, PR preview, CI/GitOps status, and rollback class.

### Compatibility Report

Prevalidation report for metadata, package, service, schema, data, and script compatibility.

Design implications:

- Findings need severity and disposition: ready, warning, blocked, covered by script, unknown.
- Blockers must prevent PR creation or deployment.

### Git Operation

Git PR or commit operation containing generated manifests, scripts, validation evidence, and rollback policy.

Design implications:

- Designers should show file tree, diff summary, PR body, evidence, and status without requiring raw Git fluency.

### Native Environment Profile

Saved connection profile for web/native Console, especially native MAUI host.

Key fields:

- environment id, display name, server URL, tenant/workspace, auth mode, transport capabilities, trust state, optional certificate reference.

Design implications:

- Native Console needs environment switcher, trust status, certificate warnings, and connection diagnostics.

