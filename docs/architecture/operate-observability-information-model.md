# Operate Observability Information Model

Status: implementation checkpoint for `honua-console#41`; backend API
contracts remain draft pending server and SDK projections.

## Purpose

Honua Console needs a unified Operate observability surface for each connected server or environment. This is the place an operator can see current health, telemetry status, alerts, logs, audit events, releases, jobs, sync conflicts, and data-change operations without jumping between Portal, Admin, Grafana, CI, cloud consoles, and raw logs.

The surface should not replace Prometheus, Grafana, OpenTelemetry, SIEM, or cloud-native logging. It should provide the product-level operational view, deep links, and normalized query contract that Console and AI DevOps can use.

## Existing Foundation

- [honua-server#512](https://github.com/honua-io/honua-server/issues/512): admin control-plane API expansion for operational monitoring.
- [honua-server#978](https://github.com/honua-io/honua-server/issues/978): admin observability realtime and OTLP status contract.
- [honua-server#504](https://github.com/honua-io/honua-server/issues/504): audit event model and storage.
- [honua-server#507](https://github.com/honua-io/honua-server/issues/507): audit coverage matrix and middleware instrumentation.
- [honua-server#350](https://github.com/honua-io/honua-server/issues/350): immutable audit logging with SIEM export.
- [honua-server#509](https://github.com/honua-io/honua-server/issues/509): SIEM export and operator access.
- [honua-devops#5](https://github.com/honua-io/honua-devops/issues/5): SLO enforcement, alerting, error-budget burn, and release gates.
- [honua-devops#4](https://github.com/honua-io/honua-devops/issues/4): AI DevOps intelligent operations foundation.
- [honua-server-admin#89](https://github.com/honua-io/honua-server-admin/issues/89): legacy Admin dashboard and observability redesign around real server health.

## Product Goals

- One event viewer for a server, environment, service, layer, release, job, replica, user, or request id.
- Per-server telemetry state: reachable, auth state, version, build, telemetry enabled, OTLP/log export health, metrics freshness, tracing state.
- Alerts surfaced as actionable operational records with severity, status, owner, source, runbook, and affected resources.
- Realtime alert rules and geofence/spatial alert configuration surfaced as governed Operate workflows.
- Jobs visible across Studio, GP/ETL, imports, GitOps releases, temporal diffs/rollbacks, alert delivery, and background maintenance.
- Logs searchable with time, severity, service, environment, correlation id, trace id, request id, job id, release id, layer id, and user id filters.
- Audit events visible beside operational telemetry, but clearly labeled as audit/security records.
- AI DevOps can summarize, correlate, and propose guided remediation without becoming the source of truth.
- Release, GitOps, job runner, temporal data, and disconnected sync workflows can deep-link into the same event timeline.

## Non Goals

- Do not build a full log aggregation backend inside Console.
- Do not make Console the authoritative metrics store.
- Do not bypass tenant/environment RBAC or SIEM retention policy.
- Do not hide raw evidence links when an operator needs to open Grafana, traces, logs, CI, cloud provider dashboards, or SIEM.

## Core Objects

### Observability Target

Anything Console can monitor or investigate.

Key fields:

- `target_id`
- `target_type`: `environment`, `server`, `service`, `layer`, `job`, `release`, `replica`, `user`, `tenant`, `connector`
- `display_name`
- `environment_id`
- `server_id`
- `resource_refs`
- `owner_team`
- `labels`
- `health_status`: `healthy`, `degraded`, `unhealthy`, `unknown`, `maintenance`
- `last_seen_at`

### Server Telemetry Status

Current telemetry configuration and freshness for one server.

Key fields:

- `server_id`
- `environment_id`
- `server_version`
- `build_sha`
- `reachable`
- `auth_status`
- `clock_skew_ms`
- `metrics_enabled`
- `metrics_last_seen_at`
- `tracing_enabled`
- `traces_last_seen_at`
- `logs_enabled`
- `logs_last_seen_at`
- `otlp_endpoint_configured`
- `otlp_exporter_status`: `disabled`, `healthy`, `degraded`, `failing`, `unknown`
- `last_export_error`
- `admin_realtime_supported`
- `admin_realtime_status`

### Operational Event

Normalized event row for the event viewer.

Key fields:

- `event_id`
- `event_time`
- `received_time`
- `environment_id`
- `server_id`
- `severity`: `trace`, `debug`, `info`, `notice`, `warning`, `error`, `critical`
- `event_type`: `health`, `log`, `audit`, `alert`, `metric`, `trace`, `job`, `release`, `sync`, `data_change`, `security`
- `category`
- `message`
- `resource_refs`
- `actor_ref`
- `correlation_id`
- `trace_id`
- `span_id`
- `request_id`
- `job_run_id`
- `release_operation_id`
- `git_ref`
- `replica_id`
- `change_set_id`
- `alert_id`
- `raw_source`
- `raw_ref`
- `retention_expires_at`

### Alert

Actionable condition requiring attention or explicit acknowledgement.

Key fields:

- `alert_id`
- `environment_id`
- `server_id`
- `severity`: `info`, `warning`, `critical`
- `status`: `firing`, `acknowledged`, `suppressed`, `resolved`
- `source`: `slo`, `health_check`, `log_rule`, `release_gate`, `job`, `security`, `data_quality`, `sync_conflict`
- `title`
- `description`
- `started_at`
- `last_seen_at`
- `resolved_at`
- `affected_resources`
- `owner_team`
- `runbook_url`
- `evidence_refs`
- `suggested_actions`
- `ai_summary_ref`

### Alert Rule

Configured realtime or scheduled rule that can produce alerts.

Key fields:

- `rule_id`
- `name`
- `rule_type`: `geofence`, `spatial_filter`, `attribute_threshold`, `dwell`, `temporal_window`, `job_status`, `data_quality`, `sync_conflict`, `release_slo`
- `enabled`
- `environment_id`
- `server_id`
- `source_resource_refs`
- `condition`
- `severity`
- `cooldown`
- `delivery_channel_refs`
- `last_evaluated_at`
- `last_triggered_at`
- `active_incident_count`
- `delivery_failure_count`
- `status`: `healthy`, `disabled`, `invalid`, `degraded`, `failing`

### Geofence Zone

Spatial zone used by geofence and spatial alert rules.

Key fields:

- `zone_id`
- `name`
- `geometry`
- `source_layer_ref`
- `environment_id`
- `version`
- `labels`
- `audit_event_ids`

### Job Run

Operational record for background work and long-running tasks.

Key fields:

- `job_run_id`
- `job_definition_id`
- `job_type`: `gp`, `etl`, `import`, `export`, `publish`, `gitops_release`, `temporal_diff`, `temporal_rollback`, `alert_delivery`, `maintenance`
- `queue`
- `status`: `queued`, `running`, `waiting`, `blocked`, `succeeded`, `failed`, `canceled`, `retrying`
- `submitted_by`
- `submitted_at`
- `started_at`
- `completed_at`
- `environment_id`
- `server_id`
- `resource_refs`
- `correlation_id`
- `trace_id`
- `progress`
- `failure_classification`
- `allowed_actions`

### Log Record

Normalized log projection for search and correlation.

Key fields:

- `log_id`
- `timestamp`
- `level`
- `logger`
- `message`
- `environment_id`
- `server_id`
- `service_id`
- `trace_id`
- `span_id`
- `request_id`
- `user_id`
- `exception_type`
- `exception_digest`
- `properties`
- `raw_ref`

### Metric Snapshot

Point-in-time summary for health cards and alert context.

Key fields:

- `metric_id`
- `environment_id`
- `server_id`
- `resource_ref`
- `name`
- `unit`
- `value`
- `window`
- `status`
- `sampled_at`
- `source_ref`

### Investigation

Saved operator workflow around an incident, failed release, job failure, performance issue, sync conflict, or data change.

Key fields:

- `investigation_id`
- `title`
- `status`: `open`, `mitigating`, `resolved`, `closed`
- `created_by`
- `created_at`
- `environment_id`
- `primary_target`
- `time_range`
- `pinned_events`
- `linked_alerts`
- `linked_jobs`
- `linked_releases`
- `linked_change_sets`
- `ai_findings`
- `remediation_refs`

## Console Views

### UI Implementation Checkpoint

`honua-console#41` adds the first native Blazor Operate observability
surface at `/operate`, `/operate/observability`, `/operate/events/{eventId}`,
`/operate/alerts/{alertId}`, and `/operate/jobs/{jobRunId}`. Until the
server and SDK projections land, the route uses a single UI projection
fixture in `OperateObservabilityFixture` rather than re-declaring server
protocol DTOs across components.

The checkpoint proves these product behaviors:

- unknown, unsupported, missing, disabled, and not configured telemetry
  render as neutral states and do not fail the environment card
- event and alert AI advisory copy is always shown beside raw evidence
  links, not as a replacement for them
- invalid realtime/geofence rules expose validation messages and render
  their enable action disabled
- Studio, publishing, GitOps, temporal, alert delivery, import, and
  maintenance jobs all link to the same `/operate/jobs/{jobRunId}` detail
  surface

Current UI projection response contract:

- `OperateObservabilitySnapshot` is the route-level projection. It
  contains `Environments`, `TelemetryFacts`, `CompatibilityRows`,
  `Events`, `Alerts`, `Rules`, `Jobs`, and `Investigations`.
- `OperateStatus` normalizes state strings by trimming, lowercasing, and
  replacing underscores with spaces. Neutral states are `unknown`,
  `unsupported`, `missing`, `disabled`, `not configured`, and
  `unconfigured`; `missing` displays as `unknown`, and `unconfigured`
  displays as `not configured`.
- Failure styling is reserved for `critical`, `error`, `failed`,
  `failing`, `firing`, `invalid`, `unhealthy`, and `blocked`. A neutral
  telemetry fact is not a failed environment, even when the server
  omitted, disabled, or cannot support that signal.
- `OperateObservabilityRoutes.EventDetail`, `AlertDetail`, and
  `JobDetail` emit `/operate/events/{eventId}`,
  `/operate/alerts/{alertId}`, and `/operate/jobs/{jobRunId}` with the
  route id escaped as a path segment.
- The checkpoint uses fixture selection helpers. If a detail route id is
  absent from the fixture, the UI selects the first event, alert, or job
  row so the page remains renderable until the backend read contract is
  available.
- `OperateAiAdvisory` is advisory only: event and alert details must keep
  raw evidence links visible beside the advisory summary and suggested
  actions.
- `OperateAlertRule.CanEnable` is true only when the rule is disabled and
  valid. A rule is invalid when its normalized status is `invalid` or it
  has validation messages.
- `OperateJobRun.DetailHref` is the single job detail link for Studio,
  publishing, GitOps, temporal, alert delivery, import, and maintenance
  work.

### Server Overview

Purpose: show whether a connected server is healthy and observable.

Content:

- server identity, version, build, environment, and last seen
- health summary
- telemetry/export status
- current alerts
- recent failed jobs/releases
- high-level metrics
- links to logs, traces, events, and audit

### Event Viewer

Purpose: searchable timeline across logs, alerts, audit, jobs, releases, sync, and data changes.

Required filters:

- time range
- server/environment
- severity
- event type
- resource
- actor/user
- correlation id
- trace id
- request id
- job id
- release id
- replica id
- change set id

Required interactions:

- pin event to investigation
- open raw evidence
- open correlated trace/log/job/release/audit record
- ask AI DevOps for explanation
- create or link support/remediation task

### Alerts

Purpose: make active and historical alerts actionable.

Content:

- firing/resolved/suppressed status
- severity and owner
- affected resources
- runbook link
- evidence timeline
- acknowledge/suppress/resolve actions when allowed
- AI DevOps summary and proposed next actions

### Realtime Rules And Geofences

Purpose: configure and monitor realtime alerting.

Content:

- alert rule list and status
- geofence zone metadata
- rule condition summary
- delivery channel bindings
- test validation result
- active incidents
- recent triggers and delivery failures
- links to events and investigations

### Jobs

Purpose: inspect and operate long-running and background work.

Content:

- job list by status, queue, type, actor, resource, environment, and time
- stage/progress detail
- logs and event timeline
- artifacts and reports
- failure classification
- allowed actions such as retry, cancel, pause/resume, approve, promote, or rerun
- links to Studio GP/ETL definitions, GitOps releases, temporal operations, imports, and alert delivery records

### Logs

Purpose: structured log search without turning Console into a raw log tool.

Content:

- log search table
- severity histogram
- exception grouping
- correlation/trace/request filters
- raw log provider link

### Investigations

Purpose: capture operational context across teams.

Content:

- pinned events
- linked alerts, jobs, releases, PRs, temporal diffs, sync conflicts
- notes and status
- remediation and approval links
- final evidence package

## Backend API Shape

Console needs stable product-level APIs even if backing providers differ by deployment:

The `honua-console#41` checkpoint does not call these endpoints yet. It
keeps the route fixture as a Console UI projection until server-owned
contracts and SDK projections land, so Console does not duplicate
backend protocol DTOs in components.

- `GET /operate/targets`
- `GET /operate/servers/{serverId}/telemetry-status`
- `POST /operate/events/query`
- `POST /operate/logs/query`
- `POST /operate/alerts/query`
- `POST /operate/alerts/{alertId}/acknowledge`
- `POST /operate/alerts/{alertId}/suppress`
- `POST /operate/alerts/{alertId}/resolve`
- `GET /operate/alert-rules`
- `POST /operate/alert-rules`
- `GET /operate/alert-rules/{ruleId}`
- `PUT /operate/alert-rules/{ruleId}`
- `POST /operate/alert-rules/{ruleId}/test`
- `GET /operate/geofence-zones`
- `POST /operate/geofence-zones`
- `GET /operate/jobs`
- `GET /operate/jobs/{jobRunId}`
- `GET /operate/jobs/{jobRunId}/logs`
- `GET /operate/jobs/{jobRunId}/artifacts`
- `POST /operate/jobs/{jobRunId}/actions`
- `POST /operate/investigations`
- `GET /operate/investigations/{investigationId}`
- `POST /operate/investigations/{investigationId}/pins`

Backend responses should include deep links to raw providers where available.

## Relationship To GitOps Publishing

GitOps release operations should emit correlated events for:

- proposal created
- compatibility preflight started/completed
- PR created/updated/merged
- CI checks started/completed
- deploy started/completed
- smoke/SLO watch status
- rollback planned/executed

The event viewer should allow filtering by `release_operation_id` and `git_ref`.

## Relationship To Temporal Data And Sync

Temporal data history and disconnected sync should emit correlated events for:

- checkpoint created
- diff job started/completed
- rollback plan created
- rollback job started/completed
- replica created/synchronized/unregistered
- sync conflict detected/resolved
- change set committed
- geofence/spatial alert triggered from a data change
- temporal rollback job status

The event viewer should allow filtering by `change_set_id`, `replica_id`, and `rollback_operation_id`.

## Relationship To AI DevOps

AI DevOps can:

- summarize a time range
- cluster related errors
- explain likely root cause with evidence links
- propose a runbook
- draft a remediation plan
- prepare a support ticket
- explain alert trigger history and job failure evidence

AI DevOps cannot:

- mark alerts resolved without explicit operator action
- suppress alerts without permission
- rewrite event history
- hide raw evidence

## Security And Governance

- Event, log, alert, audit, and investigation reads are independently permissioned.
- Sensitive log properties must be redacted by the backend before reaching Console.
- Audit events must remain immutable.
- Alert action permissions must be recorded as audit events.
- Console should show retention windows for logs/events/audit where available.
- Cross-environment queries must respect environment binding and tenant isolation.

## Backend Backlog

1. Define normalized Operate event, alert, log, telemetry status, metric snapshot, and investigation contracts.
2. Expose per-server telemetry status using existing admin observability and OTLP status foundation.
3. Add event/log/alert query APIs with correlation filters and provider deep links.
4. Add alert acknowledge/suppress/resolve APIs with audit records.
5. Add realtime alert rule and geofence configuration APIs with validation, delivery-channel binding, and rule health.
6. Add job runner query/detail/log/artifact/action APIs for the Console jobs viewer.
7. Emit correlated events from GitOps release operations, job runner operations, temporal history operations, disconnected sync, realtime alerts, and data-change workflows.
8. Add provider adapters for local/dev, cloud-managed, and external OTLP/log/alert backends.
9. Add SDK fixtures for server overview, event viewer, logs, alerts, realtime rules, jobs, and investigations.
10. Surface AI DevOps summaries as advisory records linked to immutable evidence.

## Open Questions

- Which event storage/query path is authoritative for self-hosted Community deployments?
- Which alerts are Console-native versus external provider projections?
- Should investigations live in `honua-server` metadata or `honua-devops` support state?
- Which log fields are always redacted, and which are permission-sensitive?
- What minimum observability works when OTLP is disabled?
