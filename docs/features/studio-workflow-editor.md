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

## Targeted Validation

- `npm run typecheck`
- `npm run test -- src/studio/workflows/workflowEditorModel.test.ts src/studio/workflows/StudioWorkflowEditor.test.tsx`
- `npm run build`
