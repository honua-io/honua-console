import type {
  GeospatialGrpcCancelJobResponse,
  GeospatialGrpcGetJobResponse,
  GeospatialGrpcGetJobResultResponse,
  GeospatialGrpcProcessClient,
  GeospatialGrpcSubmitJobResponse,
  HonuaProcessExecuteRequest,
} from "@honua/sdk-js/honua";
import { createGeospatialGrpcProcessAdapter, createHonuaProcessRunner } from "@honua/sdk-js/honua";
import type { JobSnapshot, JobStatus } from "@honua/sdk-js/contract";

import type {
  ArtifactKind,
  ProcessServicePublication,
  PublishedWorkflowContentItem,
  StudioWorkflowTransport,
  WorkflowDefinitionPayload,
  WorkflowDraft,
  WorkflowExecutionResult,
  WorkflowPublicationRequest,
  WorkflowRunLogEntry,
  WorkflowRunMode,
  WorkflowRunRecord,
} from "./types";
import {
  describeProcessServicePublication,
  isFiveFieldCron,
  stableDefinitionHash,
  toProcessExecutionPlan,
  validateWorkflowDefinition,
  WORKFLOW_CONTRACT_REFERENCES,
} from "./workflowContracts";

const DEFAULT_PROMPT =
  "Import shoreline permits from CSV, buffer protected habitats by 500 meters, flag intersecting permits, and publish a scheduled review package.";

export function createStudioWorkflowFixtureClient(): StudioWorkflowTransport {
  const jobClient = new FixtureGeospatialProcessClient();
  const publishedItems = new Map<string, PublishedWorkflowContentItem>();

  return {
    async createDraftFromPrompt(prompt: string): Promise<WorkflowDraft> {
      const effectivePrompt = prompt.trim() || DEFAULT_PROMPT;
      return createDraft(effectivePrompt);
    },
    async validateDefinition(definition: unknown) {
      return validateWorkflowDefinition(definition);
    },
    async runDefinition(definition: WorkflowDefinitionPayload, mode: WorkflowRunMode) {
      const validation = validateWorkflowDefinition(definition);
      if (validation.status === "blocked") {
        return blockedRun(definition, mode, validation.issues.map((issue) => issue.message));
      }

      const request: HonuaProcessExecuteRequest = {
        plan: toProcessExecutionPlan(definition, mode),
        context: {
          workspaceId: "console-studio-workflows",
          metadata: {
            "honua.console.workflowId": definition.workflowId,
            "honua.console.runMode": mode,
            "honua.console.definitionHash": stableDefinitionHash(definition),
          },
        },
      };
      const runner = createHonuaProcessRunner(createGeospatialGrpcProcessAdapter(jobClient));
      const job = await runner.execute<WorkflowExecutionResult>(request);
      const snapshots: JobSnapshot<WorkflowExecutionResult>[] = [{ status: job.status, progress: job.progress }];

      while (!isTerminal(job.status)) {
        snapshots.push(await job.poll());
      }

      const terminal = snapshots.at(-1);
      const result = terminal?.result?.outputs.result;
      return {
        runId: job.id,
        workflowId: definition.workflowId,
        mode,
        status: terminal?.status ?? job.status,
        startedAt: new Date(Date.now() - 1_200).toISOString(),
        completedAt: terminal && isTerminal(terminal.status) ? new Date().toISOString() : undefined,
        snapshots,
        logs: [
          log("info", `Queued ${mode} on Honua ProcessService job runner.`),
          log("info", "Validated source bindings and transform inputs before worker admission."),
          log("warning", "Sample run capped at 250 features; rejected rows are retained as artifacts."),
          log("info", `Job ${job.id} reached ${terminal?.status ?? job.status}.`),
        ],
        artifacts: result?.artifacts ?? [],
        featureFailures: result?.featureFailures ?? [],
        provenanceLinks: result?.provenanceLinks ?? [],
      };
    },
    async publishDefinition(definition: WorkflowDefinitionPayload, request: WorkflowPublicationRequest) {
      assertPublishableDefinition(definition);
      assertPublishableSchedule(request);

      const now = new Date().toISOString();
      const hash = stableDefinitionHash(definition);
      const itemId = `workflow-item-${definition.workflowId}`;
      const existing = publishedItems.get(itemId);
      const versions = [
        ...(existing?.versions ?? []),
        {
          versionId: `v${(existing?.versions.length ?? 0) + 1}`,
          createdAt: now,
          summary: request.executionMode === "scheduled" ? "Scheduled workflow definition" : "Manual workflow definition",
          rollbackAvailable: Boolean(existing),
        },
      ];
      const item: PublishedWorkflowContentItem = {
        itemId,
        title: definition.name,
        contentKind: "workflow-definition",
        workflowId: definition.workflowId,
        href: `/catalog/items/${itemId}`,
        executionModes:
          request.executionMode === "scheduled" ? ["manual", "scheduled"] : existing?.executionModes ?? ["manual"],
        schedule:
          request.executionMode === "scheduled"
            ? {
                kind: "Cron",
                cronExpression: request.cronExpression ?? "0 2 * * *",
                timeZone: request.timeZone ?? "Pacific/Honolulu",
                enabled: true,
              }
            : existing?.schedule,
        versions,
        activeVersionId: versions.at(-1)?.versionId ?? "v1",
        provenance: {
          createdBy: "studio:workflow-editor",
          sourcePrompt: definition.metadata.sourcePrompt ?? DEFAULT_PROMPT,
          sourceDraftId: definition.metadata.sourceDraftId ?? "draft-fixture",
          definitionHash: hash,
          upstreamItems: collectUpstreamItems(definition),
          runHistoryHref: `/studio/workflows/${definition.workflowId}/runs`,
        },
        runHistoryHref: `/studio/workflows/${definition.workflowId}/runs`,
      };
      publishedItems.set(itemId, item);
      return item;
    },
    async publishProcessService(definition: WorkflowDefinitionPayload) {
      assertPublishableDefinition(definition);
      const publication = describeProcessServicePublication(definition);
      if (!publication.eligibility.eligible) {
        throw new Error(`Workflow is not eligible for process-service publication: ${publication.eligibility.reasons.join(" ")}`);
      }

      const itemId = `workflow-item-${definition.workflowId}`;
      return {
        processId: `studio.${definition.workflowId}`,
        title: definition.name,
        stableInvocationRoute: `/ogc/processes/processes/studio.${definition.workflowId}/execution`,
        parameterMetadata: publication.parameterMetadata,
        resultPackageMetadata: publication.resultPackageMetadata,
        permissions: ["workflow:execute", "workflow:read", "process:invoke"],
        contentItemId: itemId,
      } satisfies ProcessServicePublication;
    },
    async rollbackContentItem(item: PublishedWorkflowContentItem, versionId: string) {
      if (!item.versions.some((version) => version.versionId === versionId)) {
        throw new Error(`Unknown workflow content version '${versionId}'.`);
      }
      const rolledBack: PublishedWorkflowContentItem = {
        ...item,
        activeVersionId: versionId,
        versions: item.versions.map((version) => ({
          ...version,
          rollbackAvailable: version.versionId !== versionId,
        })),
      };
      publishedItems.set(item.itemId, rolledBack);
      return rolledBack;
    },
  };
}

function createDraft(prompt: string): WorkflowDraft {
  const now = new Date().toISOString();
  const safeId = prompt
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, "-")
    .replace(/^-|-$/g, "")
    .slice(0, 36);
  const workflowId = `studio-${safeId || "workflow-draft"}`;
  const draftId = `draft-${workflowId}`;
  const definition: WorkflowDefinitionPayload = {
    workflowId,
    name: "Shoreline permit habitat review",
    description: "ETL and geoprocessing workflow generated from Studio natural language.",
    mode: "hybrid",
    createdAt: now,
    updatedAt: now,
    metadata: {
      sourcePrompt: prompt,
      sourceDraftId: draftId,
      contractSource: "console-studio-workflow-view-model",
      serverContractSource: "honua-server:WorkflowDefinition",
      contentType: "honua.workflow.definition",
    },
    trigger: {
      kind: "Manual",
      enabled: true,
    },
    steps: [
      {
        stepId: "source-permits",
        label: "Load shoreline permit CSV",
        nodeKind: "source",
        processId: "conversion.export",
        inputs: {
          sourceUri: "s3://honua-samples/shoreline-permits.csv",
          format: "csv",
          inputLayerId: "shoreline-permits-staging",
          schemaPolicy: "infer-and-review",
        },
        dependsOn: [],
        inputBindings: [],
        failurePolicy: "Fail",
        timeoutSeconds: 300,
        plan: {
          planId: "plan-source-permits",
          intentId: draftId,
          outputs: ["Table"],
          warnings: ["CSV geometry columns will be sampled before worker admission."],
          steps: [
            {
              stepId: "read-csv",
              kind: "QueryFeatures",
              inputs: {
                sourceUri: "s3://honua-samples/shoreline-permits.csv",
                format: "csv",
              },
              dependsOn: [],
            },
          ],
        },
      },
      {
        stepId: "buffer-habitats",
        label: "Buffer protected habitats",
        nodeKind: "process",
        processId: "geometry.buffer",
        inputs: {
          inputLayerId: "protected-habitats",
          distanceMeters: "500",
          units: "meters",
        },
        dependsOn: ["source-permits"],
        inputBindings: [
          {
            sourceStepId: "source-permits",
            sourceArtifactSelector: "outputs.table",
            targetInputKey: "candidatePermits",
          },
        ],
        retryPolicy: {
          maxAttempts: 2,
          backoffSeconds: 30,
        },
        failurePolicy: "Fail",
        timeoutSeconds: 900,
        plan: {
          planId: "plan-buffer-habitats",
          intentId: draftId,
          outputs: ["FeatureLayer"],
          warnings: [],
          steps: [
            {
              stepId: "buffer",
              kind: "Geoprocess",
              processId: "geometry.buffer",
              inputs: {
                inputLayerId: "protected-habitats",
                distanceMeters: "500",
                units: "meters",
              },
              dependsOn: [],
            },
          ],
        },
      },
      {
        stepId: "flag-intersections",
        label: "Flag permits intersecting habitat buffers",
        nodeKind: "transform",
        processId: "geometry.clip",
        inputs: {
          inputLayerId: "shoreline-permits-staging",
          clipLayerId: "buffer-habitats.outputs.FeatureLayer",
        },
        dependsOn: ["source-permits", "buffer-habitats"],
        inputBindings: [
          {
            sourceStepId: "source-permits",
            sourceArtifactSelector: "outputs.table",
            targetInputKey: "inputLayerId",
          },
          {
            sourceStepId: "buffer-habitats",
            sourceArtifactSelector: "outputs.FeatureLayer",
            targetInputKey: "clipLayerId",
          },
        ],
        failurePolicy: "Fail",
        timeoutSeconds: 900,
        plan: {
          planId: "plan-flag-intersections",
          intentId: draftId,
          outputs: ["FeatureLayer", "Table"],
          warnings: [],
          steps: [
            {
              stepId: "clip",
              kind: "Geoprocess",
              processId: "geometry.clip",
              inputs: {
                inputLayerId: "shoreline-permits-staging",
                clipLayerId: "buffer-habitats.outputs.FeatureLayer",
              },
              dependsOn: [],
            },
          ],
        },
      },
      {
        stepId: "publish-review-package",
        label: "Publish review package",
        nodeKind: "sink",
        processId: "conversion.export",
        inputs: {
          inputLayerId: "flag-intersections.outputs.FeatureLayer",
          format: "geoparquet",
          sinkType: "catalog-content-item",
          itemKind: "package",
          retentionPolicy: "honua.retention.result-package.default",
        },
        dependsOn: ["flag-intersections"],
        inputBindings: [
          {
            sourceStepId: "flag-intersections",
            sourceArtifactSelector: "outputs.FeatureLayer",
            targetInputKey: "inputLayerId",
          },
        ],
        failurePolicy: "Fail",
        timeoutSeconds: 600,
        plan: {
          planId: "plan-publish-review-package",
          intentId: draftId,
          outputs: ["File", "Report"],
          warnings: [],
          steps: [
            {
              stepId: "export-package",
              kind: "Export",
              processId: "conversion.export",
              inputs: {
                inputLayerId: "flag-intersections.outputs.FeatureLayer",
                format: "geoparquet",
              },
              dependsOn: [],
            },
          ],
        },
      },
      {
        stepId: "process-service-binding",
        label: "Expose reusable review process",
        nodeKind: "publication",
        processId: "analytics.summarize",
        inputs: {
          inputLayerId: "flag-intersections.outputs.FeatureLayer",
          groupByField: "permit_status",
          publishAsProcessService: "true",
          permission: "granted",
        },
        dependsOn: ["publish-review-package"],
        inputBindings: [
          {
            sourceStepId: "publish-review-package",
            sourceArtifactSelector: "outputs.resultPackage",
            targetInputKey: "resultPackageId",
          },
        ],
        failurePolicy: "Skip",
        timeoutSeconds: 300,
        plan: {
          planId: "plan-process-service-binding",
          intentId: draftId,
          outputs: ["Scalar", "Report"],
          warnings: [],
          steps: [
            {
              stepId: "summarize-results",
              kind: "Aggregate",
              processId: "analytics.summarize",
              inputs: {
                inputLayerId: "flag-intersections.outputs.FeatureLayer",
                groupByField: "permit_status",
              },
              dependsOn: [],
            },
          ],
        },
      },
    ],
  };

  return {
    draftId,
    prompt,
    generatedAt: now,
    definition,
    generatedContract: [...WORKFLOW_CONTRACT_REFERENCES],
    eligibleProcessService: true,
    warnings: ["Review inferred CSV geometry columns before sample execution."],
    explanation: [
      "Studio classified the request as a hybrid GeoETL and geoprocessing workflow.",
      "The generated definition is inspectable before any dry-run, sample-run, or publication action.",
      "Execution is submitted through the SDK ProcessService job-runner adapter, not a Console runtime.",
    ],
  };
}

class FixtureGeospatialProcessClient implements GeospatialGrpcProcessClient {
  readonly #jobs = new Map<
    string,
    {
      readonly request: { readonly plan: unknown; readonly context?: unknown };
      polls: number;
      status: JobStatus;
    }
  >();
  #counter = 0;

  async validatePlan() {
    return { accepted: true, contract: "console-studio-workflow-execution-plan" };
  }

  async dryRunPlan() {
    return { accepted: true, sideEffects: false };
  }

  async submitJob(request: { readonly plan: unknown; readonly context?: unknown }): Promise<GeospatialGrpcSubmitJobResponse> {
    this.#counter += 1;
    const jobId = `workflow-run-${this.#counter}`;
    this.#jobs.set(jobId, { request, polls: 0, status: "accepted" });
    return { jobId, state: "accepted" };
  }

  async getJob(request: { readonly jobId: string }): Promise<GeospatialGrpcGetJobResponse> {
    const job = this.#requireJob(request.jobId);
    job.polls += 1;
    if (job.polls === 1) {
      job.status = "running";
      return this.#jobResponse(request.jobId, "running", 45, "Worker admitted sample partition and opened scratch workspace.");
    }
    job.status = "successful";
    return this.#jobResponse(request.jobId, "completed", 100, "Result package and row-failure artifact are available.");
  }

  async getJobResult(request: { readonly jobId: string }): Promise<GeospatialGrpcGetJobResultResponse> {
    this.#requireJob(request.jobId);
    return {
      jobId: request.jobId,
      result: {
        summary: "Sample run processed 250 features, rejected 3 rows, and produced a result package.",
        artifacts: [
          artifact("artifact-review-layer", "FeatureLayer", "Flagged permit features", "/api/workflows/artifacts/review-layer", "application/geo+json"),
          artifact("artifact-rejected-rows", "Table", "Rejected source rows", "/api/workflows/artifacts/rejected-rows", "application/json"),
          artifact("artifact-run-report", "Report", "Run report", "/api/workflows/artifacts/run-report", "application/pdf"),
        ],
        featureFailures: [
          {
            row: 41,
            featureId: "permit-041",
            nodeId: "source-permits",
            code: "INVALID_GEOMETRY",
            message: "Latitude value is outside the configured dataset extent.",
          },
          {
            row: 87,
            featureId: "permit-087",
            nodeId: "flag-intersections",
            code: "MISSING_PERMIT_ID",
            message: "Required permit_id attribute was blank after CSV normalization.",
          },
          {
            row: 192,
            featureId: "permit-192",
            nodeId: "buffer-habitats",
            code: "EMPTY_GEOMETRY",
            message: "Feature had no geometry after reprojection.",
          },
        ],
        provenanceLinks: [
          "/catalog/items/protected-habitats/provenance",
          "/studio/workflows/runs/workflow-run-1",
          "/operate/jobs/workflow-run-1/logs",
        ],
      } satisfies WorkflowExecutionResult,
    };
  }

  async cancelJob(request: { readonly jobId: string }): Promise<GeospatialGrpcCancelJobResponse> {
    const job = this.#requireJob(request.jobId);
    job.status = "dismissed";
    return { jobId: request.jobId, state: "dismissed" };
  }

  #requireJob(jobId: string) {
    const job = this.#jobs.get(jobId);
    if (!job) {
      throw new Error(`Unknown fixture workflow job '${jobId}'.`);
    }
    return job;
  }

  #jobResponse(jobId: string, state: string, percent: number, message: string): GeospatialGrpcGetJobResponse {
    return {
      jobId,
      state,
      progress: {
        progressPercent: percent,
        message,
        updatedAt: Date.now(),
      },
    };
  }
}

function blockedRun(definition: WorkflowDefinitionPayload, mode: WorkflowRunMode, messages: readonly string[]): WorkflowRunRecord {
  const now = new Date().toISOString();
  return {
    runId: `blocked-${definition.workflowId}`,
    workflowId: definition.workflowId,
    mode,
    status: "failed",
    startedAt: now,
    completedAt: now,
    snapshots: [
      {
        status: "failed",
        error: {
          code: "WorkflowValidationBlocked",
          message: "Workflow validation blocked execution.",
          details: messages,
        },
      },
    ],
    logs: messages.map((message) => log("error", message)),
    artifacts: [],
    featureFailures: [],
    provenanceLinks: [],
  };
}

function artifact(
  artifactId: string,
  kind: ArtifactKind,
  title: string,
  href: string,
  mediaType: string,
): WorkflowExecutionResult["artifacts"][number] {
  return { artifactId, kind, title, href, mediaType };
}

function log(level: WorkflowRunLogEntry["level"], message: string): WorkflowRunLogEntry {
  return { at: new Date().toISOString(), level, message };
}

function isTerminal(status: JobStatus): boolean {
  return status === "successful" || status === "failed" || status === "dismissed";
}

function assertPublishableDefinition(definition: WorkflowDefinitionPayload): void {
  const validation = validateWorkflowDefinition(definition);
  if (validation.status === "blocked") {
    const messages = validation.issues.map((issue) => issue.message).join(" ");
    throw new Error(`Workflow validation blocked publication: ${messages}`);
  }
}

function assertPublishableSchedule(request: WorkflowPublicationRequest): void {
  if (request.executionMode !== "scheduled") return;
  if (!request.cronExpression?.trim() || !isFiveFieldCron(request.cronExpression)) {
    throw new Error("Scheduled workflow publication requires a non-empty valid 5-field cron expression.");
  }
}

function collectUpstreamItems(definition: WorkflowDefinitionPayload): readonly string[] {
  return Array.from(
    new Set(
      definition.steps.flatMap((step) =>
        [step.inputs.inputLayerId, step.inputs.clipLayerId]
          .filter((value): value is string => Boolean(value))
          .map((value) => value.split(".")[0]),
      ),
    ),
  );
}
