import { test, expect } from '@playwright/test';
import { focusedResourceIdentities, parseTerminalReceipt } from '../receipt';

test('projects exact terminal identities onto existing focused read routes', () => {
  const receipt = parseTerminalReceipt({
    receiptSchema: 'terminal-journey-receipt-v1',
    evidenceKey: 'release.e2e.terminal-zero-to-map',
    status: 'paused',
    resources: {
      connectionId: 'conn/a', serviceId: 'svc-1', serviceName: 'parcels prod', layerId: 'layer-1',
      gpJobId: 'job-1', gpResultId: 'result-1', savedMapId: 'map-1', savedMapVersionId: 'v7',
      savedMapHash: 'sha256:map', savedDashboardId: 'dash-1', savedDashboardVersionId: 'v4',
      savedDashboardHash: 'sha256:dashboard', proposalId: 'proposal-1', operationId: 'operation-1',
      auditId: 'audit-1', publicationId: 'publication-1',
    },
  });

  const identities = focusedResourceIdentities(receipt);
  expect(identities).toHaveLength(11);
  expect(identities.find(({ kind }) => kind === 'connection')?.route).toBe('/operate/connections/conn%2Fa');
  expect(identities.find(({ kind }) => kind === 'service')?.route).toBe('/operate/services/parcels%20prod/settings');
  expect(identities.find(({ kind }) => kind === 'proposal')?.route).toBe('/inbox?proposalId=proposal-1');
  expect(identities.find(({ kind }) => kind === 'savedMap')).toMatchObject({ versionId: 'v7', contentHash: 'sha256:map' });
});

test('accepts identities emitted inside stage evidence', () => {
  const receipt = parseTerminalReceipt({
    evidenceKey: 'release.e2e.terminal-zero-to-map', status: 'pass',
    stages: [{ stage: 'bounded-gp', status: 'pass', evidence: { gpJobId: 'job-9', resources: { gpResultId: 'result-9' } } }],
  });
  expect(focusedResourceIdentities(receipt)).toEqual([
    { kind: 'gpJob', id: 'job-9', route: '/operate/geoprocessing/job-9' },
    { kind: 'gpResult', id: 'result-9', route: '/operate/geoprocessing/job-9' },
  ]);
});

test('rejects unrelated or identity-free receipts', () => {
  expect(() => parseTerminalReceipt({ evidenceKey: 'other', status: 'pass', resources: { connectionId: 'x' } })).toThrow(/evidenceKey/);
  expect(() => parseTerminalReceipt({ evidenceKey: 'release.e2e.terminal-zero-to-map', status: 'pass' })).toThrow(/stages or resources/);
});
