# Operate Observability Information Model

Status: server-backed Console runtime binding for `honua-console#24`;
the honua-server admin API read paths are consumed through a temporary
Console contracts shim while the honua-sdk-dotnet Operate projection is
pending.

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
- [honua-server#1168](https://github.com/honua-io/honua-server/issues/1168): Console Operate observability event query API for telemetry logs alerts and investigations.
- [honua-server#1169](https://github.com/honua-io/honua-server/issues/1169): Realtime alert rule and geofence configuration APIs for Console Operate.
- [honua-server#1170](https://github.com/honua-io/honua-server/issues/1170): Job runner observability API for Console job viewer.
- [honua-sdk-dotnet#231](https://github.com/honua-io/honua-sdk-dotnet/issues/231): .NET SDK projection target for Console Operate observability contracts.

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

`honua-console#41` added the first native Blazor Operate observability
surface at `/operate`, `/operate/observability`, `/operate/events/{eventId}`,
`/operate/alerts/{alertId}`, and `/operate/jobs/{jobRunId}`.
`honua-console#24` replaces that runtime checkpoint with
`IConsoleOperateObservabilityClient`, a thin `HttpClient` boundary over the
honua-server admin APIs until honua-sdk-dotnet projects the Operate contracts.
`OperateObservabilityFixture` remains scaffolding/test data only.

The current runtime preserves these product behaviors:

- unknown, unsupported, missing, disabled, and not configured telemetry
  render as neutral states and do not fail the environment card
- event and alert AI advisory copy is always shown beside raw evidence
  links, not as a replacement for them
- invalid realtime/geofence rules expose validation messages and render
  their enable action disabled
- logs render from `/api/v1/admin/observability/logs` with severity and
  exception grouping derived from structured server log entries
- Studio, publishing, GitOps, temporal, alert delivery, import, and
  maintenance jobs all link to the same `/operate/jobs/{jobRunId}` detail
  surface

Current runtime response contract:

- `IConsoleOperateObservabilityClient` is the route-level read boundary.
  Each independently permissioned section returns
  `OperateSectionResult<T>` with `Allowed`, `Missing`, `Forbidden`,
  `Unavailable`, or `Unsupported` status. Non-allowed results render
  through `OperateSectionStatusPanel`.
- `HttpConsoleOperateObservabilityClient` reads from the active
  `ConsoleEnvironmentProfile.ServerBaseUri` and attaches the active
  account bearer token unless the profile is anonymous. No
  server-owned observability data is sourced from
  `OperateObservabilityFixture.Default` at runtime.
- The page starts overview, events, logs, alerts, realtime rules, jobs,
  and investigations reads in parallel. Rule health, job detail
  logs/artifacts, and investigation details are also fetched in parallel
  where the server contract requires sub-resource reads.
- The overview section is built from live admin status endpoints:
  `/api/v1/admin/version`, `/api/v1/admin/capabilities`,
  `/api/v1/admin/observability/telemetry`,
  `/api/v1/admin/observability/migrations`, and
  `/api/v1/admin/observability/errors`. It no longer probes the event feed
  to infer server metadata.
- `OperateEventQuery` maps event type, minimum severity, correlation id,
  trace id, request id, service id, resource ref, actor, operation id,
  release id, change set id, from, to, and page size to server query
  parameters on `GET /api/v1/admin/observability/events`. The current
  environment filter is applied after mapping because the current server
  event page does not carry an explicit environment query parameter.
- `OperateStatus` normalizes state strings by trimming, lowercasing, and
  replacing underscores with spaces. Neutral states are `unknown`,
  `unsupported`, `missing`, `disabled`, `not configured`, and
  `unconfigured`; `missing` displays as `unknown`, and `unconfigured`
  displays as `not configured`.
- Failure styling is reserved for `critical`, `error`, `failed`,
  `failing`, `firing`, `invalid`, `misconfigured`, `unhealthy`, and
  `blocked`; `configured` renders as success. A neutral telemetry fact is
  not a failed environment, even when the server omitted, disabled, or
  cannot support that signal.
- `OperateObservabilityRoutes.EventDetail`, `AlertDetail`, and
  `JobDetail` emit `/operate/events/{eventId}`,
  `/operate/alerts/{alertId}`, and `/operate/jobs/{jobRunId}` with the
  route id escaped as a path segment.
- `/operate/jobs/{jobRunId}` resolves a live job detail read from
  `/api/v1/admin/jobs/{jobRunId}` plus job logs and artifacts. Event and
  alert detail routes are page-scoped in this slice: the UI selects the
  matching row from the live event or alert page. When a route id is
  supplied but is missing from the loaded live page, the detail panel
  renders the shared `Missing` status instead of falling back to an
  unrelated first row.
- `OperateAiAdvisory` is advisory only: event and alert details must keep
  raw evidence links visible beside the advisory summary and suggested
  actions.
- `OperateAlertRule.CanEnable` is true only when the rule is disabled and
  valid. A rule is invalid when its normalized status is `invalid` or it
  has validation messages.
- Rule health and geofence zone reads are sub-resources of the rules
  surface. Failed rule-health reads are preserved on each rule as
  unavailable/forbidden/unsupported validation evidence, and failed zone
  reads render a `Geofence zones` status panel instead of an empty-zone
  message.
- Investigation summaries are not treated as complete detail records. If
  an investigation detail read fails, the card carries the detail status
  and message so missing pins and linked alerts/jobs do not look like an
  intentional empty state.
- `OperateJobRun.DetailHref` is the single job detail link for Studio,
  publishing, GitOps, temporal, alert delivery, import, and maintenance
  work. Job actions render from server-declared descriptors on
  `ConsoleJobDetail.Actions`; the current read-only slice does not issue a
  separate `/api/v1/admin/jobs/{jobRunId}/actions` read. Action controls
  stay disabled when the server says the action is unavailable.
- Job logs and artifacts are sub-resources of the job detail surface. A
  failed log or artifact read carries its `OperateSectionStatus` and message
  onto `OperateJobRun`, and the job detail panel renders the shared
  forbidden/missing/unsupported/unavailable status beside the logs and
  artifacts sections instead of substituting generic or empty data.

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

Console needs stable product-level APIs even if backing providers differ
by deployment. The current `honua-console#24` binding consumes the
concrete honua-server v1 admin routes below through
`OperateAdminRoutes` in `Honua.Console.Contracts` until
honua-sdk-dotnet projects the same contracts.

Read paths used by the Operate page:

- `GET /api/v1/admin/version`
- `GET /api/v1/admin/capabilities`
- `GET /api/v1/admin/observability/errors`
- `GET /api/v1/admin/observability/telemetry`
- `GET /api/v1/admin/observability/migrations`
- `GET /api/v1/admin/observability/events`
- `GET /api/v1/admin/observability/logs`
- `GET /api/v1/admin/observability/alerts`
- `GET /api/v1/admin/alerts/rules`
- `GET /api/v1/admin/alerts/rules/{ruleId}/health`
- `GET /api/v1/admin/alerts/zones`
- `GET /api/v1/admin/jobs`
- `GET /api/v1/admin/jobs/{jobRunId}`
- `GET /api/v1/admin/jobs/{jobRunId}/logs`
- `GET /api/v1/admin/jobs/{jobRunId}/artifacts`
- `GET /api/v1/admin/investigations`
- `GET /api/v1/admin/investigations/{investigationId}`

Additional routes mirrored by the shim for the same server contract
family, but not invoked by the current read-only UI slice:

- `GET /api/v1/admin/observability/audit`
- `POST /api/v1/admin/observability/alerts/{eventId}/acknowledge`
- `POST /api/v1/admin/observability/alerts/{eventId}/suppress`
- `POST /api/v1/admin/observability/alerts/{eventId}/resolve`
- `POST /api/v1/admin/alerts/rules/test`
- `GET /api/v1/admin/jobs/{jobRunId}/actions`

Events accept these query parameters when present:

- `kind`
- `minSeverity`
- `correlationId`
- `traceId`
- `requestId`
- `serviceId`
- `resourceRef`
- `actor`
- `operationId`
- `releaseId`
- `changeSetId`
- `from`
- `to`
- `pageSize`

The Console client requests `pageSize=50` for event and alert pages and
`limit=50` for jobs. Alert rules and zones use the server
`success/data/message` envelope; the other page responses use direct
camelCase JSON payloads. `OperateObservabilityJsonContext` is the
source-generated serialization context for trim/AOT safety.

Status mapping is part of the response contract:

- `401` and `403` -> `Forbidden`
- `404` -> `Missing`
- `501` -> `Unsupported`
- empty, unreadable, unreachable, or other non-success responses ->
  `Unavailable`

Backend responses should include deep links to raw providers where
available. Console preserves those links beside AI advisory summaries.

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
9. Replace the Console `OperateObservabilityContracts.cs` shim with the
   honua-sdk-dotnet projection for server overview, event viewer, logs,
   alerts, realtime rules, jobs, and investigations.
10. Surface AI DevOps summaries as advisory records linked to immutable evidence.

## Open Questions

- Which event storage/query path is authoritative for self-hosted Community deployments?
- Which alerts are Console-native versus external provider projections?
- Should investigations live in `honua-server` metadata or `honua-devops` support state?
- Which log fields are always redacted, and which are permission-sensitive?
- What minimum observability works when OTLP is disabled?
