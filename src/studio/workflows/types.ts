import type { IJobRun, JobSnapshot, JobStatus } from "@honua/sdk-js/contract";
import type { HonuaProcessExecuteRequest } from "@honua/sdk-js/honua";

export type WorkflowDefinitionSource =
  | "honua-server:WorkflowDefinition"
  | "honua-server:AnalysisPlan"
  | "honua-sdk:IJobRun"
  | "ogc-api-processes";

export type WorkflowMode = "etl" | "geoprocessing" | "hybrid";
export type WorkflowNodeKind =
  | "source"
  | "transform"
  | "sink"
  | "process"
  | "parameter"
  | "validation"
  | "artifact"
  | "publication";
export type AnalysisPlanStepKind = "QueryFeatures" | "Geoprocess" | "Aggregate" | "RenderMap" | "Export";
export type ArtifactKind = "Scalar" | "FeatureLayer" | "Table" | "Raster" | "File" | "Report" | "Map" | "AppBundle";
export type WorkflowTriggerKind = "Manual" | "Cron";

export interface WorkflowTriggerPayload {
  readonly kind: WorkflowTriggerKind;
  readonly cronExpression?: string;
  readonly timeZone?: string;
  readonly enabled: boolean;
}

export interface AnalysisPlanStepPayload {
  readonly stepId: string;
  readonly kind: AnalysisPlanStepKind;
  readonly processId?: string;
  readonly inputs: Readonly<Record<string, string>>;
  readonly dependsOn: readonly string[];
}

export interface AnalysisPlanPayload {
  readonly planId: string;
  readonly intentId: string;
  readonly steps: readonly AnalysisPlanStepPayload[];
  readonly outputs: readonly ArtifactKind[];
  readonly warnings: readonly string[];
}

export interface StepInputBindingPayload {
  readonly sourceStepId: string;
  readonly sourceArtifactSelector: string;
  readonly targetInputKey: string;
}

export interface StepRetryPolicyPayload {
  readonly maxAttempts: number;
  readonly backoffSeconds: number;
}

export interface WorkflowStepDefinitionPayload {
  readonly stepId: string;
  readonly label: string;
  readonly nodeKind: WorkflowNodeKind;
  readonly plan: AnalysisPlanPayload;
  readonly processId?: string;
  readonly inputs: Readonly<Record<string, string>>;
  readonly dependsOn: readonly string[];
  readonly inputBindings: readonly StepInputBindingPayload[];
  readonly retryPolicy?: StepRetryPolicyPayload;
  readonly failurePolicy: "Fail" | "Continue";
  readonly timeoutSeconds?: number;
}

/**
 * Structural projection of honua-server `WorkflowDefinition`.
 *
 * This is intentionally kept as an API payload at the transport boundary. The
 * editor may inspect and annotate it, but execution and publication remain
 * server or SDK responsibilities.
 */
export interface WorkflowDefinitionPayload {
  readonly workflowId: string;
  readonly name: string;
  readonly description?: string;
  readonly mode: WorkflowMode;
  readonly steps: readonly WorkflowStepDefinitionPayload[];
  readonly trigger?: WorkflowTriggerPayload;
  readonly createdAt: string;
  readonly updatedAt: string;
  readonly metadata: Readonly<Record<string, string>>;
}

export interface WorkflowDraft {
  readonly draftId: string;
  readonly prompt: string;
  readonly generatedAt: string;
  readonly definition: WorkflowDefinitionPayload;
  readonly generatedContract: WorkflowDefinitionSource[];
  readonly warnings: readonly string[];
  readonly eligibleProcessService: boolean;
  readonly explanation: readonly string[];
}

export type WorkflowValidationIssueKind =
  | "missing-parameter"
  | "unsupported-transform"
  | "sink-constraint"
  | "permission"
  | "contract"
  | "warning";
export type WorkflowValidationSeverity = "error" | "warning" | "info";

export interface WorkflowValidationIssue {
  readonly kind: WorkflowValidationIssueKind;
  readonly severity: WorkflowValidationSeverity;
  readonly code: string;
  readonly nodeId?: string;
  readonly path: string;
  readonly message: string;
  readonly requiredAction?: string;
}

export interface WorkflowValidationResult {
  readonly checkedAt: string;
  readonly status: "valid" | "blocked" | "warning";
  readonly issues: readonly WorkflowValidationIssue[];
  readonly contractReferences: WorkflowDefinitionSource[];
}

export type WorkflowRunMode = "dry-run" | "sample-run";

export interface WorkflowRunLogEntry {
  readonly at: string;
  readonly level: "info" | "warning" | "error";
  readonly message: string;
}

export interface WorkflowArtifactRef {
  readonly artifactId: string;
  readonly kind: ArtifactKind;
  readonly title: string;
  readonly href: string;
  readonly mediaType: string;
}

export interface WorkflowFeatureFailure {
  readonly row: number;
  readonly featureId?: string;
  readonly nodeId: string;
  readonly code: string;
  readonly message: string;
}

export interface WorkflowExecutionResult {
  readonly summary: string;
  readonly artifacts: readonly WorkflowArtifactRef[];
  readonly featureFailures: readonly WorkflowFeatureFailure[];
  readonly provenanceLinks: readonly string[];
}

export interface WorkflowRunRecord {
  readonly runId: string;
  readonly workflowId: string;
  readonly mode: WorkflowRunMode;
  readonly status: JobStatus;
  readonly startedAt: string;
  readonly completedAt?: string;
  readonly snapshots: readonly JobSnapshot<WorkflowExecutionResult>[];
  readonly logs: readonly WorkflowRunLogEntry[];
  readonly artifacts: readonly WorkflowArtifactRef[];
  readonly featureFailures: readonly WorkflowFeatureFailure[];
  readonly provenanceLinks: readonly string[];
}

export interface WorkflowPublicationRequest {
  readonly executionMode: "manual" | "scheduled";
  readonly cronExpression?: string;
  readonly timeZone?: string;
}

export interface WorkflowVersionRecord {
  readonly versionId: string;
  readonly createdAt: string;
  readonly summary: string;
  readonly rollbackAvailable: boolean;
}

export interface WorkflowContentProvenance {
  readonly createdBy: string;
  readonly sourcePrompt: string;
  readonly sourceDraftId: string;
  readonly definitionHash: string;
  readonly upstreamItems: readonly string[];
  readonly runHistoryHref: string;
}

export interface PublishedWorkflowContentItem {
  readonly itemId: string;
  readonly title: string;
  readonly contentKind: "workflow-definition";
  readonly workflowId: string;
  readonly href: string;
  readonly executionModes: readonly WorkflowPublicationRequest["executionMode"][];
  readonly schedule?: WorkflowTriggerPayload;
  readonly versions: readonly WorkflowVersionRecord[];
  readonly activeVersionId: string;
  readonly provenance: WorkflowContentProvenance;
  readonly runHistoryHref: string;
}

export interface ProcessParameterMetadata {
  readonly name: string;
  readonly displayName: string;
  readonly valueType: "Text" | "WholeNumber" | "FloatingPoint" | "Flag" | "Wkb" | "WkbArray" | "Srid" | "LayerId";
  readonly required: boolean;
  readonly defaultValue?: string;
}

export interface ResultPackageMetadata {
  readonly resultPackageId: string;
  readonly artifactKinds: readonly ArtifactKind[];
  readonly retentionPolicy: string;
}

export interface ProcessServicePublication {
  readonly processId: string;
  readonly title: string;
  readonly stableInvocationRoute: string;
  readonly parameterMetadata: readonly ProcessParameterMetadata[];
  readonly resultPackageMetadata: ResultPackageMetadata;
  readonly permissions: readonly string[];
  readonly contentItemId: string;
}

export interface StudioWorkflowTransport {
  createDraftFromPrompt(prompt: string, signal?: AbortSignal): Promise<WorkflowDraft>;
  validateDefinition(definition: WorkflowDefinitionPayload, signal?: AbortSignal): Promise<WorkflowValidationResult>;
  runDefinition(
    definition: WorkflowDefinitionPayload,
    mode: WorkflowRunMode,
    signal?: AbortSignal,
  ): Promise<WorkflowRunRecord>;
  publishDefinition(
    definition: WorkflowDefinitionPayload,
    request: WorkflowPublicationRequest,
    signal?: AbortSignal,
  ): Promise<PublishedWorkflowContentItem>;
  publishProcessService(
    definition: WorkflowDefinitionPayload,
    signal?: AbortSignal,
  ): Promise<ProcessServicePublication>;
  rollbackContentItem(
    item: PublishedWorkflowContentItem,
    versionId: string,
    signal?: AbortSignal,
  ): Promise<PublishedWorkflowContentItem>;
}

export interface WorkflowJobHandle {
  readonly request: HonuaProcessExecuteRequest;
  readonly job: IJobRun<WorkflowExecutionResult>;
}
