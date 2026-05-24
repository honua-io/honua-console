import { describe, expect, it } from "vitest";

import { createStudioWorkflowFixtureClient } from "./fixtureClient";

describe("Studio workflow editor model", () => {
  it("creates an inspectable hybrid ETL and GP draft from natural language", async () => {
    const client = createStudioWorkflowFixtureClient();

    const draft = await client.createDraftFromPrompt("Buffer habitat permits and publish a scheduled review package");

    expect(draft.generatedContract).toContain("honua-server:WorkflowDefinition");
    expect(draft.generatedContract).toContain("honua-sdk:IJobRun");
    expect(draft.definition.mode).toBe("hybrid");
    expect(draft.definition.steps.map((step) => step.nodeKind)).toEqual([
      "source",
      "process",
      "transform",
      "sink",
      "publication",
    ]);
    expect(JSON.stringify(draft.definition)).toContain("geometry.buffer");
  });

  it("surfaces missing parameters, unsupported transforms, sink constraints, and permissions", async () => {
    const client = createStudioWorkflowFixtureClient();
    const draft = await client.createDraftFromPrompt("Build a broken workflow");
    const [source, process, transform, sink] = draft.definition.steps;
    const broken = {
      ...draft.definition,
      steps: [
        source,
        {
          ...process,
          inputs: { inputLayerId: "protected-habitats" },
          plan: {
            ...process.plan,
            steps: [
              {
                ...process.plan.steps[0],
                inputs: { inputLayerId: "protected-habitats" },
              },
            ],
          },
        },
        {
          ...transform,
          processId: "third-party.unsupported",
        },
        {
          ...sink,
          inputs: {
            sinkType: "external-s3",
            inputLayerId: "flag-intersections.outputs.FeatureLayer",
            format: "geoparquet",
          },
        },
      ],
    };

    const validation = await client.validateDefinition(broken);

    expect(validation.status).toBe("blocked");
    expect(validation.issues.map((issue) => issue.kind)).toEqual(
      expect.arrayContaining(["missing-parameter", "unsupported-transform", "permission"]),
    );
  });

  it("runs dry-runs through the SDK job-runner surface and returns logs, artifacts, and row failures", async () => {
    const client = createStudioWorkflowFixtureClient();
    const draft = await client.createDraftFromPrompt("Run sample review");

    const run = await client.runDefinition(draft.definition, "dry-run");

    expect(run.status).toBe("successful");
    expect(run.snapshots.map((snapshot) => snapshot.status)).toEqual(["accepted", "running", "successful"]);
    expect(run.logs.some((entry) => entry.message.includes("Honua ProcessService job runner"))).toBe(true);
    expect(run.artifacts.map((artifact) => artifact.kind)).toEqual(["FeatureLayer", "Table", "Report"]);
    expect(run.featureFailures).toHaveLength(3);
    expect(run.provenanceLinks).toContain("/operate/jobs/workflow-run-1/logs");
  });

  it("publishes reusable batch definitions, scheduled definitions, process services, and rollback metadata", async () => {
    const client = createStudioWorkflowFixtureClient();
    const draft = await client.createDraftFromPrompt("Publish workflow");

    const manual = await client.publishDefinition(draft.definition, { executionMode: "manual" });
    const scheduled = await client.publishDefinition(draft.definition, {
      executionMode: "scheduled",
      cronExpression: "0 2 * * *",
      timeZone: "Pacific/Honolulu",
    });
    const service = await client.publishProcessService(draft.definition);
    const rolledBack = await client.rollbackContentItem(scheduled, manual.activeVersionId);

    expect(manual.contentKind).toBe("workflow-definition");
    expect(scheduled.executionModes).toEqual(["manual", "scheduled"]);
    expect(scheduled.schedule?.cronExpression).toBe("0 2 * * *");
    expect(scheduled.provenance.definitionHash).toMatch(/^wf-/);
    expect(scheduled.runHistoryHref).toContain("/runs");
    expect(service.stableInvocationRoute).toContain("/ogc/processes/processes/");
    expect(service.resultPackageMetadata.artifactKinds).toContain("FeatureLayer");
    expect(rolledBack.activeVersionId).toBe(manual.activeVersionId);
  });
});
