import { describe, expect, it } from "vitest";

import { createStudioWorkflowFixtureClient } from "./fixtureClient";
import {
  isFiveFieldCron,
  stableDefinitionHash,
  toServerWorkflowDefinitionPayload,
  validateWorkflowDefinition,
} from "./workflowContracts";

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

  it("returns contract issues for syntactically valid non-workflow JSON", async () => {
    const client = createStudioWorkflowFixtureClient();

    const validation = await client.validateDefinition({});

    expect(validation.status).toBe("blocked");
    expect(validation.issues.map((issue) => issue.message)).toEqual(
      expect.arrayContaining([
        "Workflow definition must declare workflowId as a string.",
        "Workflow steps must be an array.",
      ]),
    );
  });

  it("adapts the Console editor view model to the server workflow shape", async () => {
    const client = createStudioWorkflowFixtureClient();
    const draft = await client.createDraftFromPrompt("Publish workflow");

    const serverDefinition = toServerWorkflowDefinitionPayload(draft.definition);
    const [firstStep] = serverDefinition.steps;
    const editorOnlyDefinition = {
      ...draft.definition,
      steps: draft.definition.steps.map((step) => ({ ...step, label: `${step.label} edited` })),
    };

    expect(serverDefinition).not.toHaveProperty("mode");
    expect(firstStep).not.toHaveProperty("label");
    expect(firstStep).not.toHaveProperty("nodeKind");
    expect(firstStep).not.toHaveProperty("processId");
    expect(firstStep).not.toHaveProperty("inputs");
    expect(serverDefinition.steps.at(-1)?.failurePolicy).toBe("Skip");
    expect(stableDefinitionHash(editorOnlyDefinition)).toBe(stableDefinitionHash(draft.definition));
  });

  it("blocks invalid cron trigger expressions before run or publication", async () => {
    const client = createStudioWorkflowFixtureClient();
    const draft = await client.createDraftFromPrompt("Publish workflow");
    const invalidCronDefinition = {
      ...draft.definition,
      trigger: {
        kind: "Cron" as const,
        enabled: true,
        cronExpression: "99 99 99 99 99",
        timeZone: "Pacific/Honolulu",
      },
    };

    const validation = validateWorkflowDefinition(invalidCronDefinition);
    const run = await client.runDefinition(invalidCronDefinition, "dry-run");

    expect(validation.status).toBe("blocked");
    expect(validation.issues.map((issue) => issue.message)).toContain("Cron trigger must use a valid 5-field expression.");
    expect(run.status).toBe("failed");
    expect(run.logs.map((entry) => entry.message)).toContain("Cron trigger must use a valid 5-field expression.");
  });

  it("blocks retry and timeout policies that cannot execute safely", async () => {
    const client = createStudioWorkflowFixtureClient();
    const draft = await client.createDraftFromPrompt("Publish workflow");
    const invalidExecutionPolicyDefinition = {
      ...draft.definition,
      steps: draft.definition.steps.map((step, index) =>
        index === 0
          ? {
              ...step,
              retryPolicy: {
                maxAttempts: 0,
                backoffSeconds: 0,
              },
              timeoutSeconds: 0,
            }
          : step,
      ),
    };

    const validation = await client.validateDefinition(invalidExecutionPolicyDefinition);

    expect(validation.status).toBe("blocked");
    expect(validation.issues.map((issue) => issue.message)).toContain(
      "Step 'source-permits' retry policy must allow at least one attempt.",
    );
    expect(validation.issues.map((issue) => issue.message)).toContain(
      "Step 'source-permits' retry policy must use a positive backoff interval.",
    );
    expect(validation.issues.map((issue) => issue.message)).toContain(
      "Step 'source-permits' timeout must be greater than zero seconds.",
    );
  });

  it("validates the server scheduler cron subset", () => {
    expect(isFiveFieldCron("*/15 0-23/2 * 1,6 0,7")).toBe(true);
    expect(isFiveFieldCron("99 99 99 99 99")).toBe(false);
    expect(isFiveFieldCron("0 2 ? * MON")).toBe(false);
    expect(isFiveFieldCron("0 2 1,,2 * *")).toBe(false);
    expect(isFiveFieldCron("0 +2 * * *")).toBe(false);
    expect(isFiveFieldCron("0\t2 * * *")).toBe(false);
    expect(isFiveFieldCron("0 2 *")).toBe(false);
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
    expect(rolledBack.executionModes).toEqual(["manual"]);
    expect(rolledBack.schedule).toBeUndefined();
    expect(rolledBack.versions.find((version) => version.versionId === manual.activeVersionId)?.rollbackAvailable).toBe(
      false,
    );
    expect(rolledBack.versions.find((version) => version.versionId === scheduled.activeVersionId)?.rollbackAvailable).toBe(
      true,
    );
  });

  it("reconciles manual republish and scheduled rollback state", async () => {
    const client = createStudioWorkflowFixtureClient();
    const draft = await client.createDraftFromPrompt("Publish workflow");

    const firstManual = await client.publishDefinition(draft.definition, { executionMode: "manual" });
    const scheduled = await client.publishDefinition(draft.definition, {
      executionMode: "scheduled",
      cronExpression: "0 2 * * *",
      timeZone: "Pacific/Honolulu",
    });
    const nextManual = await client.publishDefinition(draft.definition, { executionMode: "manual" });
    const rolledBackToScheduled = await client.rollbackContentItem(nextManual, scheduled.activeVersionId);

    expect(firstManual.executionModes).toEqual(["manual"]);
    expect(firstManual.schedule).toBeUndefined();
    expect(nextManual.executionModes).toEqual(["manual"]);
    expect(nextManual.schedule).toBeUndefined();
    expect(rolledBackToScheduled.executionModes).toEqual(["manual", "scheduled"]);
    expect(rolledBackToScheduled.schedule?.cronExpression).toBe("0 2 * * *");
    expect(
      rolledBackToScheduled.versions.find((version) => version.versionId === scheduled.activeVersionId)
        ?.rollbackAvailable,
    ).toBe(false);
  });

  it("blocks invalid batch publication and invalid scheduled publication requests", async () => {
    const client = createStudioWorkflowFixtureClient();
    const draft = await client.createDraftFromPrompt("Publish workflow");
    const invalidDefinition = {
      ...draft.definition,
      steps: [],
    };

    await expect(client.publishDefinition(invalidDefinition, { executionMode: "manual" })).rejects.toThrow(
      /Workflow validation blocked publication/,
    );
    await expect(
      client.publishDefinition(draft.definition, {
        executionMode: "scheduled",
        cronExpression: "0 2 *",
        timeZone: "Pacific/Honolulu",
      }),
    ).rejects.toThrow(/5-field cron expression/);
    await expect(
      client.publishDefinition(draft.definition, {
        executionMode: "scheduled",
        cronExpression: "99 99 99 99 99",
        timeZone: "Pacific/Honolulu",
      }),
    ).rejects.toThrow(/valid 5-field cron expression/);
  });

  it("derives process-service eligibility and metadata from the current workflow definition", async () => {
    const client = createStudioWorkflowFixtureClient();
    const draft = await client.createDraftFromPrompt("Publish workflow");
    const editedDefinition = {
      ...draft.definition,
      steps: draft.definition.steps.map((step) =>
        step.stepId === "buffer-habitats"
          ? {
              ...step,
              inputs: {
                ...step.inputs,
                distanceMeters: "750",
              },
              plan: {
                ...step.plan,
                steps: step.plan.steps.map((planStep) =>
                  planStep.stepId === "buffer"
                    ? {
                        ...planStep,
                        inputs: {
                          ...planStep.inputs,
                          distanceMeters: "750",
                        },
                      }
                    : planStep,
                ),
              },
            }
          : step,
      ),
    };
    const etlOnlyDefinition = {
      ...draft.definition,
      steps: draft.definition.steps.filter((step) => step.nodeKind !== "publication"),
    };

    const service = await client.publishProcessService(editedDefinition);

    expect(service.parameterMetadata.find((parameter) => parameter.name === "distanceMeters")?.defaultValue).toBe("750");
    await expect(client.publishProcessService(etlOnlyDefinition)).rejects.toThrow(/publication node/);
  });
});
