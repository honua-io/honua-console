# UI Surface Briefs

Status: handoff draft.

These briefs describe the design surfaces that should exist. They intentionally avoid prescribing final layout.

## Console Shell

Purpose: one product surface for create, operate, publish, and share.

Primary areas:

- Studio
- Catalog
- Operate
- Share

Persistent needs:

- workspace indicator
- environment indicator where relevant
- global search
- recent/pinned content
- notifications/inbox
- user/RBAC context
- active job/alert/release indicators

Important states:

- no workspace
- no environment selected
- insufficient permission
- disconnected/degraded server
- native mTLS warning
- background job running

## Studio

Purpose: AI GIS development surface.

Major views:

- prompt/conversation
- source context and data binding picker
- package/spec inspector
- preview canvas
- editors for map, dashboard, report, form, app, workflow
- validation and warnings
- publish review

Information objects:

- Studio Project
- Conversation
- Package
- Data Binding
- Content Item
- Content Version
- Job Run
- Publication

Design challenges:

- Keep AI generation inspectable.
- Let users edit package outputs without needing raw JSON.
- Make preview, saved draft, and published state visually distinct.
- Show validation before execution or publish.

## Catalog

Purpose: content discovery, inspection, lineage, versions, and reuse.

Major views:

- catalog search/list
- item detail
- version history
- lineage/provenance
- data binding usage
- publication/share state
- temporal capability entry point

Information objects:

- Content Item
- Content Version
- Package
- Publication
- Semantic Resource
- Data Binding
- Temporal Source Capability

Design challenges:

- Support many content types without creating many unrelated detail pages.
- Make lineage and dependency risk understandable.
- Show where an item is used before editing or retiring it.

## Share

Purpose: public links, embeds, exports, open-data pages, and external publication flows.

Major views:

- share policy review
- public link/embed configuration
- open-data page configuration
- export configuration
- access/evidence preview

Information objects:

- Content Item
- Content Version
- Publication
- Sharing Policy
- Embed Policy
- Export Job

Design challenges:

- Make public/private state unambiguous.
- Show exactly what external users will see.
- Expose permission and data-sensitivity blockers early.

## Operate Overview

Purpose: current operational state for environments and servers.

Major views:

- environment/server overview
- health and telemetry status
- active alerts
- recent jobs/releases/failures
- quick links to event viewer, jobs, alerts, releases, logs, GitOps, temporal operations

Information objects:

- Environment
- Server
- Server Telemetry Status
- Alert
- Job Run
- Operational Event

Design challenges:

- Avoid a status-page clone. Operators need action and context.
- Show unknown/unconfigured states without treating them as failures.
- Make multi-environment context obvious.

## Event Viewer

Purpose: one timeline/table for operational evidence.

Major views:

- event search
- filter panel
- event detail
- related objects
- raw evidence links
- AI DevOps summary
- pin to investigation

Information objects:

- Operational Event
- Log Record
- Alert
- Job Run
- Release Operation
- Change Set
- Replica
- Investigation

Design challenges:

- Dense but understandable filtering.
- Clear distinction between logs, audit events, alerts, jobs, releases, sync, and data changes.
- Never hide raw evidence behind AI summary.

## Alerts And Realtime Rules

Purpose: configure, inspect, and resolve alerts.

Major views:

- alert list/detail
- alert evidence timeline
- rule list/detail
- geofence zone editor/selector
- condition builder
- delivery channel status
- dead-letter/retry status

Information objects:

- Alert
- Alert Rule
- Geofence Zone
- Delivery Channel
- Operational Event
- Investigation

Design challenges:

- Make rule validation and delivery state explicit.
- Show alert lifecycle: firing, acknowledged, suppressed, resolved.
- Prevent destructive or noisy actions without permissions.

## Jobs

Purpose: inspect and operate long-running/background work.

Major views:

- job list
- job detail
- stage/progress panel
- logs
- artifacts
- failure classification
- allowed actions
- related events/investigations

Information objects:

- Job Run
- Job Stage
- Job Artifact
- Operational Event
- Content Item
- Release Operation

Design challenges:

- Show progress and failures without log spelunking.
- Make actions stateful and policy-aware.
- Support jobs launched from many surfaces.

## GitOps Metadata Publishing

Purpose: governed cross-environment metadata and service/layer/content release.

Major views:

- release proposal
- semantic resource diff
- environment matrix
- compatibility preflight
- data script coverage
- Git PR preview
- CI/GitOps timeline
- rollback review

Information objects:

- Metadata Release Package
- Semantic Resource
- Environment Binding
- Compatibility Report
- Data Script
- Git Operation
- Release Operation
- Rollback Plan

Design challenges:

- Show semantic changes before raw manifests.
- Make blockers impossible to miss.
- Explain rollback class before merge/deploy.

## Temporal Data Viewer

Purpose: inspect and compare data across time.

Major views:

- capability/retention state
- checkpoint selector
- as-of map/table
- diff viewer
- feature timeline
- rollback review

Information objects:

- Temporal Source Capability
- Temporal Checkpoint
- Temporal Revision
- Temporal Change Set
- Temporal Diff
- Rollback Plan

Design challenges:

- Make as-of, diff, and rollback modes distinct.
- Explain unsupported/limited temporal capability clearly.
- Treat rollback as a new governed operation, not history deletion.

## Sync Conflict Review

Purpose: resolve disconnected/offline edit conflicts.

Major views:

- replica list
- conflict queue
- base/client/server feature comparison
- field and geometry conflict markers
- resolution controls
- batch resolution summary

Information objects:

- Disconnected Replica
- Sync Conflict
- Sync Conflict Resolution
- Temporal Revision
- Temporal Change Set

Design challenges:

- Keep Esri-compatible concepts available without forcing protocol details into the default UI.
- Make base/client/server comparison understandable.
- Preserve audit trail and defer option.

## Native Console Environment Manager

Purpose: optional desktop/native operator host for multiple environments, native gRPC streaming, and mTLS.

Major views:

- environment profile list
- add/edit environment
- server capability check
- auth state
- certificate selector/status
- trust diagnostics
- environment switcher

Information objects:

- Native Environment Profile
- Trust Profile
- Client Certificate Reference
- Server Telemetry Status
- Environment

Design challenges:

- Keep native mTLS optional and enterprise-oriented.
- Make certificate errors understandable: missing, expired, untrusted, wrong environment, insufficient RBAC.
- Do not create a separate product mental model from web Console.

## AI DevOps Advisory Surface

Purpose: explain operational evidence and propose next actions.

Appears in:

- event viewer
- alerts
- jobs
- GitOps releases
- investigations
- server overview

Rules:

- AI summaries are advisory.
- AI output must link to evidence.
- AI cannot resolve/suppress alerts, approve releases, or execute rollback without explicit user action.

