import { strict as assert } from "node:assert";
import { describe, test } from "node:test";

import { WORKFLOW_SCENARIO_STEPS, runWorkflowSmoke } from "../workflow.mjs";

const ORIGIN = "http://127.0.0.1:4174";

describe("Studio workflow smoke", () => {
  test("dry-run to publish to Operate monitor chain records workflow evidence", async () => {
    const { report } = await runWorkflowSmoke({ originUrl: ORIGIN });

    assert.equal(report.result, "ok", `workflow smoke failed: ${report.failure?.message ?? "unknown"}`);
    assert.equal(report.failure, null);
    assert.equal(report.steps.length, WORKFLOW_SCENARIO_STEPS.length);
    for (const [i, step] of report.steps.entries()) {
      assert.equal(step.id, WORKFLOW_SCENARIO_STEPS[i].id);
      assert.equal(step.status, "ok", `${step.id} ended in ${step.status}: ${step.error}`);
    }

    assert.equal(report.items.workflowDraftId, "wf-draft-parcel-etl");
    assert.equal(report.items.workflowContentItemId, "workflow-parcel-nightly-normalizer");
    assert.equal(report.items.workflowVersionId, "workflow-parcel-nightly-normalizer:v2");
    assert.equal(report.items.workflowDryRunJobId, "job-dryrun-0001");
    assert.equal(report.items.workflowPublishJobId, "job-publish-0002");
    assert.equal(report.items.workflowPublicationId, "pub-parcel-nightly-normalizer-002");

    const draft = report.steps.find((step) => step.id === "console/studio-workflow-draft");
    assert.deepEqual(draft.evidence.nodeCategories.sort(), ["sink", "source", "transform"]);
    assert.ok(draft.evidence.failureEdges.length >= 1, "failure edges must be visible before publish");
    assert.ok(draft.evidence.outputSchemas.length >= 1, "output schemas must be visible before publish");
    assert.equal(draft.evidence.publicationIntent.mode, "process-endpoint");
    assert.equal(draft.evidence.publicationIntent.exposeInvocationEndpoint, true);

    const dryRun = report.steps.find((step) => step.id === "server/workflow-dry-run");
    assert.equal(dryRun.evidence.status, "succeeded");
    assert.ok(dryRun.evidence.artifactCount >= 1);
    assert.ok(dryRun.evidence.outputSchemas.includes("validated_parcels"));

    const publish = report.steps.find((step) => step.id === "server/workflow-publish");
    assert.equal(publish.evidence.status, "queued");
    assert.ok(publish.evidence.invocationEndpoint.endsWith("/invoke"));
    assert.ok(publish.evidence.parameterValidation.every((parameter) => parameter.valid));

    for (const value of Object.values(report.urls)) {
      assert.equal(new URL(value).origin, new URL(ORIGIN).origin);
    }
    assert.ok(report.urls.workflowEditor.endsWith("/studio/workflows/wf-draft-parcel-etl"));
    assert.ok(report.urls.publishJob.endsWith("/operate/jobs/job-publish-0002"));
    assert.ok(report.urls.publishEvents.endsWith("/operate/events?jobId=job-publish-0002"));
  });

  test("workflow contract versions are captured in evidence", async () => {
    const { report } = await runWorkflowSmoke({ originUrl: ORIGIN });
    const contractNames = report.contractVersions.map((contract) => contract.name).sort();

    assert.deepEqual(contractNames, [
      "build-artifact",
      "workflow-dry-run",
      "workflow-package",
      "workflow-publication",
    ]);
  });

  test("missing failure edges are attributed to the Console draft step", async () => {
    const customSteps = WORKFLOW_SCENARIO_STEPS.map((step) =>
      step.id === "console/studio-workflow-draft"
        ? {
            ...step,
            async run(ctx) {
              const result = await step.run(ctx);
              ctx.workflowPackage.edges = ctx.workflowPackage.edges.filter((edge) => edge.kind !== "failure");
              if (!ctx.workflowPackage.edges.some((edge) => edge.kind === "failure")) {
                throw new Error("workflow.package must expose failure edges before publish");
              }
              return result;
            },
          }
        : step,
    );

    const { report } = await runWorkflowSmoke({ originUrl: ORIGIN, steps: customSteps });
    assert.equal(report.result, "failed");
    assert.equal(report.failure.stepId, "console/studio-workflow-draft");
    assert.equal(report.failure.owningLayer, "console");
    assert.match(report.failure.message, /failure edges/);
  });
});
