import fs from 'node:fs';
import path from 'node:path';

export const FOCUSED_RECEIPT_SCHEMA = 'console-focused-client-receipt-v1';

export type FocusedResourceKind =
  | 'connection'
  | 'service'
  | 'layer'
  | 'gpJob'
  | 'gpResult'
  | 'savedMap'
  | 'savedDashboard'
  | 'proposal'
  | 'operation'
  | 'audit'
  | 'publication';

export interface FocusedResourceIdentity {
  kind: FocusedResourceKind;
  id: string;
  route: string;
  displayValue?: string;
  versionId?: string;
  contentHash?: string;
}

export interface TerminalJourneyReceipt {
  receiptSchema?: string;
  evidenceKey: string;
  status: string;
  server?: { image?: string; sourceSha?: string };
  resources?: Record<string, unknown>;
  stages?: Array<{ stage?: string; status?: string; evidence?: Record<string, unknown> }>;
}

export interface FocusedClientEvidence {
  schema: typeof FOCUSED_RECEIPT_SCHEMA;
  evidenceKey: 'console.focused-client';
  generatedAt: string;
  terminalReceipt: { path: string; evidenceKey: string; status: string };
  server: { image?: string; sourceSha?: string };
  inspected: Array<{ kind: FocusedResourceKind; id: string; route: string; status: 'pass' | 'fail' }>;
  approval: { status: 'blocked'; blockedBy: 'honua-server#3365' };
  status: 'pass' | 'fail';
}

const resourceKeys: ReadonlyArray<[FocusedResourceKind, string]> = [
  ['connection', 'connectionId'],
  ['service', 'serviceId'],
  ['layer', 'layerId'],
  ['gpJob', 'gpJobId'],
  ['gpResult', 'gpResultId'],
  ['savedMap', 'savedMapId'],
  ['savedDashboard', 'savedDashboardId'],
  ['proposal', 'proposalId'],
  ['operation', 'operationId'],
  ['audit', 'auditId'],
  ['publication', 'publicationId'],
];

function object(value: unknown): Record<string, unknown> {
  return value !== null && typeof value === 'object' && !Array.isArray(value)
    ? value as Record<string, unknown>
    : {};
}

function nonEmpty(value: unknown): string | undefined {
  return typeof value === 'string' && value.trim().length > 0 ? value.trim() : undefined;
}

function mergedResources(receipt: TerminalJourneyReceipt): Record<string, unknown> {
  const merged = { ...object(receipt.resources) };
  for (const stage of receipt.stages ?? []) {
    Object.assign(merged, object(object(stage.evidence).resources));
    // Live terminal receipts may put identities directly in stage evidence.
    for (const [, key] of resourceKeys) {
      if (merged[key] === undefined && stage.evidence?.[key] !== undefined) {
        merged[key] = stage.evidence[key];
      }
    }
  }
  return merged;
}

function routeFor(kind: FocusedResourceKind, id: string, resources: Record<string, unknown>): string {
  const encoded = encodeURIComponent(id);
  switch (kind) {
    case 'connection': return `/operate/connections/${encoded}`;
    case 'service': return `/operate/services/${encodeURIComponent(nonEmpty(resources.serviceName) ?? id)}/settings`;
    case 'layer': return `/operate/layers/${encoded}`;
    case 'gpJob': return `/operate/geoprocessing/${encoded}`;
    case 'gpResult': return `/operate/geoprocessing/${encodeURIComponent(nonEmpty(resources.gpJobId) ?? id)}`;
    case 'savedMap':
    case 'savedDashboard': return `/catalog/${encoded}?tab=versions`;
    case 'proposal': return `/inbox?proposalId=${encoded}`;
    case 'operation': return `/operate/deploy?operationId=${encoded}`;
    case 'audit': return `/operate/events/${encoded}`;
    case 'publication': return `/operate/publishing?publicationId=${encoded}`;
  }
}

export function parseTerminalReceipt(value: unknown): TerminalJourneyReceipt {
  const receipt = object(value);
  const evidenceKey = nonEmpty(receipt.evidenceKey);
  const status = nonEmpty(receipt.status);
  if (evidenceKey !== 'release.e2e.terminal-zero-to-map') {
    throw new Error('receipt evidenceKey must be release.e2e.terminal-zero-to-map');
  }
  if (!status) throw new Error('receipt status is required');
  if (!Array.isArray(receipt.stages) && Object.keys(object(receipt.resources)).length === 0) {
    throw new Error('receipt must contain stages or resources');
  }
  return receipt as unknown as TerminalJourneyReceipt;
}

export function loadTerminalReceipt(receiptPath: string): TerminalJourneyReceipt {
  return parseTerminalReceipt(JSON.parse(fs.readFileSync(receiptPath, 'utf8')));
}

export function focusedResourceIdentities(receipt: TerminalJourneyReceipt): FocusedResourceIdentity[] {
  const resources = mergedResources(receipt);
  return resourceKeys.flatMap(([kind, key]) => {
    const id = nonEmpty(resources[key]);
    if (!id) return [];
    const identity: FocusedResourceIdentity = { kind, id, route: routeFor(kind, id, resources) };
    if (kind === 'service') {
      identity.displayValue = nonEmpty(resources.serviceName) ?? id;
    }
    if (kind === 'savedMap' || kind === 'savedDashboard') {
      identity.versionId = nonEmpty(resources[`${key.slice(0, -2)}VersionId`]);
      identity.contentHash = nonEmpty(resources[`${key.slice(0, -2)}Hash`]);
    }
    return [identity];
  });
}

export function writeFocusedEvidence(outputPath: string, evidence: FocusedClientEvidence): void {
  fs.mkdirSync(path.dirname(outputPath), { recursive: true });
  fs.writeFileSync(outputPath, `${JSON.stringify(evidence, null, 2)}\n`, { mode: 0o600 });
}
