# Temporal Data Viewer Information Model

Status: draft for backlog planning.

## Purpose

Honua Console should optionally expose temporal data history for datasets that are configured to retain row or feature history. The experience should feel like "git over data" to an operator or analyst: browse a dataset at a point in time, compare two points in time, see who changed what, and request a rollback. The backend must still treat this as a governed data operation rather than direct source-control semantics over arbitrary tables.

This complements, but does not replace, versioned editing in [honua-server#371](https://github.com/honua-io/honua-server/issues/371). Versioned editing is about concurrent named edit versions, reconcile, and post. Temporal data history is about inspectable, attributable history for committed data states.

It also provides the right review surface for disconnected sync conflicts. Esri models offline/disconnected editing through the Feature Service `Sync` capability, `createReplica`, `synchronizeReplica`, server generations, and named replicas. Honua already has lower-level replication foundation in [honua-server#305](https://github.com/honua-io/honua-server/issues/305), [honua-server#383](https://github.com/honua-io/honua-server/issues/383), and mobile conflict policy work in [honua-server#831](https://github.com/honua-io/honua-server/issues/831). The missing layer is a first-class conflict review information model that Console can use.

## Product Promise

- Users can inspect a temporal dataset at a timestamp, named checkpoint, release operation, or job run.
- Users can compare two states and see added, removed, geometry-changed, and attribute-changed features.
- Users can filter history by editor, job, service, release, field, geometry extent, feature id, or time range.
- Users can open a feature timeline showing each revision, changed fields, geometry delta summary, actor, source client, and reason.
- Users can review disconnected sync conflicts using the same feature diff and timeline components.
- Users can request rollback for a feature, selection, layer, or release-scoped change set when policy allows it.
- Rollback creates a new corrective operation with audit evidence; it does not erase immutable history.

## Non Goals

- Do not promise rollback for every data source. Some sources will be read-only or history-only.
- Do not expose raw database temporal-table implementation details as the primary UX.
- Do not allow rollback that bypasses RBAC, retention policy, validation, or job-runner execution.
- Do not make named-version reconcile/post dependent on the viewer. The viewer can use versioned editing history when available, but it must also work for simpler temporal tables.
- Do not collapse offline replica sync into versioned editing. A replica conflict can be visualized with the same diff model, but replica registration, server generations, and sync upload/download policies are their own server contract.

## Source Capability

`TemporalSourceCapability` describes what a layer or table can support.

Key fields:

- `source_id`
- `resource_id`
- `layer_id`
- `mode`: `none`, `as_of`, `history`, `diff`, `rollback`
- `history_model`: `system_versioned`, `valid_time`, `transaction_time`, `bitemporal`, `delta_log`, `external_audit`
- `stable_feature_id_field`
- `valid_from_field`
- `valid_to_field`
- `transaction_id_field`
- `actor_field`
- `operation_id_field`
- `geometry_history_supported`
- `attribute_history_supported`
- `rollback_supported`
- `retention_policy_id`
- `schema_evolution_policy`
- `minimum_snapshot_granularity`
- `max_diff_window`
- `sync_capability`: `none`, `download`, `upload`, `bidirectional`
- `sync_conflict_review_supported`
- `replica_tracking_supported`
- `change_tracking_supported`

Capability discovery is server-owned. Console should render unsupported modes clearly, but the server decides whether a source is eligible.

## Esri Parity Concepts

Honua should explicitly map the Esri concepts instead of inventing opaque names:

- `Sync` capability: service-level declaration that offline/disconnected workflows are supported.
- `syncCapabilities`: supported sync direction, per-layer/per-replica sync, async operation, registration of existing data, and rollback-on-failure behavior.
- `replicaID`: durable server identity for a disconnected copy.
- `replicaName`: optional user-friendly replica name, unique per feature service.
- `replicaServerGen` and `layerServerGens`: sync cursors that establish the base state for later uploads/downloads.
- `ChangeTracking`: efficient server-side change extraction; useful for history and sync but not equivalent to `Sync`.
- Branch-version-per-replica: Esri can create a branch version for each downloaded map when data is branch versioned. Honua should model this as an optional `replica_version_ref`, not as the only offline strategy.

## Core Objects

### Temporal Checkpoint

Named or implicit point that can be used as a history cursor.

Key fields:

- `checkpoint_id`
- `source_id`
- `resource_id`
- `cursor_type`: `timestamp`, `transaction`, `release`, `job`, `named_checkpoint`
- `cursor_value`
- `label`
- `created_at`
- `created_by`
- `operation_ref`
- `git_ref`
- `job_run_id`
- `metadata_release_id`

Examples:

- "before release 2026-05-23.4"
- "after parcel migration job 018"
- "2026-05-22 14:00 HST"

### Temporal Revision

One committed state of one feature or row.

Key fields:

- `revision_id`
- `source_id`
- `feature_id`
- `valid_from`
- `valid_to`
- `transaction_time`
- `operation`: `insert`, `update`, `delete`, `restore`
- `actor_id`
- `actor_display_name`
- `source_client`
- `job_run_id`
- `release_operation_id`
- `edit_session_id`
- `reason`
- `schema_version`
- `geometry_digest`
- `attribute_digest`

### Temporal Change Set

A grouped set of revisions caused by one edit session, job, release, import, sync, or rollback.

Key fields:

- `change_set_id`
- `source_id`
- `operation_ref`
- `operation_kind`: `interactive_edit`, `bulk_import`, `etl_job`, `metadata_release`, `sync`, `rollback`
- `started_at`
- `completed_at`
- `actor_id`
- `status`
- `feature_counts`
- `field_counts`
- `geometry_change_count`
- `validation_summary`
- `rollback_eligibility`

### Disconnected Replica

Durable server-side record for an offline/disconnected dataset copy.

Key fields:

- `replica_id`
- `replica_name`
- `source_id`
- `resource_id`
- `owner_id`
- `client_id`
- `device_id`
- `created_at`
- `last_sync_at`
- `sync_model`: `per_replica`, `per_layer`
- `sync_direction`: `download`, `upload`, `bidirectional`, `snapshot`
- `replica_geometry`
- `layer_queries`
- `base_checkpoint_id`
- `replica_server_gen`
- `layer_server_gens`
- `replica_version_ref`
- `status`: `active`, `closed`, `expired`, `conflicted`, `unregistered`

`replica_version_ref` is optional. It is used when the source implements a branch-version-per-replica pattern.

### Sync Conflict

Server-detected conflict between a disconnected edit and the current server state.

Key fields:

- `conflict_id`
- `replica_id`
- `source_id`
- `feature_id`
- `layer_id`
- `base_revision_id`
- `client_revision`
- `server_revision_id`
- `conflict_type`: `attribute`, `geometry`, `delete_update`, `update_delete`, `insert_duplicate`, `attachment`, `relationship`
- `field_conflicts`
- `geometry_conflict`
- `server_change_set_id`
- `client_change_set_id`
- `detected_at`
- `resolution_policy`: `manual`, `client_wins`, `server_wins`, `field_merge`, `reject`
- `status`: `pending`, `resolved`, `rejected`, `superseded`

### Sync Conflict Resolution

Decision record for one conflict or batch of conflicts.

Key fields:

- `resolution_id`
- `conflict_ids`
- `resolved_by`
- `resolved_at`
- `resolution_action`: `accept_client`, `keep_server`, `merge_fields`, `restore_base`, `reject_client_edit`
- `field_resolutions`
- `geometry_resolution`
- `result_revision_id`
- `audit_event_ids`
- `job_run_id`

### Temporal Diff

Server-generated comparison between two checkpoints or time cursors.

Key fields:

- `diff_id`
- `source_id`
- `from_checkpoint_id`
- `to_checkpoint_id`
- `extent`
- `filter`
- `summary`
- `added_features`
- `removed_features`
- `updated_features`
- `geometry_changed_features`
- `attribute_changed_features`
- `sample_feature_changes`
- `generated_at`
- `expires_at`

Diff results must be page-able and resumable. Large diffs should be produced by the job runner and exposed as artifacts.

### Feature Diff

Per-feature comparison.

Key fields:

- `feature_id`
- `change_type`: `added`, `removed`, `updated`, `unchanged`
- `from_revision_id`
- `to_revision_id`
- `attribute_changes`
- `geometry_change`
- `actor_ids`
- `operation_refs`
- `validation_findings`

`attribute_changes` should include field-level before/after values when policy allows it. Sensitive fields can be masked.

### Temporal Rollback Plan

Server-authored plan for a proposed rollback.

Key fields:

- `rollback_plan_id`
- `source_id`
- `target_scope`: `feature`, `selection`, `layer`, `change_set`, `release`
- `target_checkpoint_id`
- `current_checkpoint_id`
- `affected_feature_count`
- `validation_findings`
- `compatibility_findings`
- `requires_job`
- `requires_approval`
- `rollback_mode`: `metadata_only`, `data_revert`, `service_revision_switch`, `script_required`, `manual`
- `generated_script_ref`
- `estimated_duration`
- `risk_level`

Rollback plans are immutable evidence. Executing a plan creates a new operation and a new checkpoint.

### Temporal Rollback Operation

Tracked execution record for a rollback.

Key fields:

- `rollback_operation_id`
- `rollback_plan_id`
- `job_run_id`
- `status`
- `requested_by`
- `approved_by`
- `started_at`
- `completed_at`
- `result_checkpoint_id`
- `audit_event_ids`
- `artifact_refs`
- `failure_reason`

## Console Views

### Dataset Timeline

Purpose: show the available history for a layer/table.

Inputs:

- source capability
- checkpoints
- change sets
- retention policy

Required states:

- no temporal support
- as-of only
- diff available
- rollback available
- history retention warning

### As-Of Viewer

Purpose: render the map/table as it existed at a selected checkpoint or timestamp.

Controls:

- checkpoint selector
- time scrubber for dense history
- actor/source/job filters
- extent filter
- field filter

### Diff Viewer

Purpose: compare two states.

Visual layers:

- added
- removed
- geometry changed
- attribute changed
- unchanged context

Panel content:

- diff summary counts
- feature change table
- field-level diff
- actor and operation attribution
- links to jobs, GitOps releases, edit sessions, and audit events

### Feature Timeline

Purpose: inspect the complete revision history for one feature.

Content:

- revision list
- geometry preview per revision
- field changes
- actor/source/reason
- related operation
- restore-to-this-revision action when allowed

### Rollback Review

Purpose: make rollback explicit, validated, and auditable.

Content:

- target scope and checkpoint
- affected features and fields
- validation and compatibility findings
- generated script or job plan
- approval requirements
- resulting checkpoint preview

### Sync Conflict Review

Purpose: resolve disconnected edit conflicts using the same visual language as temporal diffs.

Content:

- replica name, owner, device, sync generation, and affected layer
- base/client/server comparison for each conflicting feature
- field-level and geometry conflict markers
- server-side edits that happened since the replica base checkpoint
- resolution controls for client wins, server wins, field merge, reject, or defer
- batch resolution summary and audit trail

This view should be available from Operate, Catalog layer history, and field workflow administration. It should not require users to understand raw `replicaServerGen` values unless they open diagnostics.

## Backend API Shape

Initial endpoints can be implemented under the server admin or data API namespace, but the contract should stay product-level:

- `GET /temporal/capabilities?resourceId=...`
- `GET /temporal/checkpoints?sourceId=...`
- `POST /temporal/query-as-of`
- `POST /temporal/diff`
- `GET /temporal/diffs/{diffId}`
- `GET /temporal/features/{featureId}/timeline`
- `POST /temporal/rollback-plans`
- `POST /temporal/rollback-operations`
- `GET /temporal/rollback-operations/{operationId}`
- `GET /temporal/replicas?sourceId=...`
- `GET /temporal/replicas/{replicaId}/conflicts`
- `GET /temporal/sync-conflicts/{conflictId}`
- `POST /temporal/sync-conflict-resolutions`

Large operations should return job references and artifact links instead of forcing synchronous responses.

## Relationship To GitOps Metadata Publishing

Temporal data history improves GitOps safety when metadata releases include data scripts:

- preflight can capture a before checkpoint
- release operations can link to data change sets
- rollback reports can state whether data rollback is automatic, scripted, or manual
- Console can compare data state before and after a release
- compatibility validation can use history to prove whether a breaking metadata change was accompanied by the expected data transformation
- release preflight can detect active replicas whose base schema or data compatibility would be broken by a proposed metadata/data-script release

## Relationship To AI Studio

AI-generated analysis and dashboards should be able to target temporal views explicitly:

- "show parcels as they looked before yesterday's import"
- "map all hydrants whose inspection status changed this month"
- "summarize edits by contractor over the last quarter"
- "create a dashboard comparing current zoning to the pre-release version"
- "show disconnected sync conflicts for the water inspection dataset and group them by crew"

The AI layer should produce structured temporal queries against the same backend contract. It should not invent history when the source capability says history is unavailable.

## Security And Governance

- History access is separately permissioned from current-data read access.
- Rollback requires write permission plus rollback-specific entitlement.
- Sensitive fields can be masked in diffs while still counting as changed.
- Immutable audit history is never deleted by rollback.
- Retention policy must be visible before a user relies on history for compliance.
- Bulk rollback must run through the job runner with logs, artifacts, and approval evidence.
- Conflict resolution requires edit permission on the target layer plus replica/conflict-review entitlement.
- Conflict review must preserve the base, client, and server versions even after resolution.

## Backend Backlog

1. Discover and persist temporal source capabilities for configured layers and tables.
2. Implement as-of query cursors for supported temporal models.
3. Implement checkpoint listing and release/job/edit-session checkpoint linking.
4. Implement temporal diff jobs with summary, paging, and artifacts.
5. Implement feature timeline with field-level and geometry-change attribution.
6. Implement rollback plan generation with validation, compatibility, and approval requirements.
7. Execute approved rollback through the job runner as a forward corrective operation.
8. Link temporal change sets to GitOps metadata releases, ETL jobs, imports, sync operations, and edit sessions.
9. Add SDK contracts so Console, MCP, QGIS, and Studio can consume the same temporal capability model.
10. Model disconnected replicas with replica names, sync cursors, base checkpoints, layer filters, owner/device metadata, and optional branch-version references.
11. Expose sync conflict reads and resolution writes using the same feature diff primitives as temporal history.
12. Validate metadata/data-script releases against active replicas so Console can warn before schema or compatibility changes strand disconnected clients.

## Open Questions

- Which temporal table pattern is the default for first-party Postgres sources?
- Should rollback be Enterprise-only while read-only temporal viewing is Pro?
- What is the minimum acceptable geometry diff for MVP: digest/bounds only, or visual geometry delta?
- Which audit fields are mandatory for a source to be eligible for rollback?
- Should named checkpoints be user-created, release-created, or both?
- Should disconnected sync conflict review ship in Pro because it supports field workflows, while branch-version-per-replica remains Enterprise?
- Do we need full Esri `Sync` API parity for Console conflict review, or can Console use a Honua-native admin contract while FeatureServer compatibility stays on the public REST surface?
