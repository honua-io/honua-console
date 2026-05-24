# GitOps Metadata Publishing Information Model

Status: draft

Owner surfaces: Honua Console Operate, Catalog, Studio, Share

Backend owners: `honua-server`, `honua-devops`

Related foundation:

- [honua-server#351](https://github.com/honua-io/honua-server/issues/351): GitOps manifest apply, dry run, prune, drift, approval workflows.
- [honua-server#515](https://github.com/honua-io/honua-server/issues/515): GitOps drift detection and manifest rollback.
- [honua-server#518](https://github.com/honua-io/honua-server/issues/518): GitOps git repo watching and change management UI.
- [honua-server#992](https://github.com/honua-io/honua-server/issues/992): GitOps config promotion and production rollback gaps.
- [honua-server#1035](https://github.com/honua-io/honua-server/issues/1035): Metadata v2 canonical resource graph.
- [honua-server#1162](https://github.com/honua-io/honua-server/issues/1162): Console metadata v2 content and RBAC API baseline.
- [honua-devops#13](https://github.com/honua-io/honua-devops/issues/13): Desired-state schemas.
- [honua-devops#14](https://github.com/honua-io/honua-devops/issues/14): `honua-gitops` engine.
- [honua-devops#17](https://github.com/honua-io/honua-devops/issues/17): Safe release orchestration.
- [honua-devops#18](https://github.com/honua-io/honua-devops/issues/18): ServiceBundle reconciliation.

## Purpose

Honua already has GitOps, deploy preflight, manifest history, drift, approval, and rollback foundations. The missing workflow is the higher-level round trip for metadata and GIS artifacts:

1. Author or edit metadata in Console, Studio, MCP, or an AI DevOps flow.
2. Compare the proposed change against dev, staging, and production semantic state.
3. Prevalidate data compatibility, service compatibility, package compatibility, and optional data scripts before deployment.
4. Write the operation to Git as a pull request.
5. Let CI and the GitOps controller apply, promote, and roll back with deterministic evidence.

This document defines the information model needed to connect Console to the existing server and devops primitives without creating a separate UI-only workflow.

## Design Rules

- Git is the operation log and desired-state source for promotion.
- Server remains authoritative for runtime truth, metadata validation, content versions, RBAC, drift reads, operation status, and rollback handles.
- `honua-devops` owns GitOps planning, PR authoring, promotion orchestration, AI DevOps guidance, and runtime adapter execution.
- Console owns proposal authoring, visualization, review, approval handoff, and operational monitoring.
- Semantic identities travel across environments; physical IDs and URLs are environment bindings.
- Data scripts are optional release attachments, never hidden side effects.
- Rollback semantics must be known before merge or execution.

## Existing Foundation To Reuse

`honua-server` already has:

- manifest version storage in `honua.manifest_versions`
- pending approval storage in `honua.manifest_pending_changes`
- GitOps watch config/change records in `honua.gitops_watch_configs` and `honua.gitops_change_records`
- admin manifest export/apply/drift/version endpoints
- deploy preflight and deploy operation endpoints under `/api/v1/admin/deploy`
- metadata v2 canonical resource graph work

`honua-devops` already has:

- desired-state object schemas for `PlatformStack`, `PlatformRelease`, `ServiceBundle`, `Promotion`, and `ExecutionPolicy`
- `honua-gitops` plan/diff/sync/status/drift/pause/resume/approve/promote/rollback semantics
- release orchestration stages for preflight, backup, migration, rollout, smoke, SLO watch, promote, and rollback
- service bundle reconciliation over Honua control-plane exports
- AI operator trust boundaries and evidence model

## Missing Round Trip

The current foundation can move deployment and manifest state. The new capability needs to move semantic metadata releases across environments.

The missing objects are:

- semantic IDs for services, layers, fields, maps, dashboards, forms, apps, workflows, and GP/ETL services
- environment bindings from semantic IDs to physical IDs, URLs, schemas, tables, secrets, queues, and runtime targets
- metadata release packages that bundle changed artifact versions plus target environments
- compatibility reports that compare package requirements to each target environment
- optional data scripts with declared before/after contracts
- Git PR operations with generated manifests, scripts, validation evidence, and rollback policy
- deployment/rollback operation records linked back to content versions and Git commits

## Core Objects

### Semantic Resource

Stable identity for a logical GIS resource across environments.

Examples:

- `service.property.parcels`
- `layer.property.parcels.current`
- `field.property.parcels.parcel_id`
- `map.planning.parcel_review`
- `dashboard.operations.permit_activity`
- `form.inspections.field_report`
- `workflow.property.parcel_enrichment`
- `gp_service.analytics.buffer_parcels`

Key fields:

- `semantic_id`
- `resource_type`
- `display_name`
- `owner_workspace_id`
- `canonical_item_id`
- `canonical_version_id`
- `lifecycle_state`
- `tags`

### Environment

Known runtime target that Console can inspect and deploy to.

Key fields:

- `environment_id`
- `name`
- `kind`
- `tier`
- `base_url`
- `runtime_target`
- `gitops_target`
- `metadata_export_capabilities`
- `data_validation_capabilities`
- `approval_policy`

### Environment Binding

Maps a semantic resource to its physical representation in one environment.

Key fields:

- `semantic_id`
- `environment_id`
- `physical_resource_id`
- `service_url`
- `layer_id`
- `catalog_item_id`
- `datastore_ref`
- `schema_name`
- `table_name`
- `secret_refs`
- `runtime_profile`
- `current_revision`
- `current_content_version_id`
- `last_observed_at`

### Metadata Release Package

Versioned proposed change set for one or more semantic resources.

Key fields:

- `release_package_id`
- `title`
- `summary`
- `source_environment_id`
- `target_environment_ids`
- `base_revision`
- `desired_revision`
- `change_summary`
- `changed_resources`
- `data_scripts`
- `validation_requirements`
- `rollback_policy`
- `evidence_refs`
- `git_operation_id`

### Changed Resource

One artifact change inside a release package.

Key fields:

- `semantic_id`
- `resource_type`
- `current_version_id`
- `desired_version_id`
- `change_class`
- `compatibility_contract`
- `dependent_semantic_ids`
- `breaking_change_hint`

Change classes:

- `metadata_only`
- `style`
- `access_policy`
- `service_config`
- `field_contract`
- `schema_contract`
- `data_transform`
- `publication`
- `workflow`
- `app_package`

### Data Script

Optional executable or declarative data change attached to a release.

Key fields:

- `script_id`
- `script_type`
- `target_environment_scope`
- `inputs`
- `declared_before_contract`
- `declared_after_contract`
- `idempotency_key`
- `dry_run_supported`
- `rollback_supported`
- `rollback_script_ref`
- `requires_backup`
- `execution_order`

Script types:

- `sql`
- `migration`
- `etl_workflow`
- `gp_workflow`
- `external_job`

### Compatibility Contract

Machine-readable expectation a proposed artifact has of its target data/runtime.

Key fields:

- `required_fields`
- `field_type_expectations`
- `required_domains`
- `geometry_type`
- `spatial_reference`
- `temporal_fields`
- `indexes`
- `required_capabilities`
- `service_protocols`
- `package_dependencies`
- `rbac_requirements`

### Compatibility Report

Prevalidation output generated before Git PR creation and again in CI.

Key fields:

- `report_id`
- `release_package_id`
- `environment_id`
- `status`
- `generated_at`
- `checks`
- `breaking_changes`
- `script_coverage`
- `affected_dependents`
- `rollback_readiness`
- `recommended_gate`

Statuses:

- `ready`
- `warning`
- `blocked`
- `unknown`

### Compatibility Check

Single finding inside a compatibility report.

Key fields:

- `check_id`
- `scope`
- `severity`
- `semantic_id`
- `environment_id`
- `message`
- `expected`
- `actual`
- `covered_by_script_id`
- `required_action`

Scopes:

- `metadata_schema`
- `data_schema`
- `data_values`
- `geometry`
- `crs`
- `service_protocol`
- `style`
- `form`
- `dashboard`
- `app`
- `workflow`
- `rbac`
- `rollback`

### Git Operation

Git-backed operation that turns a validated proposal into a pull request, merge, and GitOps apply.

Key fields:

- `git_operation_id`
- `repository`
- `branch`
- `base_branch`
- `pull_request_url`
- `commit_sha`
- `status`
- `generated_paths`
- `ci_status`
- `merge_status`
- `gitops_operation_id`

Generated paths should include:

- desired-state manifests
- metadata package files
- environment overlays
- optional data scripts
- compatibility reports
- rollback policy
- provenance record

### Release Operation

Server-visible execution record for a merged or submitted release.

Key fields:

- `operation_id`
- `release_package_id`
- `environment_id`
- `desired_revision`
- `current_revision`
- `git_operation_id`
- `deploy_operation_id`
- `job_ids`
- `status`
- `stage`
- `evidence_refs`
- `rollback_plan_id`
- `created_at`
- `updated_at`

### Rollback Plan

Typed recovery model generated before execution.

Key fields:

- `rollback_plan_id`
- `release_package_id`
- `environment_id`
- `rollback_mode`
- `last_known_good_revision`
- `metadata_revert_commit`
- `alias_repoint_supported`
- `data_restore_required`
- `script_rollback_required`
- `backup_ref`
- `estimated_blast_radius`
- `approval_required`

Rollback modes:

- `metadata_revert`
- `alias_repoint`
- `service_revision_revert`
- `script_rollback`
- `snapshot_restore`
- `manual_recovery`

## Workflow

### 1. Propose

Inputs can come from:

- Console metadata editor
- Studio publishing
- AI DevOps request
- MCP
- QGIS plugin
- imported manifest
- lower-environment tested release

Output:

- metadata release package
- changed resource list
- desired revision
- target environment list

### 2. Compare Environments

Server exports actual semantic state for source and target environments.

Output:

- environment bindings
- actual revisions
- drift report
- dependent artifact graph

### 3. Attach Data Scripts

User or AI DevOps may attach optional scripts. Scripts must declare before/after contracts so prevalidation can know what they cover.

Output:

- script list
- script coverage map
- rollback classification

### 4. Prevalidate

Server and devops jointly validate:

- metadata schema compatibility
- source/target data schema compatibility
- script coverage for incompatible schema or value changes
- map/dashboard/form/app dependency compatibility
- service protocol compatibility
- RBAC and sharing policy compatibility
- rollback readiness

Output:

- compatibility reports by environment
- blocking findings
- warning findings
- required evidence
- recommended promotion gate

### 5. Create Git PR

`honua-devops` writes the desired-state change to the control repo and opens a pull request.

Output:

- PR URL
- generated manifest paths
- compatibility reports
- CI check links
- rollback plan

### 6. CI Revalidates

CI reruns prevalidation and, where safe, data script dry-runs against lower or preview environments.

Output:

- required status checks
- compatibility report artifacts
- data script dry-run artifacts
- updated rollback evidence

### 7. Merge And Reconcile

GitOps controller applies the release to the target environment.

Execution stages:

- preflight
- backup
- data script dry-run or migration
- metadata apply
- service publication
- smoke
- SLO watch
- promotion gate

### 8. Rollback

Rollback should prefer immutable-version operations before destructive data restore:

1. repoint semantic alias to previous metadata/content version
2. revert Git commit or PR
3. revert service revision
4. run rollback script
5. restore data snapshot only when required

## Backend Ownership

### `honua-server`

Server should own:

- semantic resource and environment binding export
- metadata release package validation
- compatibility report generation
- dependency graph and affected artifact analysis
- data script before/after contract validation
- release operation records
- rollback plan persistence
- deploy preflight integration
- manifest export/apply/drift/version APIs

### `honua-devops`

DevOps should own:

- Git PR authoring
- desired-state manifest generation
- `honua-gitops` extension for metadata release packages
- AI DevOps planning and explanation
- CI validation workflow wiring
- promotion orchestration
- runtime adapter execution
- rollback command generation

### `honua-console`

Console should own:

- proposal creation and review
- environment comparison visualization
- compatibility report visualization
- data script coverage review
- Git PR handoff
- operation monitoring
- rollback approval and recovery visualization

## MVP Acceptance Shape

MVP is ready when a user can:

1. edit metadata in Console or choose a lower-environment tested metadata revision
2. select target environments
3. attach an optional data script
4. see whether the script covers the compatibility delta
5. generate a Git PR
6. watch CI and GitOps operation status
7. roll back by metadata version or Git revert when the release does not require data restore

Data restore rollback can remain a guarded later capability as long as the rollback report clearly classifies it as required.
