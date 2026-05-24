# Studio Workflow Editor Smoke

Use this targeted smoke for honua-console#17 until server-backed Console
previews are wired.

## Automated Smoke

Run:

```sh
npm run test -- src/studio/workflows/workflowEditorModel.test.ts src/studio/workflows/StudioWorkflowEditor.test.tsx
```

This covers prompt-to-draft, definition inspection, malformed-but-valid JSON
contract failures, validation failures, SDK-shaped dry-run history, artifacts,
rejected rows, scheduled publication gating, process-service eligibility,
current-definition service metadata, and rollback metadata.

## Manual Smoke

1. Start Console with `npm run dev -- --host 127.0.0.1`.
2. Open the Vite URL. The current scaffold renders the Studio workflow editor
   as the active Console area.
3. Select `Generate Draft`.
4. Confirm the generated definition JSON contains `source`, `process`,
   `transform`, `sink`, and `publication` nodes.
5. Select `Validate` and confirm the validation panel reports `valid`.
6. Select `Dry Run` or `Sample Run`.
7. Confirm run history shows `accepted -> running -> successful`, logs,
   artifacts, rejected rows, and provenance links.
8. Enable scheduled execution, keep `0 2 * * *`, and select
   `Publish Batch Definition`.
9. Confirm the published content item shows manual and scheduled modes,
   versions, rollback controls, provenance hash, and run-history link.
10. Select `Publish Process Service`.
11. Confirm the process route uses `/ogc/processes/processes/.../execution`
    and includes parameter, result package, and permission metadata.

Publication controls should stay disabled until the current definition has a
non-blocked validation result. Scheduled publication should remain disabled for
cron input that is not a valid five-field expression, including out-of-range
five-field values such as `99 99 99 99 99`, malformed comma lists such as
`0 2 1,,2 * *`, and unsupported scheduler syntax such as `0 2 ? * MON`.

To smoke the contract guardrails, replace the generated definition JSON with
`{}` and select `Validate`. The graph should stay empty, validation should
report blocked contract issues such as a missing `workflowId`, and publication
buttons should stay disabled. Restore or regenerate the draft before continuing.

To smoke current-definition process-service eligibility, remove the
`publication` node or remove its `resultPackageId` binding, validate the edited
definition, and confirm `Publish Process Service` stays disabled. Restore or
regenerate the draft before publication smoke.

## Expected Contract Evidence

- Draft summary lists `honua-server:WorkflowDefinition`,
  `honua-server:AnalysisPlan`, `honua-sdk:IJobRun`, and `ogc-api-processes`.
- Run snapshots display `accepted -> running -> successful`.
- Artifacts include a feature layer, rejected-row table, and report.
- Published workflow content shows `workflow-definition`, manual/scheduled
  modes, a `wf-` definition hash, version controls, provenance, and run-history
  href.
- Process-service metadata is derived from the edited workflow definition and
  excludes internal sink, binding, and publication-only inputs.
