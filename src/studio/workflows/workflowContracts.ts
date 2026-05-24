import type {
  AnalysisPlanPayload,
  AnalysisPlanStepKind,
  ArtifactKind,
  ProcessParameterMetadata,
  ResultPackageMetadata,
  ServerWorkflowDefinitionPayload,
  ServerWorkflowStepDefinitionPayload,
  WorkflowDefinitionPayload,
  WorkflowMode,
  WorkflowNodeKind,
  WorkflowStepDefinitionPayload,
  WorkflowValidationIssue,
  WorkflowValidationResult,
} from "./types";

export const WORKFLOW_CONTRACT_REFERENCES = [
  "honua-server:WorkflowDefinition",
  "honua-server:AnalysisPlan",
  "honua-sdk:IJobRun",
  "ogc-api-processes",
] as const;

const SUPPORTED_TRANSFORMS = new Set(["geometry.buffer", "geometry.clip", "analytics.summarize", "conversion.export"]);
const REQUIRED_PROCESS_PARAMETERS: Readonly<Record<string, readonly string[]>> = {
  "geometry.buffer": ["inputLayerId", "distanceMeters", "units"],
  "geometry.clip": ["inputLayerId", "clipLayerId"],
  "analytics.summarize": ["inputLayerId", "groupByField"],
  "conversion.export": ["inputLayerId", "format"],
};
const WORKFLOW_MODES = ["etl", "geoprocessing", "hybrid"] satisfies readonly WorkflowMode[];
const WORKFLOW_NODE_KINDS = [
  "source",
  "transform",
  "sink",
  "process",
  "parameter",
  "validation",
  "artifact",
  "publication",
] satisfies readonly WorkflowNodeKind[];
const ANALYSIS_PLAN_STEP_KINDS = [
  "QueryFeatures",
  "Geoprocess",
  "Aggregate",
  "RenderMap",
  "Export",
] satisfies readonly AnalysisPlanStepKind[];
const ARTIFACT_KINDS = [
  "Scalar",
  "FeatureLayer",
  "Table",
  "Raster",
  "File",
  "Report",
  "Map",
  "AppBundle",
] satisfies readonly ArtifactKind[];
const NON_SERVICE_PARAMETER_INPUTS = new Set([
  "format",
  "itemKind",
  "permission",
  "publishAsProcessService",
  "retentionPolicy",
  "schemaPolicy",
  "sinkType",
  "sourceUri",
]);

export interface ProcessServiceEligibility {
  readonly eligible: boolean;
  readonly bindingStep?: WorkflowStepDefinitionPayload;
  readonly reasons: readonly string[];
}

export interface ProcessServicePublicationContract {
  readonly eligibility: ProcessServiceEligibility;
  readonly parameterMetadata: readonly ProcessParameterMetadata[];
  readonly resultPackageMetadata: ResultPackageMetadata;
}

export function isWorkflowDefinitionPayload(value: unknown): value is WorkflowDefinitionPayload {
  return validateWorkflowDefinitionShape(value).length === 0;
}

export function validateWorkflowDefinition(definition: unknown): WorkflowValidationResult {
  const shapeIssues = validateWorkflowDefinitionShape(definition);
  if (shapeIssues.length > 0) {
    return validationResult(shapeIssues);
  }

  const payload = definition as WorkflowDefinitionPayload;
  const issues: WorkflowValidationIssue[] = [];

  if (!payload.workflowId.trim()) {
    issues.push(contractIssue("Workflow identifier is required.", "$.workflowId"));
  }
  if (!payload.name.trim()) {
    issues.push(contractIssue("Workflow name is required.", "$.name"));
  }
  if (payload.steps.length === 0) {
    issues.push(contractIssue("Workflow must contain at least one step.", "$.steps"));
  }

  const stepIds = new Set<string>();
  for (const step of payload.steps) {
    validateStep(step, issues);
    if (stepIds.has(step.stepId)) {
      issues.push(contractIssue(`Duplicate step identifier '${step.stepId}'.`, `$.steps.${step.stepId}`));
    }
    stepIds.add(step.stepId);
  }

  for (const step of payload.steps) {
    for (const dependency of step.dependsOn) {
      if (!stepIds.has(dependency)) {
        issues.push(
          contractIssue(
            `Step '${step.stepId}' depends on unknown step '${dependency}'.`,
            `$.steps.${step.stepId}.dependsOn`,
            step.stepId,
          ),
        );
      }
    }
    for (const binding of step.inputBindings) {
      if (!stepIds.has(binding.sourceStepId)) {
        issues.push(
          contractIssue(
            `Step '${step.stepId}' binding references unknown source step '${binding.sourceStepId}'.`,
            `$.steps.${step.stepId}.inputBindings`,
            step.stepId,
          ),
        );
      }
      if (!step.dependsOn.includes(binding.sourceStepId)) {
        issues.push({
          kind: "contract",
          severity: "error",
          code: "BINDING_REQUIRES_DEPENDENCY",
          nodeId: step.stepId,
          path: `$.steps.${step.stepId}.inputBindings`,
          message: `Binding from '${binding.sourceStepId}' must also be listed in dependsOn.`,
          requiredAction: "Add the source step to dependsOn so execution ordering covers the data edge.",
        });
      }
    }
  }

  if (hasCycle(payload.steps)) {
    issues.push(contractIssue("Workflow contains a dependency cycle.", "$.steps"));
  }

  if (payload.trigger?.kind === "Cron") {
    if (!payload.trigger.cronExpression?.trim()) {
      issues.push(contractIssue("Cron trigger requires a non-empty cron expression.", "$.trigger.cronExpression"));
    } else if (!isFiveFieldCron(payload.trigger.cronExpression)) {
      issues.push(
        contractIssue(
          "Cron trigger must use a valid 5-field expression.",
          "$.trigger.cronExpression",
          undefined,
          "Use the supported POSIX subset: *, values, lists, ranges, and steps for minute hour day-of-month month day-of-week.",
        ),
      );
    }
  }

  return validationResult(issues);
}

export function getProcessServiceEligibility(definition: WorkflowDefinitionPayload): ProcessServiceEligibility {
  const bindingStep = definition.steps.find(isProcessServiceBinding);
  if (!bindingStep) {
    return {
      eligible: false,
      reasons: ["Process-service publication requires a publication node with publishAsProcessService=true."],
    };
  }

  const reasons: string[] = [];
  const processId = getStepProcessId(bindingStep);
  if (!processId || !SUPPORTED_TRANSFORMS.has(processId)) {
    reasons.push("Process-service publication requires a Console-advertised process id.");
  }
  if (!bindingStep.plan.steps.some((planStep) => planStep.processId === processId || planStep.kind === "Geoprocess")) {
    reasons.push("Process-service publication requires process-capable analysis-plan metadata.");
  }
  if (!bindingStep.inputBindings.some((binding) => binding.targetInputKey === "resultPackageId")) {
    reasons.push("Process-service publication requires an explicit result-package input binding.");
  }
  if (bindingStep.inputs.permission !== "granted") {
    reasons.push("Process-service publication requires publish-process permission on the current definition.");
  }
  if (collectProcessServiceParameterMetadata(definition).length === 0) {
    reasons.push("Process-service publication requires at least one invokable parameter.");
  }

  return {
    eligible: reasons.length === 0,
    bindingStep,
    reasons,
  };
}

export function describeProcessServicePublication(definition: WorkflowDefinitionPayload): ProcessServicePublicationContract {
  return {
    eligibility: getProcessServiceEligibility(definition),
    parameterMetadata: collectProcessServiceParameterMetadata(definition),
    resultPackageMetadata: {
      resultPackageId: `result-package-${definition.workflowId}`,
      artifactKinds: collectResultArtifactKinds(definition),
      retentionPolicy: "honua.retention.result-package.default",
    },
  };
}

function validationResult(issues: readonly WorkflowValidationIssue[]): WorkflowValidationResult {
  const status = issues.some((issue) => issue.severity === "error")
    ? "blocked"
    : issues.some((issue) => issue.severity === "warning")
      ? "warning"
      : "valid";

  return {
    checkedAt: new Date().toISOString(),
    status,
    issues,
    contractReferences: [...WORKFLOW_CONTRACT_REFERENCES],
  };
}

export function toProcessExecutionPlan(definition: WorkflowDefinitionPayload, mode: "dry-run" | "sample-run") {
  return {
    workflowId: definition.workflowId,
    mode,
    steps: definition.steps.map((step) => ({
      stepId: step.stepId,
      nodeKind: step.nodeKind,
      plan: step.plan,
      inputs: step.inputs,
      dependsOn: step.dependsOn,
    })),
    metadata: {
      ...definition.metadata,
      "console.runMode": mode,
      "console.contract": "console-studio-workflow-execution-plan",
    },
  };
}

export function toServerWorkflowDefinitionPayload(
  definition: WorkflowDefinitionPayload,
): ServerWorkflowDefinitionPayload {
  return {
    workflowId: definition.workflowId,
    name: definition.name,
    steps: definition.steps.map(toServerWorkflowStepDefinitionPayload),
    createdAt: definition.createdAt,
    updatedAt: definition.updatedAt,
    metadata: definition.metadata,
    ...(definition.description !== undefined ? { description: definition.description } : {}),
    ...(definition.trigger !== undefined ? { trigger: definition.trigger } : {}),
  };
}

export function stableDefinitionHash(definition: WorkflowDefinitionPayload): string {
  const encoded = stableStringify(toServerWorkflowDefinitionPayload(definition));
  let hash = 5381;
  for (let index = 0; index < encoded.length; index += 1) {
    hash = (hash * 33) ^ encoded.charCodeAt(index);
  }
  return `wf-${(hash >>> 0).toString(16).padStart(8, "0")}`;
}

function toServerWorkflowStepDefinitionPayload(
  step: WorkflowStepDefinitionPayload,
): ServerWorkflowStepDefinitionPayload {
  return {
    stepId: step.stepId,
    plan: step.plan,
    dependsOn: step.dependsOn,
    inputBindings: step.inputBindings,
    failurePolicy: step.failurePolicy,
    ...(step.retryPolicy
      ? {
          retryPolicy: {
            maxAttempts: step.retryPolicy.maxAttempts,
            initialDelaySeconds: step.retryPolicy.backoffSeconds,
          },
        }
      : {}),
    ...(step.timeoutSeconds !== undefined ? { timeoutSeconds: step.timeoutSeconds } : {}),
  };
}

function validateStep(step: WorkflowStepDefinitionPayload, issues: WorkflowValidationIssue[]): void {
  if (!step.stepId.trim()) {
    issues.push(contractIssue("Every step must declare a non-empty step identifier.", "$.steps[].stepId"));
  }
  if (!step.plan || !step.plan.planId.trim()) {
    issues.push(contractIssue("Step must declare a canonical analysis plan.", `$.steps.${step.stepId}.plan`, step.stepId));
  }
  validatePlan(step.stepId, step.plan, issues);

  if (step.retryPolicy && step.retryPolicy.maxAttempts < 1) {
    issues.push(
      contractIssue(
        `Step '${step.stepId}' retry policy must allow at least one attempt.`,
        `$.steps.${step.stepId}.retryPolicy.maxAttempts`,
        step.stepId,
      ),
    );
  }
  if (step.retryPolicy && step.retryPolicy.backoffSeconds <= 0) {
    issues.push(
      contractIssue(
        `Step '${step.stepId}' retry policy must use a positive backoff interval.`,
        `$.steps.${step.stepId}.retryPolicy.backoffSeconds`,
        step.stepId,
      ),
    );
  }
  if (step.timeoutSeconds !== undefined && step.timeoutSeconds <= 0) {
    issues.push(
      contractIssue(
        `Step '${step.stepId}' timeout must be greater than zero seconds.`,
        `$.steps.${step.stepId}.timeoutSeconds`,
        step.stepId,
      ),
    );
  }

  if (step.nodeKind === "transform" || step.nodeKind === "process") {
    const processId = step.processId ?? step.plan.steps.find((planStep) => planStep.kind === "Geoprocess")?.processId;
    if (processId && !SUPPORTED_TRANSFORMS.has(processId)) {
      issues.push({
        kind: "unsupported-transform",
        severity: "error",
        code: "UNSUPPORTED_TRANSFORM",
        nodeId: step.stepId,
        path: `$.steps.${step.stepId}.processId`,
        message: `Transform '${processId}' is not in the Honua process catalog exposed to Console.`,
        requiredAction: "Choose a supported Honua process or wait for the server catalog to advertise this transform.",
      });
    }
    for (const parameter of REQUIRED_PROCESS_PARAMETERS[processId ?? ""] ?? []) {
      if (!hasInput(step, parameter)) {
        issues.push({
          kind: "missing-parameter",
          severity: "error",
          code: "MISSING_PARAMETER",
          nodeId: step.stepId,
          path: `$.steps.${step.stepId}.inputs.${parameter}`,
          message: `Required parameter '${parameter}' is missing for '${processId}'.`,
          requiredAction: "Supply the parameter before running or publishing.",
        });
      }
    }
  }

  if (step.nodeKind === "sink") {
    if (step.inputs.sinkType === "catalog-content-item" && !step.inputs.itemKind) {
      issues.push({
        kind: "sink-constraint",
        severity: "error",
        code: "SINK_ITEM_KIND_REQUIRED",
        nodeId: step.stepId,
        path: `$.steps.${step.stepId}.inputs.itemKind`,
        message: "Catalog sinks must declare the content item kind they will produce.",
        requiredAction: "Set itemKind to layer, table, package, or report.",
      });
    }
    if (step.inputs.sinkType === "external-s3" && step.inputs.permission !== "granted") {
      issues.push({
        kind: "permission",
        severity: "error",
        code: "SINK_PERMISSION_REQUIRED",
        nodeId: step.stepId,
        path: `$.steps.${step.stepId}.inputs.permission`,
        message: "External object-store sinks require an explicit publish permission grant.",
        requiredAction: "Ask an operator to grant the external sink permission or change the sink.",
      });
    }
  }

  if (step.nodeKind === "publication" && step.inputs.publishAsProcessService === "true") {
    if (step.inputs.permission !== "granted") {
      issues.push({
        kind: "permission",
        severity: "warning",
        code: "PROCESS_SERVICE_PERMISSION_PENDING",
        nodeId: step.stepId,
        path: `$.steps.${step.stepId}.inputs.permission`,
        message: "Process service publication is eligible but requires publish-process permission at submit time.",
        requiredAction: "Confirm the service publication permission before promoting.",
      });
    }
  }
}

function validateWorkflowDefinitionShape(value: unknown): WorkflowValidationIssue[] {
  const issues: WorkflowValidationIssue[] = [];
  if (!isRecord(value)) {
    issues.push(contractIssue("Workflow definition must be a JSON object.", "$"));
    return issues;
  }

  expectString(value.workflowId, "$.workflowId", "Workflow definition must declare workflowId as a string.", issues);
  expectString(value.name, "$.name", "Workflow definition must declare name as a string.", issues);
  expectEnum(value.mode, WORKFLOW_MODES, "$.mode", "Workflow mode must be etl, geoprocessing, or hybrid.", issues);
  expectString(value.createdAt, "$.createdAt", "Workflow definition must declare createdAt as an ISO string.", issues);
  expectString(value.updatedAt, "$.updatedAt", "Workflow definition must declare updatedAt as an ISO string.", issues);
  expectStringRecord(value.metadata, "$.metadata", "Workflow metadata must be a string map.", issues);
  validateTriggerShape(value.trigger, issues);

  if (!Array.isArray(value.steps)) {
    issues.push(contractIssue("Workflow steps must be an array.", "$.steps"));
    return issues;
  }

  value.steps.forEach((step, index) => validateStepShape(step, `$.steps[${index}]`, issues));
  return issues;
}

function validateTriggerShape(value: unknown, issues: WorkflowValidationIssue[]): void {
  if (value === undefined) return;
  if (!isRecord(value)) {
    issues.push(contractIssue("Workflow trigger must be an object.", "$.trigger"));
    return;
  }
  expectEnum(value.kind, ["Manual", "Cron"], "$.trigger.kind", "Workflow trigger kind must be Manual or Cron.", issues);
  if (value.cronExpression !== undefined) {
    expectString(
      value.cronExpression,
      "$.trigger.cronExpression",
      "Workflow trigger cronExpression must be a string.",
      issues,
    );
  }
  if (value.timeZone !== undefined) {
    expectString(value.timeZone, "$.trigger.timeZone", "Workflow trigger timeZone must be a string.", issues);
  }
  if (typeof value.enabled !== "boolean") {
    issues.push(contractIssue("Workflow trigger enabled must be a boolean.", "$.trigger.enabled"));
  }
}

function validateStepShape(value: unknown, path: string, issues: WorkflowValidationIssue[]): void {
  if (!isRecord(value)) {
    issues.push(contractIssue("Workflow step must be an object.", path));
    return;
  }

  expectString(value.stepId, `${path}.stepId`, "Workflow step must declare stepId as a string.", issues);
  expectString(value.label, `${path}.label`, "Workflow step must declare label as a string.", issues);
  expectEnum(value.nodeKind, WORKFLOW_NODE_KINDS, `${path}.nodeKind`, "Workflow step nodeKind is unsupported.", issues);
  if (value.processId !== undefined) {
    expectString(value.processId, `${path}.processId`, "Workflow step processId must be a string.", issues);
  }
  expectStringRecord(value.inputs, `${path}.inputs`, "Workflow step inputs must be a string map.", issues);
  expectStringArray(value.dependsOn, `${path}.dependsOn`, "Workflow step dependsOn must be a string array.", issues);
  validateInputBindingsShape(value.inputBindings, `${path}.inputBindings`, issues);
  if (value.failurePolicy !== "Fail" && value.failurePolicy !== "Skip") {
    issues.push(contractIssue("Workflow step failurePolicy must be Fail or Skip.", `${path}.failurePolicy`));
  }
  if (value.timeoutSeconds !== undefined && !isFiniteNumber(value.timeoutSeconds)) {
    issues.push(contractIssue("Workflow step timeoutSeconds must be a number.", `${path}.timeoutSeconds`));
  }
  validateRetryPolicyShape(value.retryPolicy, `${path}.retryPolicy`, issues);
  validatePlanShape(value.plan, `${path}.plan`, issues);
}

function validateInputBindingsShape(value: unknown, path: string, issues: WorkflowValidationIssue[]): void {
  if (!Array.isArray(value)) {
    issues.push(contractIssue("Workflow step inputBindings must be an array.", path));
    return;
  }
  value.forEach((binding, index) => {
    const bindingPath = `${path}[${index}]`;
    if (!isRecord(binding)) {
      issues.push(contractIssue("Workflow input binding must be an object.", bindingPath));
      return;
    }
    expectString(
      binding.sourceStepId,
      `${bindingPath}.sourceStepId`,
      "Workflow input binding sourceStepId must be a string.",
      issues,
    );
    expectString(
      binding.sourceArtifactSelector,
      `${bindingPath}.sourceArtifactSelector`,
      "Workflow input binding sourceArtifactSelector must be a string.",
      issues,
    );
    expectString(
      binding.targetInputKey,
      `${bindingPath}.targetInputKey`,
      "Workflow input binding targetInputKey must be a string.",
      issues,
    );
  });
}

function validateRetryPolicyShape(value: unknown, path: string, issues: WorkflowValidationIssue[]): void {
  if (value === undefined) return;
  if (!isRecord(value)) {
    issues.push(contractIssue("Workflow retryPolicy must be an object.", path));
    return;
  }
  if (!isFiniteNumber(value.maxAttempts)) {
    issues.push(contractIssue("Workflow retryPolicy maxAttempts must be a number.", `${path}.maxAttempts`));
  }
  if (!isFiniteNumber(value.backoffSeconds)) {
    issues.push(contractIssue("Workflow retryPolicy backoffSeconds must be a number.", `${path}.backoffSeconds`));
  }
}

function validatePlanShape(value: unknown, path: string, issues: WorkflowValidationIssue[]): void {
  if (!isRecord(value)) {
    issues.push(contractIssue("Workflow step plan must be an object.", path));
    return;
  }
  expectString(value.planId, `${path}.planId`, "Analysis plan must declare planId as a string.", issues);
  expectString(value.intentId, `${path}.intentId`, "Analysis plan must declare intentId as a string.", issues);
  expectEnumArray(value.outputs, ARTIFACT_KINDS, `${path}.outputs`, "Analysis plan outputs must be artifact kinds.", issues);
  expectStringArray(value.warnings, `${path}.warnings`, "Analysis plan warnings must be a string array.", issues);

  if (!Array.isArray(value.steps)) {
    issues.push(contractIssue("Analysis plan steps must be an array.", `${path}.steps`));
    return;
  }
  value.steps.forEach((planStep, index) => validatePlanStepShape(planStep, `${path}.steps[${index}]`, issues));
}

function validatePlanStepShape(value: unknown, path: string, issues: WorkflowValidationIssue[]): void {
  if (!isRecord(value)) {
    issues.push(contractIssue("Analysis plan step must be an object.", path));
    return;
  }
  expectString(value.stepId, `${path}.stepId`, "Analysis plan step must declare stepId as a string.", issues);
  expectEnum(value.kind, ANALYSIS_PLAN_STEP_KINDS, `${path}.kind`, "Analysis plan step kind is unsupported.", issues);
  if (value.processId !== undefined) {
    expectString(value.processId, `${path}.processId`, "Analysis plan step processId must be a string.", issues);
  }
  expectStringRecord(value.inputs, `${path}.inputs`, "Analysis plan step inputs must be a string map.", issues);
  expectStringArray(value.dependsOn, `${path}.dependsOn`, "Analysis plan step dependsOn must be a string array.", issues);
}

function validatePlan(stepId: string, plan: AnalysisPlanPayload, issues: WorkflowValidationIssue[]): void {
  if (!plan.planId.trim()) {
    issues.push(contractIssue(`Step '${stepId}' plan must declare a plan identifier.`, `$.steps.${stepId}.plan.planId`, stepId));
  }
  if (plan.steps.length === 0) {
    issues.push(contractIssue(`Step '${stepId}' plan must contain at least one analysis step.`, `$.steps.${stepId}.plan.steps`, stepId));
  }
  const planStepIds = new Set(plan.steps.map((planStep) => planStep.stepId));
  for (const planStep of plan.steps) {
    for (const dependency of planStep.dependsOn) {
      if (!planStepIds.has(dependency)) {
        issues.push(
          contractIssue(
            `Plan step '${planStep.stepId}' depends on unknown plan step '${dependency}'.`,
            `$.steps.${stepId}.plan.steps.${planStep.stepId}.dependsOn`,
            stepId,
          ),
        );
      }
    }
  }
}

function hasInput(step: WorkflowStepDefinitionPayload, key: string): boolean {
  if (step.inputs[key] !== undefined && step.inputs[key] !== "") return true;
  return step.plan.steps.some((planStep) => planStep.inputs[key] !== undefined && planStep.inputs[key] !== "");
}

function isProcessServiceBinding(step: WorkflowStepDefinitionPayload): boolean {
  return step.nodeKind === "publication" && step.inputs.publishAsProcessService === "true";
}

function getStepProcessId(step: WorkflowStepDefinitionPayload): string | undefined {
  return step.processId ?? step.plan.steps.find((planStep) => planStep.processId)?.processId;
}

function collectProcessServiceParameterMetadata(definition: WorkflowDefinitionPayload): readonly ProcessParameterMetadata[] {
  const parameters = new Map<string, ProcessParameterMetadata>();
  for (const step of definition.steps) {
    if (step.nodeKind !== "process" && step.nodeKind !== "transform" && step.nodeKind !== "publication") continue;

    const required = new Set(REQUIRED_PROCESS_PARAMETERS[getStepProcessId(step) ?? ""] ?? []);
    collectInputParameters(step.inputs, required, parameters);
    for (const planStep of step.plan.steps) {
      collectInputParameters(planStep.inputs, required, parameters);
    }
  }

  return Array.from(parameters.values()).sort((left, right) => parameterSortKey(left.name) - parameterSortKey(right.name));
}

function collectInputParameters(
  inputs: Readonly<Record<string, string>>,
  required: ReadonlySet<string>,
  parameters: Map<string, ProcessParameterMetadata>,
): void {
  for (const [name, value] of Object.entries(inputs)) {
    if (!isServiceParameterInput(name, value)) continue;
    const next: ProcessParameterMetadata = {
      name,
      displayName: displayName(name),
      valueType: parameterValueType(name),
      required: required.has(name) || name === "inputLayerId",
      defaultValue: value,
    };
    const current = parameters.get(name);
    parameters.set(name, current ? { ...current, required: current.required || next.required } : next);
  }
}

function isServiceParameterInput(name: string, value: string): boolean {
  return value.trim() !== "" && !value.includes(".outputs.") && !NON_SERVICE_PARAMETER_INPUTS.has(name);
}

function parameterValueType(name: string): ProcessParameterMetadata["valueType"] {
  const normalized = name.toLowerCase();
  if (normalized.endsWith("layerid") || normalized === "layer") return "LayerId";
  if (normalized.includes("distance") || normalized.includes("meters") || normalized.includes("ratio")) {
    return "FloatingPoint";
  }
  if (normalized.includes("limit") || normalized.includes("count")) return "WholeNumber";
  if (normalized.includes("enabled") || normalized.startsWith("is") || normalized.startsWith("has")) return "Flag";
  if (normalized.includes("srid")) return "Srid";
  if (normalized.includes("wkb")) return "Wkb";
  return "Text";
}

function displayName(name: string): string {
  return name
    .replace(/([a-z0-9])([A-Z])/g, "$1 $2")
    .replace(/[-_]+/g, " ")
    .replace(/^./, (first) => first.toUpperCase());
}

function parameterSortKey(name: string): number {
  return ["inputLayerId", "clipLayerId", "distanceMeters", "units", "groupByField"].indexOf(name) + 1 || 99;
}

function collectResultArtifactKinds(definition: WorkflowDefinitionPayload): readonly ArtifactKind[] {
  const artifactKinds = Array.from(new Set(definition.steps.flatMap((step) => step.plan.outputs)));
  return artifactKinds.length > 0 ? artifactKinds : ["Report"];
}

function contractIssue(
  message: string,
  path: string,
  nodeId?: string,
  requiredAction = "Revise the generated definition so it matches the server workflow contract.",
): WorkflowValidationIssue {
  return {
    kind: "contract",
    severity: "error",
    code: "WORKFLOW_CONTRACT",
    nodeId,
    path,
    message,
    requiredAction,
  };
}

function hasCycle(steps: readonly WorkflowStepDefinitionPayload[]): boolean {
  const lookup = new Map(steps.map((step) => [step.stepId, step]));
  const visiting = new Set<string>();
  const visited = new Set<string>();

  function visit(stepId: string): boolean {
    if (visiting.has(stepId)) return true;
    if (visited.has(stepId)) return false;
    visiting.add(stepId);
    const step = lookup.get(stepId);
    if (step) {
      for (const dependency of step.dependsOn) {
        if (lookup.has(dependency) && visit(dependency)) return true;
      }
    }
    visiting.delete(stepId);
    visited.add(stepId);
    return false;
  }

  return steps.some((step) => visit(step.stepId));
}

export function isFiveFieldCron(value: string): boolean {
  const fields = value.trim().split(" ").filter((field) => field.length > 0);
  if (fields.length !== 5) return false;

  return (
    parseCronField(fields[0], 0, 59) &&
    parseCronField(fields[1], 0, 23) &&
    parseCronField(fields[2], 1, 31) &&
    parseCronField(fields[3], 1, 12) &&
    parseCronField(fields[4], 0, 7)
  );
}

function parseCronField(field: string, min: number, max: number): boolean {
  const values = new Set<number>();
  const parts = field.split(",");
  if (parts.some((part) => part.trim() === "")) return false;
  for (const part of parts) {
    if (!parseCronPart(part.trim(), min, max, values)) return false;
  }
  for (let value = min; value <= max; value += 1) {
    if (values.has(value)) return true;
  }
  return false;
}

function parseCronPart(part: string, min: number, max: number, values: Set<number>): boolean {
  if (!part) return false;

  const slashIndex = part.indexOf("/");
  const body = slashIndex >= 0 ? part.slice(0, slashIndex) : part;
  const stepText = slashIndex >= 0 ? part.slice(slashIndex + 1) : undefined;
  const step = stepText === undefined ? 1 : parseCronInteger(stepText);
  if (step === undefined || step <= 0) return false;

  let start: number | undefined;
  let end: number | undefined;
  if (body === "*") {
    start = min;
    end = max;
  } else if (body.includes("-")) {
    const rangeParts = body.split("-");
    if (rangeParts.length !== 2) return false;
    start = parseCronInteger(rangeParts[0]);
    end = parseCronInteger(rangeParts[1]);
  } else {
    start = parseCronInteger(body);
    end = start;
  }

  if (start === undefined || end === undefined || start < min || end > max || start > end) {
    return false;
  }

  for (let current = start; current <= end; current += step) {
    values.add(current);
  }
  return true;
}

function parseCronInteger(value: string): number | undefined {
  if (!/^\d+$/.test(value)) return undefined;
  const parsed = Number(value);
  return Number.isSafeInteger(parsed) ? parsed : undefined;
}

function isFiniteNumber(value: unknown): value is number {
  return typeof value === "number" && Number.isFinite(value);
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function expectString(value: unknown, path: string, message: string, issues: WorkflowValidationIssue[]): void {
  if (typeof value !== "string") {
    issues.push(contractIssue(message, path));
  }
}

function expectStringArray(value: unknown, path: string, message: string, issues: WorkflowValidationIssue[]): void {
  if (!Array.isArray(value)) {
    issues.push(contractIssue(message, path));
    return;
  }
  value.forEach((entry, index) => {
    if (typeof entry !== "string") {
      issues.push(contractIssue(message, `${path}[${index}]`));
    }
  });
}

function expectStringRecord(value: unknown, path: string, message: string, issues: WorkflowValidationIssue[]): void {
  if (!isRecord(value)) {
    issues.push(contractIssue(message, path));
    return;
  }
  for (const [key, entry] of Object.entries(value)) {
    if (typeof entry !== "string") {
      issues.push(contractIssue(message, `${path}.${key}`));
    }
  }
}

function expectEnum<T extends string>(
  value: unknown,
  values: readonly T[],
  path: string,
  message: string,
  issues: WorkflowValidationIssue[],
): void {
  if (typeof value !== "string" || !values.includes(value as T)) {
    issues.push(contractIssue(message, path));
  }
}

function expectEnumArray<T extends string>(
  value: unknown,
  values: readonly T[],
  path: string,
  message: string,
  issues: WorkflowValidationIssue[],
): void {
  if (!Array.isArray(value)) {
    issues.push(contractIssue(message, path));
    return;
  }
  value.forEach((entry, index) => expectEnum(entry, values, `${path}[${index}]`, message, issues));
}

function stableStringify(value: unknown): string {
  return JSON.stringify(sortValue(value));
}

function sortValue(value: unknown): unknown {
  if (Array.isArray(value)) return value.map(sortValue);
  if (value && typeof value === "object") {
    return Object.fromEntries(
      Object.entries(value as Record<string, unknown>)
        .sort(([left], [right]) => left.localeCompare(right))
        .map(([key, entry]) => [key, sortValue(entry)]),
    );
  }
  return value;
}
