# Studio Unified GP And ETL Workflow Editor

Status: first Console implementation for honua-console#17.

## Contract Boundary

The editor works at the server contract boundary instead of owning a Console-only
runtime. The implemented surface uses these contract sources:

- `honua-server:WorkflowDefinition` for reusable workflow definitions,
  trigger metadata, workflow steps, bindings, versions, and publication.
- `honua-server:AnalysisPlan` for executable per-step process plans.
- `@honua/sdk-js` `IJobRun` and the unified process runner adapter for dry-run
  and sample-run job lifecycle status.
- OGC API Processes route shape for eligible process service invocation.

The local fixture client in `src/studio/workflows/fixtureClient.ts` is a
deterministic transport stand-in. It exists behind `StudioWorkflowTransport` so
the server client can replace it without changing editor state or UI behavior.

The TypeScript payloads in `src/studio/workflows/types.ts` are a structural
projection for this boundary. They should be replaced by imported server or SDK
DTOs where those shared contracts are available; Console should not fork the
protocol.

## Implemented Workflow

1. A Studio builder enters a natural-language request.
2. Console asks the transport to create a workflow draft and displays the
   generated workflow definition JSON before execution.
3. The builder can edit the definition JSON and run validation.
4. Validation surfaces missing parameters, unsupported transforms, sink
   constraints, permission issues, and structural contract failures.
5. Dry-run and sample-run submit through the SDK process runner and display job
   snapshots, logs, artifacts, row failures, and provenance links.
6. The builder can publish the workflow as a manual or scheduled batch
   definition.
7. Eligible workflows can publish an OGC process service route with parameter
   metadata, result package metadata, permissions, and a backing content item.
8. Published workflow content items expose provenance, versions, rollback, and
   run-history links.

## Response Contract

`StudioWorkflowTransport` is the editor contract consumed by the React surface:

- `createDraftFromPrompt(prompt)` returns `WorkflowDraft`.
  - Includes `draftId`, original `prompt`, `generatedAt`, `warnings`,
    `explanation`, `eligibleProcessService`, and `generatedContract`.
  - Carries an inspectable `definition` projection of
    `honua-server:WorkflowDefinition` with `workflowId`, `name`, `mode`,
    `steps`, optional `trigger`, timestamps, and metadata.
  - Each workflow step carries `nodeKind`, canonical `AnalysisPlan` payload,
    inputs, dependencies, input bindings, failure policy, and optional retry or
    timeout metadata.
- `validateDefinition(definition)` accepts parsed JSON and returns
  `WorkflowValidationResult`; syntactically valid non-workflow JSON returns
  blocked contract issues instead of reaching graph rendering.
  - `status` is `valid`, `warning`, or `blocked`.
  - Issues include `kind`, `severity`, `code`, JSON `path`, optional `nodeId`,
    message, and `requiredAction`.
  - Issue kinds are `missing-parameter`, `unsupported-transform`,
    `sink-constraint`, `permission`, `contract`, and `warning`.
  - `contractReferences` echoes the shared contract sources used for the check.
- `runDefinition(definition, mode)` returns `WorkflowRunRecord`.
  - `mode` is `dry-run` or `sample-run`.
  - `status` and `snapshots` use the SDK job lifecycle shape.
  - Results expose logs, artifacts, row-level feature failures, and provenance
    links. A validation-blocked definition returns a failed run with a
    `WorkflowValidationBlocked` snapshot and no artifacts.
- `publishDefinition(definition, request)` returns
  `PublishedWorkflowContentItem`.
  - The request selects `manual` or `scheduled` execution and may include a
    cron expression and time zone.
  - Publication rejects definitions with blocked validation results.
  - Scheduled publication requires a non-empty five-field cron expression.
  - The response includes content item identity, `workflow-definition` kind,
    execution modes, optional schedule, versions, active version, provenance
    hash, upstream item references, and run-history link.
- `publishProcessService(definition)` returns `ProcessServicePublication`.
  - Blocked definitions are rejected.
  - Eligibility is derived from the current definition, not the original draft:
    a publication node must opt into process-service publication, bind an
    upstream result package, carry process-capable plan metadata, and have
    publish-process permission.
  - Successful responses include a stable
    `/ogc/processes/processes/{processId}/execution` route, parameter metadata,
    result package metadata, required permissions, and backing content item id.
  - Parameter and result-package metadata are derived from the current workflow
    definition.
- `rollbackContentItem(item, versionId)` returns the updated
  `PublishedWorkflowContentItem` with the active version moved to `versionId`
  and rollback availability recalculated.

## Validation Scope

The current validation pass checks the contract shape before run or publication:

- Required workflow identifiers, names, step ids, and non-empty step lists.
- Valid JSON that is not a workflow definition is surfaced as blocked contract
  validation instead of rendering graph nodes.
- Duplicate step ids, unknown dependencies, missing binding dependencies, and
  dependency cycles.
- Canonical analysis-plan presence and plan-step dependency references.
- Cron triggers must include a non-empty five-field cron expression.
- Process nodes must use Console-advertised process ids:
  `geometry.buffer`, `geometry.clip`, `analytics.summarize`, or
  `conversion.export`.
- Supported processes must include their required input parameters.
- Catalog sinks must declare produced item kind, external object-store sinks
  require explicit publish permission, and process-service publication reports a
  permission warning when submit-time permission is pending.
- Batch and process-service publication controls remain disabled until the
  current definition has a non-blocked validation result.

## Usage Notes

- The current app mounts the Studio workflow editor inside the unified Console
  shell with Studio active.
- Draft generation, validation, dry-run, sample-run, batch publication, process
  service publication, and rollback are explicit builder actions.
- The fixture transport is deterministic and browser-local. Server-backed
  wiring should preserve the same response contract while moving validation,
  job execution, content versioning, RBAC, provenance, and OGC route ownership
  to the server and SDK clients.
- The editor performs no workflow network fetch on Console startup. Future
  server integration should keep draft, validation, run, and publish requests
  action-scoped to avoid startup and preview waterfalls.

## Targeted Validation

- `npm run typecheck`
- `npm run test -- src/studio/workflows/workflowEditorModel.test.ts src/studio/workflows/StudioWorkflowEditor.test.tsx`
- `npm run build`
