import { createHash } from 'node:crypto';
import { spawn, spawnSync } from 'node:child_process';
import { createServer } from 'node:http';
import { mkdtemp, readFile, rm, writeFile } from 'node:fs/promises';
import { join } from 'node:path';
import { tmpdir } from 'node:os';
import { fileURLToPath } from 'node:url';
import { test, expect } from '@playwright/test';
import {
  browserTransport,
  produceConsoleReceipts,
  readConsoleBoundary,
} from '../live/console-receipt-producer.mjs';

const endpoint = 'http://127.0.0.1:5174';
const families = ['map', 'app', 'dashboard'] as const;

test('browser harness approves exact checkpoint-bound map/app/dashboard proposals and emits both strict receipts', async ({ page }) => {
  const { checkpoint, handoff } = candidateBoundary(endpoint);
  const approved = new Set<string>();
  const authorization: string[] = [];
  const posts: string[] = [];

  await page.route('**/*', async (route) => {
    const request = route.request();
    const url = new URL(request.url());
    const auth = request.headers().authorization;
    if (auth) authorization.push(auth);
    const body = responseFor(url, request.method(), approved, posts);
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(body) });
  });
  await page.goto(`${endpoint}/healthz/ready`);

  const boundary = readConsoleBoundary(checkpoint, handoff);
  const { releaseReceipt, sdkReceipt } = await produceConsoleReceipts({
    endpoint,
    credential: 'scoped-test-token',
    mode: 'full',
    boundary,
    transport: browserTransport(page),
  });

  expect(posts).toEqual(families.map((family) => `/api/v1/admin/proposals/${family}-proposal/approve`));
  expect(releaseReceipt.proposals).toEqual(expect.objectContaining({
    map: expect.objectContaining({ proposalId: 'map-proposal', executionOperationId: 'map-operation' }),
    app: expect.objectContaining({ proposalId: 'app-proposal', executionOperationId: 'app-operation' }),
    dashboard: expect.objectContaining({ proposalId: 'dashboard-proposal', executionOperationId: 'dashboard-operation' }),
  }));
  expect(releaseReceipt.publications.app.publicUrl).toBe(`${endpoint}/api/v1/published/app-route`);
  expect(releaseReceipt.audit.app.operationId).toBe(releaseReceipt.proposals.app.executionOperationId);
  expect(releaseReceipt.resources.gp.jobId).toBe(releaseReceipt.resources.jobs.esriMcp);
  expect(sdkReceipt.proposal).toEqual({ ...releaseReceipt.proposals.app, draftId: 'app-original-draft' });
  expect(sdkReceipt.audit).toEqual(releaseReceipt.audit.app);
  expect(sdkReceipt.resources.draftId).toBe('app-original-draft');
  expect(sdkReceipt.resources).not.toHaveProperty('studio');
  expect(JSON.stringify({ releaseReceipt, sdkReceipt })).not.toContain('scoped-test-token');
  expect(authorization.every((value) => value === 'Bearer scoped-test-token')).toBeTruthy();
});

test('witness mode stays read-only and refuses a pending candidate', async () => {
  const fixture = candidateBoundary();
  const boundary = readConsoleBoundary(fixture.checkpoint, fixture.pausedReceipt);
  await expect(produceConsoleReceipts({
    endpoint,
    credential: 'witness-token',
    mode: 'witness',
    boundary,
    transport: mockTransport(new Set(), []),
  })).rejects.toThrow('witness mode cannot approve pending map proposal');
});

test('witness mode emits evidence for already-resolved proposals without mutation', async () => {
  const fixture = candidateBoundary();
  const boundary = readConsoleBoundary(fixture.checkpoint, fixture.pausedReceipt);
  const posts: string[] = [];
  const approved = new Set<string>(families);
  const { releaseReceipt } = await produceConsoleReceipts({
    endpoint,
    credential: 'witness-token',
    mode: 'witness',
    boundary,
    transport: mockTransport(approved, posts),
  });
  expect(releaseReceipt.status).toBe('passed');
  expect(posts).toEqual([]);
});

test('candidate boundary rejects tampering and pre-Console failures', () => {
  const fixture = candidateBoundary();
  fixture.checkpoint.resume.capturedVariables.connectionId = 'tampered';
  expect(() => readConsoleBoundary(fixture.checkpoint, fixture.pausedReceipt)).toThrow('checkpoint.integrity.digest');

  const second = candidateBoundary();
  second.pausedReceipt.stages[0].actions[0].status = 'failed';
  expect(() => readConsoleBoundary(second.checkpoint, second.pausedReceipt)).toThrow('pre-Console action create-connection is failed');

  const third = candidateBoundary(endpoint);
  third.handoff.candidateId = 'manifest-sha256:tampered';
  expect(() => readConsoleBoundary(third.checkpoint, third.handoff)).toThrow('Studio handoff.candidateId');
});

test('CLI fails closed without endpoint, scoped credential, and candidate inputs', () => {
  const cli = new URL('../live/console-receipt-cli.mjs', import.meta.url);
  const result = spawnSync(process.execPath, [fileURLToPath(cli)], {
    encoding: 'utf8',
    env: { PATH: process.env.PATH ?? '' },
  });
  expect(result.status).toBe(1);
  expect(result.stderr).toContain('endpoint is required');
  expect(result.stdout).toBe('');
});

test('CLI refuses broad admin-key credentials before reading candidate files', () => {
  const cli = new URL('../live/console-receipt-cli.mjs', import.meta.url);
  const result = spawnSync(process.execPath, [
    fileURLToPath(cli), '--endpoint', endpoint, '--checkpoint', 'missing-checkpoint.json',
    '--pre-console-evidence', 'missing-handoff.json', '--output', 'unused-receipt.json',
  ], {
    encoding: 'utf8',
    env: { PATH: process.env.PATH ?? '', HONUA_ADMIN_KEY: 'must-not-be-used', HONUA_AI_ARC_CONSOLE_TOKEN: 'scoped' },
  });
  expect(result.status).toBe(1);
  expect(result.stderr).toContain('broad admin-key variables are not accepted');
  expect(result.stderr).not.toContain('must-not-be-used');
});

test('CLI consumes the sealed Studio handoff and writes secret-free release and SDK artifacts', async () => {
  const root = await mkdtemp(join(tmpdir(), 'honua-console-receipt-'));
  const approved = new Set<string>();
  const posts: string[] = [];
  const server = createServer((request, response) => {
    try {
      const url = new URL(request.url!, `http://${request.headers.host}`);
      const body = responseFor(url, request.method ?? 'GET', approved, posts);
      response.writeHead(200, { 'content-type': 'application/json' });
      response.end(JSON.stringify(body));
    } catch (error) {
      response.writeHead(500, { 'content-type': 'application/json' });
      response.end(JSON.stringify({ error: error instanceof Error ? error.message : 'mock failure' }));
    }
  });
  await new Promise<void>((resolve) => server.listen(0, '127.0.0.1', resolve));
  try {
    const address = server.address();
    if (!address || typeof address === 'string') throw new Error('mock server did not bind TCP');
    const candidateEndpoint = `http://127.0.0.1:${address.port}`;
    const fixture = candidateBoundary(candidateEndpoint);
    const checkpointPath = join(root, 'checkpoint.json');
    const handoffPath = join(root, 'studio-handoff.json');
    const releasePath = join(root, 'release.json');
    const sdkPath = join(root, 'sdk.json');
    await writeFile(checkpointPath, JSON.stringify(fixture.checkpoint), 'utf8');
    await writeFile(handoffPath, JSON.stringify(fixture.handoff), 'utf8');
    const cli = fileURLToPath(new URL('../live/console-receipt-cli.mjs', import.meta.url));
    const env = { ...process.env };
    delete env.HONUA_ADMIN_KEY;
    delete env.HONUA_API_KEY;
    Object.assign(env, {
      HONUA_AI_ARC_ENDPOINT: candidateEndpoint,
      HONUA_AI_ARC_CHECKPOINT: checkpointPath,
      HONUA_AI_ARC_REAL_MODEL_EVIDENCE: handoffPath,
      HONUA_AI_ARC_CONSOLE_RECEIPT: releasePath,
      HONUA_AI_ARC_SDK_CONSOLE_RECEIPT: sdkPath,
      HONUA_AI_ARC_CONSOLE_TOKEN: 'cli-scoped-token',
    });
    const result = await run(cli, env);
    expect(result.code, result.stderr).toBe(0);
    const release = await readFile(releasePath, 'utf8');
    const sdk = await readFile(sdkPath, 'utf8');
    expect(JSON.parse(release).proposals.dashboard.proposalId).toBe('dashboard-proposal');
    expect(JSON.parse(sdk).proposal.proposalId).toBe('app-proposal');
    expect(release + sdk + result.stdout + result.stderr).not.toContain('cli-scoped-token');
  } finally {
    await new Promise<void>((resolve, reject) => server.close((error) => error ? reject(error) : resolve()));
    await rm(root, { recursive: true, force: true });
  }
});

function candidateBoundary(candidateEndpoint = endpoint) {
  const captures: Record<string, unknown> = {
    candidateId: `manifest-sha256:${'a'.repeat(64)}`,
    releaseId: '2026.1-rc.2',
    connectionId: 'connection-1', serviceName: 'zero-to-map', parcelsLayerId: 1, zoningLayerId: 2,
    esriMcpJobId: 'esri-job', gpServerJobId: 'gpserver-job', directAnalysisJobId: 'direct-job',
    esriMcpServiceId: 'analysis', esriMcpTaskName: 'Buffer', esriMcpProcessId: 'geometry.buffer',
    esriMcpResultPackageId: 'esri-job:v1', esriMcpArtifactId: 'esri-artifact', bufferArtifactId: 'direct-artifact',
  };
  for (const [index, family] of families.entries()) {
    Object.assign(captures, {
      [`${family}DraftId`]: `${family}-original-draft`,
      [`${family}ItemId`]: `${family}-item`,
      [`${family}VersionId`]: `${family}-version`,
      [`${family}ContentHash`]: `${family}-content-hash`,
      [`${family}ReopenedDraftId`]: `${family}-reopened-draft`,
      [`${family}ProposalGeneration`]: index + 2,
      [`${family}PublicationVersionId`]: `${family}-publication-version`,
      [`${family}PublicationContentHash`]: `${family}-publication-hash`,
      [`${family}Route`]: `${family}-route`,
    });
  }
  const checkpoint: any = {
    schemaVersion: 'honua.zero-to-map.checkpoint/v1', state: 'paused', target: 'local-docker',
    journeyId: '2026.1-zero-to-map', releaseContract: 'honua-release#123/D9.3',
    sourceRevision: '2'.repeat(40),
    candidateId: captures.candidateId, releaseId: captures.releaseId,
    resume: {
      resumeAt: { stageId: 'console', actionId: 'console-approval' },
      capturedVariables: captures,
    },
    consoleReceiptRequest: {
      schemaVersion: 'honua.zero-to-map.console-receipt-request/v1',
      actionId: 'console-approval', receiptSchema: 'honua.zero-to-map.console-receipt/v1',
      matches: {
        '/journeyId': '2026.1-zero-to-map', '/releaseContract': 'honua-release#123/D9.3', '/status': 'passed',
        '/candidate/candidateId': captures.candidateId, '/candidate/releaseId': captures.releaseId,
        '/resources/connectionId': captures.connectionId,
      },
      requiredPointers: families.flatMap((family) => [
        `/proposals/${family}/proposalId`, `/proposals/${family}/executionOperationId`,
        `/publications/${family}/publicationId`, `/publications/${family}/publicUrl`,
        `/audit/${family}/correlationId`,
      ]).concat(['/shareUrl']),
      equalPointers: families.flatMap((family) => [
        [`/proposals/${family}/proposalId`, `/publications/${family}/requestId`],
        [`/proposals/${family}/executionOperationId`, `/audit/${family}/operationId`],
      ]).concat([[`/resources/gp/jobId`, `/resources/jobs/esriMcp`], ['/shareUrl', '/publications/app/publicUrl']]),
    },
  };
  checkpoint.integrity = { algorithm: 'sha256', digest: digest(checkpoint) };
  const pausedReceipt: any = {
    schemaVersion: 'honua.zero-to-map.receipt/v1', mode: 'live', status: 'blocked',
    journeyId: checkpoint.journeyId, releaseContract: checkpoint.releaseContract,
    stages: [
      { actions: [{ id: 'create-connection', status: 'passed', captures }] },
      { actions: [{ id: 'buffer-esri-gpserver', status: 'passed', evidence: { resultNames: ['outputFeatureLayer'] } }] },
      { actions: [{ id: 'console-approval', status: 'blocked', code: 'external-receipt-missing' }] },
      { actions: [{ id: 'verify-share-url', status: 'skipped' }] },
    ],
  };
  const components = Object.fromEntries(
    ['honua-server', 'honua-sdk-js', 'honua-console', 'honua-studio', 'honua-devops', 'honua-iac']
      .map((name, index) => [name, name === 'honua-sdk-js' ? checkpoint.sourceRevision : String(index + 1).repeat(40)]),
  );
  const lanes = Object.fromEntries(['admin', 'esriGp', 'nativeAnalysis', 'studioPublication'].map((name) => [name, {
    promptSha256: 'b'.repeat(64), transcriptSha256: 'c'.repeat(64), calls: [{ status: 'passed' }],
  }]));
  const handoff: any = {
    schemaVersion: 'honua.studio.real-model-ai-arc-handoff/v1', status: 'paused', target: checkpoint.target,
    candidateId: checkpoint.candidateId, releaseId: checkpoint.releaseId,
    endpointSha256: createHash('sha256').update(candidateEndpoint.replace(/\/$/, '')).digest('hex'),
    source: { repository: 'honua-io/honua-studio', sha: components['honua-studio'] }, components,
    model: { provider: 'bedrock', modelId: 'claude-sonnet' }, promptVersion: '2026.1-v1', evalVersion: '2026.1-v1',
    transcriptSha256: 'd'.repeat(64),
    deterministic: { target: checkpoint.target, checkpointDigest: checkpoint.integrity.digest },
    lanes, joins: { ...captures }, consoleReceiptRequest: checkpoint.consoleReceiptRequest,
  };
  handoff.integrity = { algorithm: 'sha256', digest: digest(handoff) };
  return { checkpoint, pausedReceipt, handoff };
}

function mockTransport(approved: Set<string>, posts: string[]) {
  return async ({ url, method, headers }: any) => ({
    status: 200,
    body: JSON.stringify(responseFor(new URL(url), method, approved, posts, headers)),
  });
}

function responseFor(url: URL, method: string, approved: Set<string>, posts: string[], _headers?: unknown): any {
  const path = url.pathname;
  if (path === '/healthz/ready') return { status: 'Healthy' };
  if (path === '/api/v1/admin/version') return { data: { version: '2026.1.0-test' } };
  if (path === '/api/v1/admin/connections/') return { data: [{ connectionId: 'connection-1' }] };
  if (path === '/api/v1/admin/services/') return { data: [{ serviceName: 'zero-to-map' }] };
  if (path === '/api/v1/admin/connections/connection-1/layers/') return { data: [{ layerId: 1 }, { layerId: 2 }] };
  if (path.startsWith('/api/v1/admin/jobs/')) {
    const jobId = decodeURIComponent(path.split('/').at(-1)!);
    return jobId === 'esri-job'
      ? { jobId, selectedMetadata: { serviceId: 'analysis', taskName: 'Buffer', processId: 'geometry.buffer', resultPackageId: 'esri-job:v1' }, evidence: ['esri-artifact'] }
      : { jobId, evidence: jobId === 'direct-job' ? ['direct-artifact'] : [] };
  }
  if (path === '/api/v1/admin/proposals') return { proposals: families.map((family) => ({ proposalId: `${family}-proposal` })) };
  const proposalMatch = path.match(/^\/api\/v1\/admin\/proposals\/(map|app|dashboard)-proposal(\/approve)?$/);
  if (proposalMatch) {
    const family = proposalMatch[1];
    if (method === 'POST') { approved.add(family); posts.push(path); }
    return proposal(family, approved.has(family));
  }
  if (path === '/api/v1/studio/content-items') {
    const family = url.searchParams.get('family')!;
    return { items: [{ itemId: `${family}-item`, publishedVersionId: `${family}-publication-version`, publication: { publicationId: `${family}-publication`, routePath: `/api/v1/published/${family}-route` } }] };
  }
  const publicationMatch = path.match(/^\/api\/v1\/console\/publications\/(map|app|dashboard)-publication$/);
  if (publicationMatch) return publication(publicationMatch[1]);
  if (path === '/api/v1/admin/observability/audit') {
    const proposalId = url.searchParams.get('resourceId')!;
    const family = proposalId.split('-')[0];
    return { items: [{ resourceId: proposalId, action: 'operation.applied', outcome: 'Success', correlationId: `${family}-correlation`, details: JSON.stringify({ executionOperationId: `${family}-operation` }) }] };
  }
  if (path === '/api/v1/admin/observability/audit/verify') return { verified: true };
  if (path.startsWith('/api/v1/published/')) return { status: 'published' };
  throw new Error(`unexpected mock request ${method} ${url}`);
}

function proposal(family: string, isApproved: boolean) {
  return {
    proposalId: `${family}-proposal`, status: isApproved ? 'Submitted' : 'AwaitingApproval',
    executionOperationId: isApproved ? `${family}-operation` : null,
    summary: `${family}-item ${family}-publication-version ${family}-reopened-draft ${family}-route`,
    diff: [], dryRun: [],
  };
}

function publication(family: string) {
  return {
    route: { publicationId: `${family}-publication`, activeVersionId: `${family}-active`, routePath: `/api/v1/published/${family}-route` },
    versions: [{
      versionId: `${family}-active`, sourceContentId: `${family}-item`, contentVersionId: `${family}-publication-version`,
      contentHash: `${family}-publication-hash`, operationId: `${family}-operation`,
    }],
  };
}

function digest(checkpoint: any) {
  return createHash('sha256').update(canonical(checkpoint)).digest('hex');
}

function run(cli: string, env: NodeJS.ProcessEnv): Promise<{ code: number | null; stdout: string; stderr: string }> {
  return new Promise((resolve, reject) => {
    const child = spawn(process.execPath, [cli], { env, stdio: ['ignore', 'pipe', 'pipe'] });
    let stdout = '';
    let stderr = '';
    child.stdout.setEncoding('utf8').on('data', (chunk) => { stdout += chunk; });
    child.stderr.setEncoding('utf8').on('data', (chunk) => { stderr += chunk; });
    child.once('error', reject);
    child.once('close', (code) => resolve({ code, stdout, stderr }));
  });
}

function canonical(value: any): string {
  if (Array.isArray(value)) return `[${value.map(canonical).join(',')}]`;
  if (value && typeof value === 'object') return `{${Object.keys(value).sort().map((key) => `${JSON.stringify(key)}:${canonical(value[key])}`).join(',')}}`;
  return JSON.stringify(value);
}
