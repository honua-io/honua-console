import type {
  AnalysisPlanPayload,
  WorkflowDefinitionPayload,
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

export function validateWorkflowDefinition(definition: WorkflowDefinitionPayload): WorkflowValidationResult {
  const issues: WorkflowValidationIssue[] = [];

  if (!definition.workflowId.trim()) {
    issues.push(contractIssue("Workflow identifier is required.", "$.workflowId"));
  }
  if (!definition.name.trim()) {
    issues.push(contractIssue("Workflow name is required.", "$.name"));
  }
  if (definition.steps.length === 0) {
    issues.push(contractIssue("Workflow must contain at least one step.", "$.steps"));
  }

  const stepIds = new Set<string>();
  for (const step of definition.steps) {
    validateStep(step, issues);
    if (stepIds.has(step.stepId)) {
      issues.push(contractIssue(`Duplicate step identifier '${step.stepId}'.`, `$.steps.${step.stepId}`));
    }
    stepIds.add(step.stepId);
  }

  for (const step of definition.steps) {
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

  if (hasCycle(definition.steps)) {
    issues.push(contractIssue("Workflow contains a dependency cycle.", "$.steps"));
  }

  if (definition.trigger?.kind === "Cron") {
    if (!definition.trigger.cronExpression?.trim()) {
      issues.push(contractIssue("Cron trigger requires a non-empty cron expression.", "$.trigger.cronExpression"));
    } else if (!isFiveFieldCron(definition.trigger.cronExpression)) {
      issues.push(
        contractIssue(
          "Cron trigger must use a 5-field expression.",
          "$.trigger.cronExpression",
          undefined,
          "Use minute hour day-of-month month day-of-week.",
        ),
      );
    }
  }

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
      "console.contract": "honua-server:WorkflowDefinition",
    },
  };
}

export function stableDefinitionHash(definition: WorkflowDefinitionPayload): string {
  const encoded = stableStringify(definition);
  let hash = 5381;
  for (let index = 0; index < encoded.length; index += 1) {
    hash = (hash * 33) ^ encoded.charCodeAt(index);
  }
  return `wf-${(hash >>> 0).toString(16).padStart(8, "0")}`;
}

function validateStep(step: WorkflowStepDefinitionPayload, issues: WorkflowValidationIssue[]): void {
  if (!step.stepId.trim()) {
    issues.push(contractIssue("Every step must declare a non-empty step identifier.", "$.steps[].stepId"));
  }
  if (!step.plan || !step.plan.planId.trim()) {
    issues.push(contractIssue("Step must declare a canonical analysis plan.", `$.steps.${step.stepId}.plan`, step.stepId));
  }
  validatePlan(step.stepId, step.plan, issues);

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

function isFiveFieldCron(value: string): boolean {
  return value.trim().split(/\s+/).length === 5;
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
