import { createHash } from 'node:crypto';
import { readFileSync, openSync, writeFileSync, closeSync } from 'node:fs';
import { expect, request, test } from '@playwright/test';

test('separate human approval and anonymous audience resolve the same native map version', async ({ page }) => {
  const handoffPath = process.env.HONUA_WORKSPACE_E2E_PUBLICATION_HANDOFF;
  const witnessPath = process.env.HONUA_WORKSPACE_E2E_PUBLICATION_WITNESS;
  expect(handoffPath).toBeTruthy();
  expect(witnessPath).toBeTruthy();
  const handoff = JSON.parse(readFileSync(handoffPath!, 'utf8'));
  expect(handoff.schemaVersion).toBe('honua.native-docker.publication-handoff/v1');
  expect(handoff.status).toBe('awaiting-separate-human-approval');
  expect(handoff.audience).toBe('public');
  expect(typeof handoff.requestedBy).toBe('string');
  expect(handoff.requestedBy.trim()).toBeTruthy();
  expect(handoff.requestId, 'The pending proposal must retain a canonical publication request ID for read-only reconciliation.').toBeTruthy();

  const sessionResponse = await page.request.get('/api/agent-workspace/session');
  expect(sessionResponse.status()).toBe(200);
  const session = await sessionResponse.json();
  expect(session.authenticated).toBe(true);
  expect(session.principalId).toBe(handoff.principalId);
  expect(session.environmentId).toBe(handoff.environmentId);
  const headers = {
    'X-Honua-Environment': session.environmentId as string,
    'X-Honua-Session-Partition': session.principalId as string,
  };
  const proposalResponse = await page.request.get(`/api/processes/admin/proposals/${encodeURIComponent(handoff.proposalId)}`, { headers });
  expect(proposalResponse.status()).toBe(200);
  const proposal = await proposalResponse.json();
  expect(proposal.proposalId).toBe(handoff.proposalId);
  expect(proposal.status?.toLowerCase()).toBe('succeeded');
  expect(typeof proposal.requestedBy).toBe('string');
  expect(proposal.requestedBy.trim()).toBeTruthy();
  expect(proposal.requestedBy).toBe(handoff.requestedBy);
  expect(typeof proposal.resolvedBy).toBe('string');
  expect(proposal.resolvedBy.trim()).toBeTruthy();
  expect(proposal.resolvedBy).not.toBe(proposal.requestedBy);

  // The Console proposal detail intentionally redacts internal operation
  // fields. Read the exact Server handle with the installer's separate current
  // admin:read workspace key; this never enters the browser page or receipt.
  const serverOrigin = process.env.HONUA_WORKSPACE_E2E_SERVER_ORIGIN;
  const agentKey = process.env.HONUA_WORKSPACE_E2E_AGENT_KEY;
  expect(serverOrigin).toMatch(/^http:\/\/127\.0\.0\.1:\d+$/);
  expect(agentKey).toBeTruthy();
  const agentRead = await request.newContext({ baseURL: serverOrigin,
    extraHTTPHeaders: { 'X-API-Key': agentKey!, Accept: 'application/json' } });
  try {
    const operationResponse = await agentRead.get(
      `/api/v1/operations/handles/${encodeURIComponent(handoff.operationInstanceId)}`);
    expect(operationResponse.status()).toBe(200);
    const operationWire = await operationResponse.json();
    const operation = operationWire.data ?? operationWire;
    expect(operation.operationInstanceId).toBe(handoff.operationInstanceId);
    expect(operation.operationId).toBe('studio.content.create-publication-request');
    expect(operation.proposalId).toBe(handoff.proposalId);
    expect(operation.resourceIds?.requestId).toBe(handoff.requestId);
    expect(operation.resourceIds?.itemId).toBe(handoff.itemId);
    expect(operation.resourceIds?.versionId).toBe(handoff.versionId);
  } finally { await agentRead.dispose(); }

  const read = async (path: string) => {
    const response = await page.request.get(path, { headers });
    expect(response.status()).toBe(200);
    const wire = await response.json();
    return wire.data ?? wire;
  };
  const draft = await read(`/api/studio/v1/studio/package-drafts/${encodeURIComponent(handoff.reopenedDraftId)}`);
  const version = await read(
    `/api/studio/v1/studio/content-items/${encodeURIComponent(handoff.itemId)}/versions/${encodeURIComponent(handoff.versionId)}`);
  expect(draft.draftId).toBe(handoff.reopenedDraftId);
  expect(draft.itemId).toBe(handoff.itemId);
  expect(draft.packageKey).toBe(handoff.packageKey);
  expect(version.itemId).toBe(handoff.itemId);
  expect(version.versionId).toBe(handoff.versionId);
  expect(version.contentHash).toBe(handoff.contentHash);
  expect(draft.envelope?.body).toEqual(version.envelope?.body);
  expect(createHash('sha256').update(JSON.stringify(version.envelope.body)).digest('hex')).toBe(handoff.bodySha256);
  expect(draft.envelope?.body?.layers?.some((layer: { sourceId?: string }) =>
    layer.sourceId === handoff.sourceId)).toBe(true);

  const publicationResponse = await page.request.get(
    `/api/studio/v1/studio/content-items/${encodeURIComponent(handoff.itemId)}/versions/${encodeURIComponent(handoff.versionId)}/publish-requests/${encodeURIComponent(handoff.requestId)}`,
    { headers },
  );
  expect(publicationResponse.status()).toBe(200);
  const publicationWire = await publicationResponse.json();
  const publication = publicationWire.data ?? publicationWire;
  expect(publication.requestId).toBe(handoff.requestId);
  expect(publication.itemId).toBe(handoff.itemId);
  expect(publication.versionId).toBe(handoff.versionId);
  expect(publication.status?.toLowerCase()).toBe('accepted');
  expect(publication.intent?.route).toBe(handoff.route);
  expect(publication.intent?.visibility).toBe('public');
  const publicPath = `/api/v1/studio/published${handoff.route}`;

  const anonymous = await request.newContext({ baseURL: serverOrigin,
    extraHTTPHeaders: { Accept: 'application/json' } });
  try {
    const audienceResponse = await anonymous.get(publicPath);
    expect(audienceResponse.status()).toBe(200);
    const audienceWire = await audienceResponse.json();
    const artifact = audienceWire.data ?? audienceWire;
    expect(artifact.itemId).toBe(handoff.itemId);
    expect(artifact.publicationId).toBe(handoff.requestId);
    expect(artifact.versionId).toBe(handoff.versionId);
    expect(artifact.contentHash).toBe(handoff.contentHash);
    expect(artifact.packageKey).toBe(handoff.packageKey);
    expect(artifact.route).toBe(handoff.route);
    expect(artifact.visibility).toBe('public');
    expect(artifact.envelope?.body?.layers?.some((layer: { sourceId?: string }) =>
      layer.sourceId === handoff.sourceId)).toBe(true);
    const receipt = { schemaVersion: 'honua.native-docker.audience-witness/v1',
      status: 'pass', proposalId: handoff.proposalId, requestedBy: proposal.requestedBy,
      resolvedBy: proposal.resolvedBy, commandId: handoff.commandId,
      operationInstanceId: handoff.operationInstanceId,
      requestId: handoff.requestId, itemId: artifact.itemId, versionId: artifact.versionId,
      contentHash: artifact.contentHash, route: artifact.route, publicationUrl: serverOrigin + publicPath };
    const fd = openSync(witnessPath!, 'wx', 0o600);
    try { writeFileSync(fd, JSON.stringify(receipt, null, 2) + '\n'); }
    finally { closeSync(fd); }
  } finally { await anonymous.dispose(); }
});
