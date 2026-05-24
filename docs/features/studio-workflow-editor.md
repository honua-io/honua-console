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

The TypeScript payloads in `src/studio/workflows/types.ts` are Console editor
view models that mirror and decorate this boundary; they are not the server
DTOs. The fixture adapts the editor view model to a narrow server
`WorkflowDefinition` wire shape before hashing publication provenance. These
structural types should be replaced by imported server or SDK DTOs where those
shared contracts are available; Console should not fork the protocol.

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
  - Carries an inspectable editor `definition` with `workflowId`, `name`,
    editor `mode`, `steps`, optional `trigger`, timestamps, and metadata.
  - Each workflow step carries `nodeKind`, canonical `AnalysisPlan` payload,
    inputs, dependencies, input bindings, failure policy, and optional retry or
    timeout metadata.
  - The fixture adapter strips editor-only fields before server-shaped
    publication provenance is produced.
- `validateDefinition(definition)` accepts parsed JSON and returns
  `WorkflowValidationResult`; syntactically valid non-workflow JSON returns
  blocked contract issues instead of reaching graph rendering.
  - Shape validation accepts `unknown` input at the transport boundary and only
    promotes values that match the Console workflow editor view model to
    graph, run, or publication actions.
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
    links.
  - The transport revalidates before execution; a semantically blocked
    definition returns a failed run with a `WorkflowValidationBlocked` snapshot
    and no artifacts.
- `publishDefinition(definition, request)` returns
  `PublishedWorkflowContentItem`.
  - The request selects `manual` or `scheduled` execution and may include a
    cron expression and time zone.
  - Publication rejects definitions with blocked validation results.
  - Scheduled publication requires a non-empty valid numeric five-field cron
    expression using the server scheduler's POSIX subset: wildcard, values,
    lists, ranges, and positive steps across minute, hour, day-of-month, month,
    and day-of-week. Empty comma-list entries, signed values, named
    month/day tokens, question marks, tab-separated fields, and out-of-range
    values are rejected.
  - The response includes content item identity, `workflow-definition` kind,
    execution modes, optional schedule, versions, active version, provenance
    hash, upstream item references, and run-history link.
- `publishProcessService(definition)` returns `ProcessServicePublication`.
  - Blocked definitions are rejected.
  - Eligibility is derived from the current definition, not the original draft:
    a publication node must opt into process-service publication, bind an
    upstream result package, carry process-capable plan metadata, and have
    publish-process permission.
  - The current definition must also yield at least one invokable process
    parameter after Console filters internal sink, binding, and publication
    inputs from the metadata projection.
  - Successful responses include a stable
    `/ogc/processes/processes/{processId}/execution` route, parameter metadata,
    result package metadata, required permissions, and backing content item id.
  - Parameter and result-package metadata are derived from the current workflow
    definition.
- `rollbackContentItem(item, versionId)` returns the updated
  `PublishedWorkflowContentItem` with the active version moved to `versionId`,
  rollback availability recalculated, and title, provenance, upstream item
  references, definition hash, and manual/scheduled state restored from the
  target version.

## Validation Scope

The current validation pass checks the contract shape before run or publication:

- Required workflow identifiers, names, step ids, and non-empty step lists.
- Valid JSON that is not a workflow definition is surfaced as blocked contract
  validation instead of rendering graph nodes.
- Structural fields use the shared contract vocabulary: workflow mode
  `etl`, `geoprocessing`, or `hybrid`; trigger kind `Manual` or `Cron`; step
  node kinds `source`, `transform`, `sink`, `process`, `parameter`,
  `validation`, `artifact`, or `publication`; analysis plan step kinds
  `QueryFeatures`, `Geoprocess`, `Aggregate`, `RenderMap`, or `Export`; and
  artifact kinds from the SDK/server projection.
- Duplicate step ids, unknown dependencies, missing binding dependencies, and
  dependency cycles.
- Input binding artifact selectors must use the server resolver forms
  `artifact:{index}` or `artifact:{label}`.
- Canonical analysis-plan presence and plan-step dependency references.
- Cron triggers must include a non-empty valid numeric five-field cron
  expression using the scheduler subset documented above.
- Cron trigger time zones must resolve through the server scheduler's IANA time
  zone support; blank values default to UTC.
- Retry policies must allow at least one whole-number attempt and use a
  positive whole-number backoff interval.
- Step timeouts must be positive whole-number seconds when present.
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
- Editing the generated definition clears prior validation and process-service
  publication state; publication controls remain tied to the latest parsed and
  validated definition.
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
