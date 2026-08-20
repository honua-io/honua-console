import { test, expect } from '@playwright/test';
import { buildConsoleReceipt, readZeroToMapFacts } from '../live/zero-to-map-receipt';

test('zero-to-map receipt keeps Studio intent, approval, audit, and three GP jobs distinct', () => {
  const plan = {
    schemaVersion: 'honua.zero-to-map.plan/v1',
    journeyId: '2026.1-zero-to-map',
    releaseContract: 'honua-release#123/D9.3',
    variables: { serviceName: 'zero-to-map', route: 'zero-to-map' },
  };
  const captures = {
    connectionId: 'connection-1',
    parcelsLayerId: 12,
    zoningLayerId: 13,
    esriMcpJobId: 'job-esri-mcp',
    gpServerJobId: 'job-gpserver',
    directAnalysisJobId: 'job-direct',
    esriMcpServiceId: 'analysis',
    esriMcpTaskName: 'Buffer',
    esriMcpProcessId: 'geometry.buffer',
    esriMcpResultPackageId: 'job-esri-mcp:v1',
    esriMcpArtifactId: 'artifact-esri',
    bufferArtifactId: 'artifact-direct',
    draftId: '11111111-1111-1111-1111-111111111111',
    proposalGeneration: 4,
  };
  const receipt = {
    schemaVersion: 'honua.zero-to-map.receipt/v1',
    journeyId: plan.journeyId,
    releaseContract: plan.releaseContract,
    mode: 'live',
    status: 'blocked',
    stages: [
      {
        actions: [
          { id: 'captured-journey', status: 'passed', captures },
          {
            id: 'buffer-esri-gpserver',
            status: 'passed',
            evidence: { resultNames: ['outputFeatureLayer'] },
          },
          { id: 'console-approval', status: 'blocked', code: 'external-receipt-missing' },
        ],
      },
    ],
  };

  const facts = readZeroToMapFacts(plan, receipt);
  const consoleReceipt = buildConsoleReceipt(
    facts,
    {
      proposalId: 'proposal-real-admin',
      candidateId: 'candidate-1',
      releaseId: 'release-1',
      shareUrl: 'https://example.test/apps/zero-to-map',
    },
    { executionOperationId: 'operation-approved', correlationId: 'correlation-job' },
  );

  expect(consoleReceipt.proposal.proposalId).toBe('proposal-real-admin');
  expect(consoleReceipt.proposal.draftId).toBe(captures.draftId);
  expect(consoleReceipt.audit).toEqual({ correlationId: 'correlation-job', operationId: 'operation-approved' });
  expect(consoleReceipt.resources.jobs).toEqual({
    esriMcp: 'job-esri-mcp',
    gpServer: 'job-gpserver',
    directAnalysis: 'job-direct',
  });
  expect(consoleReceipt.resources.gp.jobId).toBe(consoleReceipt.resources.jobs.esriMcp);
  expect(consoleReceipt.resources.gpServerResultNames).toEqual(['outputFeatureLayer']);
  expect(consoleReceipt.resources.gp).not.toHaveProperty('resultNames');
  expect(consoleReceipt.audit).not.toHaveProperty('auditId');
});
