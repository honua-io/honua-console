import type { Page } from '@playwright/test';

const PLAN_SCHEMA = 'honua.zero-to-map.plan/v1';
const RECEIPT_SCHEMA = 'honua.zero-to-map.receipt/v1';
const CONSOLE_RECEIPT_SCHEMA = 'honua.zero-to-map.console-receipt/v1';

type JsonRecord = Record<string, unknown>;

interface ActionReceipt {
  readonly id: string;
  readonly status: string;
  readonly code?: string;
  readonly captures?: JsonRecord;
  readonly evidence?: JsonRecord;
}

export interface ZeroToMapFacts {
  readonly journeyId: string;
  readonly releaseContract: string;
  readonly connectionId: string;
  readonly serviceId: string;
  readonly layerIds: { readonly parcels: number; readonly zoning: number };
  readonly jobs: { readonly esriMcp: string; readonly gpServer: string; readonly directAnalysis: string };
  readonly gp: {
    readonly jobId: string;
    readonly serviceId: string;
    readonly taskName: string;
    readonly processId: string;
    readonly resultPackageId: string;
    readonly artifactId: string;
  };
  readonly gpServerResultNames?: readonly string[];
  readonly artifactId: string;
  readonly draftId: string;
  readonly generation: number;
  readonly route: string;
}

export interface ConsoleReceiptInputs {
  readonly proposalId: string;
  readonly candidateId: string;
  readonly releaseId: string;
  readonly shareUrl: string;
}

export interface ConsoleReceiptObservations {
  readonly executionOperationId: string;
  readonly correlationId: string;
}

export interface ZeroToMapConsoleReceipt {
  readonly schemaVersion: typeof CONSOLE_RECEIPT_SCHEMA;
  readonly journeyId: string;
  readonly releaseContract: string;
  readonly status: 'passed';
  readonly proposal: {
    readonly draftId: string;
    readonly generation: number;
    readonly route: string;
    readonly proposalId: string;
    readonly executionOperationId: string;
  };
  readonly audit: { readonly correlationId: string; readonly operationId: string };
  readonly resources: {
    readonly connectionId: string;
    readonly serviceId: string;
    readonly layerIds: { readonly parcels: number; readonly zoning: number };
    readonly jobs: { readonly esriMcp: string; readonly gpServer: string; readonly directAnalysis: string };
    readonly gp: ZeroToMapFacts['gp'];
    readonly gpServerResultNames?: readonly string[];
    readonly artifactId: string;
    readonly draftId: string;
  };
  readonly candidate: { readonly candidateId: string; readonly releaseId: string };
  readonly checks: { readonly health: 'passed'; readonly audit: 'passed'; readonly recovery: 'passed' };
  readonly shareUrl: string;
}

/**
 * Joins the checked-in SDK plan to one passed live execution receipt. The plan supplies stable
 * contract values; every runtime identity comes from an action capture/evidence value. Missing
 * evidence is a hard failure, never a best-effort guess.
 */
export function readZeroToMapFacts(planValue: unknown, receiptValue: unknown): ZeroToMapFacts {
  const plan = record(planValue, 'plan');
  const receipt = record(receiptValue, 'receipt');
  requireEqual(plan.schemaVersion, PLAN_SCHEMA, 'plan.schemaVersion');
  requireEqual(receipt.schemaVersion, RECEIPT_SCHEMA, 'receipt.schemaVersion');
  requireEqual(receipt.mode, 'live', 'receipt.mode');

  const journeyId = stringValue(plan.journeyId, 'plan.journeyId');
  const releaseContract = stringValue(plan.releaseContract, 'plan.releaseContract');
  requireEqual(receipt.journeyId, journeyId, 'receipt.journeyId');
  requireEqual(receipt.releaseContract, releaseContract, 'receipt.releaseContract');

  const actions = receiptActions(receipt);
  if (receipt.status === 'passed') {
    for (const candidate of actions) {
      if (candidate.status !== 'passed') {
        throw new Error(`passed receipt action ${candidate.id} is ${candidate.status}`);
      }
    }
  } else if (receipt.status === 'blocked') {
    // This is the normal input to the Console gate: stages 1-5 passed, then the SDK runner
    // stopped exactly because the external Console receipt does not exist yet. Accept no other
    // blocker, failure, or skipped prerequisite.
    const consoleIndex = actions.findIndex((candidate) => candidate.id === 'console-approval');
    if (consoleIndex < 0) throw new Error('blocked receipt is missing the console-approval action');
    const consoleAction = actions[consoleIndex];
    if (consoleAction.status !== 'blocked' || consoleAction.code !== 'external-receipt-missing') {
      throw new Error('console-approval must be blocked only by external-receipt-missing');
    }
    for (const candidate of actions.slice(0, consoleIndex)) {
      if (candidate.status !== 'passed') {
        throw new Error(`Console prerequisite ${candidate.id} is ${candidate.status}, not passed`);
      }
    }
    for (const candidate of actions.slice(consoleIndex + 1)) {
      if (candidate.status !== 'skipped') {
        throw new Error(`post-Console action ${candidate.id} must be skipped until the receipt exists`);
      }
    }
  } else {
    throw new Error(`receipt.status must be blocked at Console or passed; received ${String(receipt.status)}`);
  }

  const variables = record(plan.variables, 'plan.variables');
  const serviceId = stringValue(variables.serviceName, 'plan.variables.serviceName');
  const route = stringValue(variables.route, 'plan.variables.route');
  const esriMcpJobId = capturedString(actions, 'esriMcpJobId');
  const gpServerJobId = capturedString(actions, 'gpServerJobId');
  const directAnalysisJobId = capturedString(actions, 'directAnalysisJobId');
  const gpServerEvidence = action(actions, 'buffer-esri-gpserver').evidence;
  const resultNames = optionalStringArray(gpServerEvidence?.resultNames);

  return {
    journeyId,
    releaseContract,
    connectionId: capturedString(actions, 'connectionId'),
    serviceId,
    layerIds: {
      parcels: capturedInteger(actions, 'parcelsLayerId'),
      zoning: capturedInteger(actions, 'zoningLayerId'),
    },
    jobs: {
      esriMcp: esriMcpJobId,
      gpServer: gpServerJobId,
      directAnalysis: directAnalysisJobId,
    },
    gp: {
      jobId: esriMcpJobId,
      serviceId: capturedString(actions, 'esriMcpServiceId'),
      taskName: capturedString(actions, 'esriMcpTaskName'),
      processId: capturedString(actions, 'esriMcpProcessId'),
      resultPackageId: capturedString(actions, 'esriMcpResultPackageId'),
      artifactId: capturedString(actions, 'esriMcpArtifactId'),
    },
    ...(resultNames.length > 0 ? { gpServerResultNames: resultNames } : {}),
    artifactId: capturedString(actions, 'bufferArtifactId'),
    draftId: capturedString(actions, 'draftId'),
    generation: capturedInteger(actions, 'proposalGeneration'),
    route,
  };
}

export function buildConsoleReceipt(
  facts: ZeroToMapFacts,
  inputs: ConsoleReceiptInputs,
  observations: ConsoleReceiptObservations,
): ZeroToMapConsoleReceipt {
  const proposalId = nonEmpty(inputs.proposalId, 'proposalId');
  const executionOperationId = nonEmpty(observations.executionOperationId, 'executionOperationId');
  const correlationId = nonEmpty(observations.correlationId, 'correlationId');
  const candidateId = nonEmpty(inputs.candidateId, 'candidateId');
  const releaseId = nonEmpty(inputs.releaseId, 'releaseId');
  const shareUrl = validateHttpUrl(inputs.shareUrl);

  return {
    schemaVersion: CONSOLE_RECEIPT_SCHEMA,
    journeyId: facts.journeyId,
    releaseContract: facts.releaseContract,
    status: 'passed',
    proposal: {
      draftId: facts.draftId,
      generation: facts.generation,
      route: facts.route,
      proposalId,
      executionOperationId,
    },
    audit: { correlationId, operationId: executionOperationId },
    resources: {
      connectionId: facts.connectionId,
      serviceId: facts.serviceId,
      layerIds: facts.layerIds,
      jobs: facts.jobs,
      gp: facts.gp,
      ...(facts.gpServerResultNames ? { gpServerResultNames: facts.gpServerResultNames } : {}),
      artifactId: facts.artifactId,
      draftId: facts.draftId,
    },
    candidate: { candidateId, releaseId },
    checks: { health: 'passed', audit: 'passed', recovery: 'passed' },
    shareUrl,
  };
}

/** Read one exact identifier rendered by the shared correlation chip. */
export async function correlationChipValue(page: Page, kind: 'CorrelationId' | 'OperationId'): Promise<string> {
  const value = await page
    .locator(`[data-correlation-chip][data-correlation-kind="${kind}"]`)
    .first()
    .getAttribute('data-correlation-id');
  return nonEmpty(value, `${kind} rendered by Console`);
}

function receiptActions(receipt: JsonRecord): ActionReceipt[] {
  if (!Array.isArray(receipt.stages)) throw new Error('receipt.stages must be an array');
  const actions: ActionReceipt[] = [];
  for (const [stageIndex, stageValue] of receipt.stages.entries()) {
    const stage = record(stageValue, `receipt.stages[${stageIndex}]`);
    if (!Array.isArray(stage.actions)) throw new Error(`receipt.stages[${stageIndex}].actions must be an array`);
    for (const [actionIndex, value] of stage.actions.entries()) {
      const candidate = record(value, `receipt.stages[${stageIndex}].actions[${actionIndex}]`);
      actions.push({
        id: stringValue(candidate.id, `receipt action id`),
        status: stringValue(candidate.status, `receipt action status`),
        ...(candidate.code === undefined ? {} : { code: stringValue(candidate.code, 'receipt action code') }),
        ...(candidate.captures === undefined ? {} : { captures: record(candidate.captures, 'action.captures') }),
        ...(candidate.evidence === undefined ? {} : { evidence: record(candidate.evidence, 'action.evidence') }),
      });
    }
  }
  return actions;
}

function action(actions: readonly ActionReceipt[], id: string): ActionReceipt {
  const match = actions.find((candidate) => candidate.id === id);
  if (!match) throw new Error(`receipt is missing action ${id}`);
  return match;
}

function captured(actions: readonly ActionReceipt[], variable: string): unknown {
  for (const candidate of actions) {
    if (candidate.captures && Object.prototype.hasOwnProperty.call(candidate.captures, variable)) {
      return candidate.captures[variable];
    }
  }
  throw new Error(`receipt is missing required capture ${variable}`);
}

function capturedString(actions: readonly ActionReceipt[], variable: string): string {
  return stringValue(captured(actions, variable), `receipt capture ${variable}`);
}

function capturedInteger(actions: readonly ActionReceipt[], variable: string): number {
  const value = captured(actions, variable);
  const number = typeof value === 'number' ? value : typeof value === 'string' ? Number(value) : Number.NaN;
  if (!Number.isInteger(number) || number < 0) {
    throw new Error(`receipt capture ${variable} must be a non-negative integer`);
  }
  return number;
}

function optionalStringArray(value: unknown): readonly string[] {
  if (value === undefined) return [];
  if (!Array.isArray(value) || value.some((item) => typeof item !== 'string' || item.trim().length === 0)) {
    throw new Error('GPServer evidence.resultNames must be an array of non-empty strings when present');
  }
  return value.map((item) => item.trim());
}

function record(value: unknown, label: string): JsonRecord {
  if (!value || typeof value !== 'object' || Array.isArray(value)) throw new Error(`${label} must be an object`);
  return value as JsonRecord;
}

function stringValue(value: unknown, label: string): string {
  if (typeof value !== 'string') throw new Error(`${label} must be a string`);
  return nonEmpty(value, label);
}

function nonEmpty(value: string | null | undefined, label: string): string {
  const trimmed = value?.trim();
  if (!trimmed) throw new Error(`${label} must not be empty`);
  return trimmed;
}

function requireEqual(actual: unknown, expected: unknown, label: string): void {
  if (actual !== expected) throw new Error(`${label} must be ${String(expected)}; received ${String(actual)}`);
}

function validateHttpUrl(value: string): string {
  const url = new URL(nonEmpty(value, 'shareUrl'));
  if (url.protocol !== 'http:' && url.protocol !== 'https:') throw new Error('shareUrl must use HTTP or HTTPS');
  return url.toString();
}
