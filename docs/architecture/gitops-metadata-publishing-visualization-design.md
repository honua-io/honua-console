# GitOps Metadata Publishing Visualization Design

Status: draft

Owner surface: Honua Console Operate, with entry points from Studio and Catalog

Related model: [GitOps Metadata Publishing Information Model](gitops-metadata-publishing-information-model.md)

## Purpose

Console needs to make GitOps metadata publishing understandable without exposing users to raw manifests first. The user should see what semantic resources change, how those changes differ by environment, whether optional data scripts cover compatibility gaps, what Git PR will be created, and how rollback will work.

This document defines the prerequisite visualization design. It avoids final UI layout work and focuses on workflow, state, required views, and visual encodings.

## User Jobs

1. Promote a metadata/service/layer change that was tested in dev or staging.
2. Understand whether prod is semantically different from lower environments.
3. Attach or review data scripts that make a metadata change safe.
4. See what maps, dashboards, forms, apps, workflows, and services will be affected.
5. Create a Git PR with validation evidence.
6. Track the release through CI, GitOps reconciliation, smoke checks, and SLO gates.
7. Roll back with a clear understanding of whether rollback is metadata-only or data-affecting.

## Entry Points

### Studio

Studio can publish a generated artifact and then offer:

- publish to current environment
- promote to another environment
- create GitOps metadata release

### Catalog

Catalog can start from an existing content item or service and offer:

- compare across environments
- propose metadata change
- promote selected version
- inspect dependents

### Operate

Operate is the primary workspace for:

- environment inventory
- release queue
- GitOps operations
- approval gates
- rollback
- drift review

### AI DevOps

AI DevOps can draft or explain:

- proposed release package
- compatibility findings
- data script coverage
- PR contents
- rollback path

The AI output is advisory. The release package, compatibility report, Git PR, and operation record are the durable truth.

## Workflow Views

### 1. Release Proposal

Shows what is being promoted.

Required content:

- title and summary
- source environment
- target environments
- desired revision
- changed semantic resources
- change classes
- dependencies
- attached scripts
- rollback classification

Primary visual:

- grouped resource list by type: service, layer, field, map, dashboard, form, app, workflow, GP/ETL
- change badges: metadata, style, field contract, schema contract, service config, RBAC, workflow, app package

### 2. Environment Matrix

Shows semantic alignment across environments.

Rows:

- semantic resource IDs

Columns:

- dev
- staging
- prod
- optional preview environments

Cell states:

- same revision
- different revision
- missing binding
- drifted
- blocked
- unknown

Required interactions:

- select a row to see physical bindings
- expand dependents
- compare source vs target
- filter to changed/drifted/blocked

### 3. Impact Graph

Shows dependencies and blast radius.

Node groups:

- changed resources
- data sources
- services/layers
- maps
- dashboards
- reports
- forms
- apps
- workflows
- users/groups or access policies

Edge types:

- reads
- publishes
- embeds
- depends on field
- invokes workflow
- grants access

Required states:

- directly changed
- indirectly affected
- blocked
- covered by script
- rollback-sensitive

### 4. Compatibility Preflight

Shows whether the release can safely target each environment.

Sections:

- metadata schema
- data schema
- geometry/CRS
- service protocol
- style/popup/label
- form validation
- dashboard/chart bindings
- app routes/actions
- workflow/GP/ETL parameters
- RBAC/sharing
- rollback readiness

Finding severities:

- ready
- warning
- blocked
- unknown

Each finding should show:

- expected
- actual
- affected semantic resource
- affected environments
- whether a data script covers it
- required action

### 5. Data Script Coverage

Shows whether optional scripts make the change safe.

Required content:

- script order
- target environment scope
- dry-run support
- before contract
- after contract
- covered compatibility findings
- uncovered findings
- rollback support
- backup requirement

Visual rule:

- a script never hides a blocker; it changes a blocker to covered only when the declared after contract satisfies the compatibility check.

### 6. Git PR Preview

Shows what will be written to Git.

Required content:

- target repository
- branch
- base branch
- generated file tree
- manifest diff
- environment overlays
- scripts
- compatibility reports
- rollback policy
- commit message
- PR title/body preview

Required gates:

- no secrets in generated files
- no unresolved blockers
- required approvals identified
- rollback classification acknowledged

### 7. CI And GitOps Timeline

Shows operation status after PR creation or merge.

Stages:

- PR opened
- CI validation
- lower-env dry run
- approval
- merge
- GitOps reconcile
- preflight
- backup
- data script or migration
- metadata apply
- service publication
- smoke
- SLO watch
- promotion
- completed

Each stage should show:

- status
- evidence links
- logs
- responsible system
- retry/stop/rollback actions when allowed

### 8. Rollback View

Shows what rollback means before and after release.

Required content:

- last known good revision
- rollback mode
- metadata revert commit
- alias repoint option
- service revision revert option
- script rollback option
- data restore requirement
- backup reference
- expected blast radius
- approval requirement

Rollback status should distinguish:

- automatic metadata rollback available
- Git revert available
- service revision rollback available
- script rollback required
- data restore required
- manual recovery required

## Visualization States

### Environment Cell State

```text
ready       same semantic state or compatible target
changed     selected release changes this resource
drifted     target actual state differs from declared state
missing     semantic resource has no target binding
blocked     prevalidation found a release-blocking issue
unknown     actual state or validation evidence is incomplete
```

### Finding Severity

```text
ready       no action needed
warning     publish can continue, but user should review
blocked     publish cannot proceed without change or approval
unknown     evidence is missing; CI must not auto-promote
```

### Rollback Classification

```text
metadata-only        rollback can revert metadata/content/service pointers
service-revision     rollback requires service revision switch
script-reversible    rollback requires attached rollback script
snapshot-required    rollback requires data backup/restore
manual               rollback cannot be automated safely
```

## AI DevOps Surface In Console

Console should surface AI DevOps as an assistant inside the release workflow, not as a separate deployment product.

AI DevOps can:

- explain drift
- summarize impact
- suggest missing compatibility checks
- draft PR title/body
- propose data script coverage
- explain rollback classification
- recommend promotion gates

AI DevOps must not:

- silently change generated desired state
- bypass blockers
- execute Git or runtime operations without explicit user/gate approval
- become the source of truth for compatibility status

## Minimum MVP Views

MVP can be dense and operational. It does not need a polished canvas.

Required MVP:

1. release proposal summary
2. environment matrix
3. compatibility findings table
4. data script coverage table
5. Git PR preview
6. CI/GitOps timeline
7. rollback summary

The impact graph can start as an expandable dependency table if graph layout is not ready.

## Backend Data Dependencies

Console needs APIs or SDK contracts for:

- semantic resource search and environment bindings
- environment actual-state export
- release package create/update/read
- compatibility report generation
- affected dependent graph
- data script contract validation
- Git PR proposal/create/status
- deploy operation status
- rollback plan/status
- AI DevOps explanation hooks

## Open Questions

- Should Git PR creation be initiated by server or directly by `honua-devops` through a Console-facing API?
- Should compatibility reports be persisted by server before PR creation, or generated on demand and committed only in Git?
- What is the minimum script sandbox for dry-run validation against lower environments?
- Should semantic IDs be globally tenant-unique or namespaced by workspace/catalog?
- Which rollback classes are allowed for self-service users versus operators only?
