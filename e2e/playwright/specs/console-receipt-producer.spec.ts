import { createHash } from 'node:crypto';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { test, expect } from '@playwright/test';
import { buildConsoleEvidence, produceConsoleReceiptInBrowser } from '../live/console-receipt-browser.mjs';
import { buildReceiptAliasBytes } from '../live/console-receipt-output.mjs';
import { observeConsoleApiWitness, readConsoleBoundary } from '../live/console-receipt-producer.mjs';

const consoleOrigin = 'http://console.test';
const serverEndpoint = 'https://server.example';
const families = ['map', 'app', 'dashboard'] as const;

test('real browser drives exact Console UI approvals and emits one SDK-owned aggregate', async ({ page }) => {
  const fixture = candidateBoundary();
  const approved = new Set<string>();
  await installConsoleUi(page, fixture, approved);

  const boundary = readConsoleBoundary(fixture.checkpoint, fixture.handoff);
  const produced = await produceConsoleReceiptInBrowser({
    page,
    consoleOrigin,
    serverEndpoint,
    mode: 'full',
    boundary,
    receiptSchema: receiptSchemaFixture(),
  });

  expect([...approved]).toEqual(families);
  expect(produced.aggregate.proposals.app).toEqual(expect.objectContaining({
    proposalId: 'app-proposal',
    executionOperationId: 'app-operation',
  }));
  expect(produced.aggregate.audit.app.operationId).toBe('app-operation');
  expect(produced.aggregate.publications.app.publicUrl).toBe('https://server.example/share/app-route');
  expect(produced.aggregate.resources.gp.jobId).toBe(produced.aggregate.resources.jobs.esriMcp);
  expect(JSON.stringify(produced)).not.toContain('scoped-test-token');

  const aggregateBytes = `${JSON.stringify(produced.aggregate, null, 2)}\n`;
  const aliases = buildReceiptAliasBytes(produced.aggregate, receiptSchemaFixture());
  expect(aliases.aggregateBytes).toBe(aliases.sdkBytes);
  expect(aliases.aggregateBytes).toBe(aggregateBytes);
  const evidence = buildConsoleEvidence({
    boundary,
    aggregateSha256: createHash('sha256').update(aggregateBytes).digest('hex'),
    observations: produced.observations,
  });
  expect(evidence).toEqual(expect.objectContaining({
    schemaVersion: 'honua.console.ai-arc-evidence/v1',
    aggregateSha256: createHash('sha256').update(aggregateBytes).digest('hex'),
    handoffDigest: fixture.handoff.integrity.digest,
    checkpointDigest: fixture.checkpoint.integrity.digest,
    runtime: {
      consoleCommit: fixture.handoff.components['honua-console'],
      serverSourceRevision: fixture.handoff.components['honua-server'],
    },
  }));
  expect(evidence.publications.map.recovery).toEqual(expect.objectContaining({
    status: 'passed',
    resumedJobId: 'esri-job',
    actionableDiagnostics: true,
  }));
  expect(Object.keys(evidence)).toEqual([
    'schemaVersion', 'status', 'target', 'candidate', 'endpointSha256', 'components', 'handoffDigest',
    'checkpointDigest', 'aggregateSha256', 'runtime', 'publications', 'checks', 'integrity',
  ]);
  expect(evidence.integrity.algorithm).toBe('sha256');
  const unsignedEvidence = { ...evidence } as any;
  delete unsignedEvidence.integrity;
  expect(evidence.integrity.digest).toBe(digest(unsignedEvidence));
});

test('browser witness mode is read-only and refuses a pending UI proposal', async ({ page }) => {
  const fixture = candidateBoundary();
  await installConsoleUi(page, fixture, new Set());
  const boundary = readConsoleBoundary(fixture.checkpoint, fixture.handoff);

  await expect(produceConsoleReceiptInBrowser({
    page,
    consoleOrigin,
    serverEndpoint,
    mode: 'witness',
    boundary,
    receiptSchema: receiptSchemaFixture(),
  })).rejects.toThrow('witness mode cannot approve pending Map proposal');
});

test('direct server producer is retained only as a read-only witness', async () => {
  const fixture = candidateBoundary();
  const boundary = readConsoleBoundary(fixture.checkpoint, fixture.handoff);
  await expect(observeConsoleApiWitness({
    endpoint: serverEndpoint,
    credential: 'scoped-test-token',
    mode: 'full',
    boundary,
    transport: async () => ({ status: 500, body: '{}' }),
  })).rejects.toThrow('direct server API production is witness-only');
});

test('candidate boundary rejects tampering and carries exact component/digest identity', () => {
  const fixture = candidateBoundary();
  const boundary = readConsoleBoundary(fixture.checkpoint, fixture.handoff);
  expect(boundary.components).toEqual(fixture.handoff.components);
  expect(boundary.handoffDigest).toBe(fixture.handoff.integrity.digest);

  fixture.checkpoint.resume.capturedVariables.connectionId = 'tampered';
  expect(() => readConsoleBoundary(fixture.checkpoint, fixture.handoff)).toThrow('checkpoint.integrity.digest');
});

test('CLI fails closed without endpoint, Console origin, credential, and candidate inputs', () => {
  const cli = new URL('../live/console-receipt-cli.mjs', import.meta.url);
  const result = spawnSync(process.execPath, [fileURLToPath(cli)], {
    encoding: 'utf8',
    env: { PATH: process.env.PATH ?? '' },
  });
  expect(result.status).toBe(1);
  expect(result.stderr).toContain('endpoint is required');
  expect(result.stdout).toBe('');
});

test('CLI refuses broad admin-key credentials without logging their value', () => {
  const cli = new URL('../live/console-receipt-cli.mjs', import.meta.url);
  const result = spawnSync(process.execPath, [fileURLToPath(cli)], {
    encoding: 'utf8',
    env: { PATH: process.env.PATH ?? '', HONUA_ADMIN_KEY: 'must-not-be-used', HONUA_AI_ARC_CONSOLE_TOKEN: 'scoped' },
  });
  expect(result.status).toBe(1);
  expect(result.stderr).toContain('broad admin-key variables are not accepted');
  expect(result.stderr).not.toContain('must-not-be-used');
});

async function installConsoleUi(page: import('@playwright/test').Page, fixture: ReturnType<typeof candidateBoundary>, approved: Set<string>) {
  await page.context().route('**/*', async (route) => {
    const url = new URL(route.request().url());
    if (url.origin === 'https://server.example') {
      await route.fulfill({ status: 200, contentType: 'text/html', body: '<!doctype html><title>published</title>' });
      return;
    }
    if (url.origin !== consoleOrigin) {
      await route.abort('blockedbyclient');
      return;
    }
    const body = consoleHtml(url, fixture, approved);
    if (url.pathname === '/version.json') {
      await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(body) });
    } else {
      await route.fulfill({ status: 200, contentType: 'text/html', body: String(body) });
    }
  });
}

function consoleHtml(url: URL, fixture: ReturnType<typeof candidateBoundary>, approved: Set<string>): string | object {
  if (url.pathname === '/version.json') return { commit: fixture.handoff.components['honua-console'], version: '2026.1' };
  if (url.pathname === '/operate/health') return html('<div class="approval-inbox-toolbar"><span class="console-status console-state-success">Healthy</span></div><div id="health-checks">ready</div>');
  if (url.pathname === '/operate/connections/connection-1') return html('<div data-connection-id="connection-1" data-connection-loaded="True">connection-1</div>');
  if (url.pathname === '/operate/services/zero-to-map/settings') return html('<div data-service-name="zero-to-map" data-service-loaded="True"><i data-layer-id="1">1</i><i data-layer-id="2">2</i></div>');
  if (url.pathname === '/operate/layers') return html('<i data-layer-id="1">1</i><i data-layer-id="2">2</i>');
  if (url.pathname.startsWith('/operate/geoprocessing/')) {
    const jobId = decodeURIComponent(url.pathname.split('/').at(-1)!);
    if (jobId.startsWith('console-recovery-missing-')) {
      return html('<a href="/operate/geoprocessing">Back to jobs</a><div class="operate-status-denied">Not found<div data-console-diagnostics><span data-diagnostics-detail-text>HTTP 404; use the stable job link to recover.</span></div></div>');
    }
    const gp = jobId === 'esri-job' || jobId === 'gpserver-job';
    return html(`<h2 id="job-detail-heading">${jobId}</h2>${gp ? '<span data-gpserver-service-id>analysis</span><span data-gpserver-task-name>Buffer</span><span data-canonical-process-id>geometry.buffer</span>' : ''}${jobId === 'esri-job' ? '<span data-result-package-id>esri-job:v1</span><i data-evidence-id="esri-artifact">esri-artifact</i>' : ''}${jobId === 'direct-job' ? '<i data-evidence-id="direct-artifact">direct-artifact</i>' : ''}`);
  }
  if (url.pathname === '/inbox' && !url.searchParams.has('proposalId')) {
    return html(families.map((family) => `<article data-proposal-id="${family}-proposal">${family}</article>`).join(''));
  }
  if (url.pathname === '/inbox') {
    const proposalId = url.searchParams.get('proposalId')!;
    const family = proposalId.split('-')[0];
    const status = approved.has(family) ? 'Submitted' : 'AwaitingApproval';
    return html(`<article data-proposal-id="${proposalId}">
      <span data-proposal-status="${status}">${status}</span>
      <div data-proposal-diff>${family}-item ${family}-publication-version ${family}-reopened-draft ${family}-route</div>
      ${approved.has(family) ? operationChip(family) : `<button data-proposal-approve>Approve</button>`}
      <p data-proposal-action-message hidden></p>
      <script>document.querySelector('[data-proposal-approve]')?.addEventListener('click',()=>{
        const root=document.querySelector('[data-proposal-id]');
        root.querySelector('[data-proposal-status]').setAttribute('data-proposal-status','Submitted');
        root.querySelector('[data-proposal-status]').textContent='Submitted';
        root.querySelector('[data-proposal-approve]').remove();
        root.insertAdjacentHTML('beforeend',${JSON.stringify(operationChip(family))});
        const message=root.querySelector('[data-proposal-action-message]'); message.hidden=false; message.textContent='Approved';
        fetch('/mock-approved/${family}',{method:'POST'});
      });</script>
    </article>`);
  }
  if (url.pathname.startsWith('/mock-approved/')) {
    approved.add(url.pathname.split('/').at(-1)!);
    return html('ok');
  }
  if (url.pathname === '/operate/release-witness') {
    const family = url.searchParams.get('family')!;
    return html(`<article data-release-witness
      data-server-source-revision="${fixture.handoff.components['honua-server']}"
      data-family="${family}" data-item-id="${family}-item"
      data-version-id="${family}-publication-version" data-content-hash="${family}-publication-hash"
      data-proposal-id="${family}-proposal" data-publication-id="${family}-publication"
      data-public-url="https://server.example/share/${family}-route"
      data-audit-correlation-id="${family}-correlation" data-audit-operation-id="${family}-operation"
      data-audit-verified="true">${family} release witness</article>`);
  }
  if (url.pathname === '/operate/publishing') {
    return html(`<article data-publishing-review data-publication-id="${url.searchParams.get('publicationId')}">publication review</article>`);
  }
  throw new Error(`unexpected Console UI route ${url}`);
}

function operationChip(family: string) {
  return `<a data-correlation-chip data-correlation-kind="OperationId" data-correlation-id="${family}-operation">${family}-operation</a>`;
}

function html(body: string) {
  return `<!doctype html><html><body>${body}</body></html>`;
}

function candidateBoundary() {
  const captures: Record<string, unknown> = {
    candidateId: `manifest-sha256:${'a'.repeat(64)}`,
    releaseId: '2026.1-rc.2',
    connectionId: 'connection-1', serviceName: 'zero-to-map', parcelsLayerId: 1, zoningLayerId: 2,
    esriMcpJobId: 'esri-job', gpServerJobId: 'gpserver-job', directAnalysisJobId: 'direct-job',
    esriMcpServiceId: 'analysis', esriMcpTaskName: 'Buffer', esriMcpProcessId: 'geometry.buffer',
    esriMcpResultPackageId: 'esri-job:v1', esriMcpArtifactId: 'esri-artifact', bufferArtifactId: 'direct-artifact',
  };
  families.forEach((family, index) => Object.assign(captures, {
    [`${family}DraftId`]: `${family}-original-draft`,
    [`${family}ItemId`]: `${family}-item`,
    [`${family}VersionId`]: `${family}-version`,
    [`${family}ContentHash`]: `${family}-content-hash`,
    [`${family}ReopenedDraftId`]: `${family}-reopened-draft`,
    [`${family}ProposalGeneration`]: index + 2,
    [`${family}PublicationVersionId`]: `${family}-publication-version`,
    [`${family}PublicationContentHash`]: `${family}-publication-hash`,
    [`${family}Route`]: `${family}-route`,
  }));
  const checkpoint: any = {
    schemaVersion: 'honua.zero-to-map.checkpoint/v1', state: 'paused', target: 'local-docker',
    journeyId: '2026.1-zero-to-map', releaseContract: 'honua-release#123/D9.3', sourceRevision: '2'.repeat(40),
    candidateId: captures.candidateId, releaseId: captures.releaseId,
    resume: { resumeAt: { stageId: 'console', actionId: 'console-approval' }, capturedVariables: captures },
    consoleReceiptRequest: {
      schemaVersion: 'honua.zero-to-map.console-receipt-request/v1', actionId: 'console-approval',
      receiptSchema: 'honua.zero-to-map.console-receipt/v1',
      matches: { '/journeyId': '2026.1-zero-to-map', '/releaseContract': 'honua-release#123/D9.3', '/status': 'passed', '/candidate/candidateId': captures.candidateId, '/candidate/releaseId': captures.releaseId },
      requiredPointers: families.flatMap((family) => [`/proposals/${family}/proposalId`, `/proposals/${family}/executionOperationId`, `/publications/${family}/publicationId`, `/publications/${family}/publicUrl`, `/audit/${family}/correlationId`]).concat(['/shareUrl']),
      equalPointers: families.flatMap((family) => [[`/proposals/${family}/proposalId`, `/publications/${family}/requestId`], [`/proposals/${family}/executionOperationId`, `/audit/${family}/operationId`]]).concat([[`/resources/gp/jobId`, `/resources/jobs/esriMcp`], ['/shareUrl', '/publications/app/publicUrl']]),
    },
  };
  checkpoint.integrity = { algorithm: 'sha256', digest: digest(checkpoint) };
  const components = Object.fromEntries(
    ['honua-server', 'honua-sdk-js', 'honua-console', 'honua-studio', 'honua-devops', 'honua-iac']
      .map((name, index) => [name, name === 'honua-sdk-js' ? checkpoint.sourceRevision : String(index + 1).repeat(40)]),
  );
  const handoff: any = {
    schemaVersion: 'honua.studio.real-model-ai-arc-handoff/v1', status: 'paused', target: checkpoint.target,
    candidateId: checkpoint.candidateId, releaseId: checkpoint.releaseId,
    endpointSha256: createHash('sha256').update(serverEndpoint).digest('hex'),
    source: { repository: 'honua-io/honua-studio', sha: components['honua-studio'] }, components,
    model: { provider: 'bedrock', modelId: 'claude-sonnet' }, promptVersion: '2026.1-v1', evalVersion: '2026.1-v1',
    transcriptSha256: 'd'.repeat(64),
    deterministic: { target: checkpoint.target, checkpointDigest: checkpoint.integrity.digest },
    lanes: Object.fromEntries(['admin', 'esriGp', 'nativeAnalysis', 'studioPublication'].map((name) => [name, { promptSha256: 'b'.repeat(64), transcriptSha256: 'c'.repeat(64), calls: [{ status: 'passed' }] }])),
    joins: { ...captures }, consoleReceiptRequest: checkpoint.consoleReceiptRequest,
  };
  handoff.integrity = { algorithm: 'sha256', digest: digest(handoff) };
  return { checkpoint, handoff };
}

function digest(value: any) {
  return createHash('sha256').update(canonical(value)).digest('hex');
}

function canonical(value: any): string {
  if (Array.isArray(value)) return `[${value.map(canonical).join(',')}]`;
  if (value && typeof value === 'object') return `{${Object.keys(value).sort().map((key) => `${JSON.stringify(key)}:${canonical(value[key])}`).join(',')}}`;
  return JSON.stringify(value);
}

function receiptSchemaFixture() {
  const text = { type: 'string', minLength: 1 };
  const strict = (properties: Record<string, unknown>, required = Object.keys(properties)) => ({
    type: 'object', required, properties, additionalProperties: false,
  });
  const familiesOf = (child: unknown) => strict({ map: child, app: child, dashboard: child });
  const proposal = strict({
    draftId: text, generation: { type: 'integer', minimum: 1 }, route: text, proposalId: text,
    executionOperationId: text,
  });
  const publication = strict({
    requestId: text, itemId: text, versionId: text, status: { const: 'published' }, publicationId: text,
    publicUrl: { type: 'string', pattern: '^https://' },
  });
  const audit = strict({ correlationId: text, operationId: text });
  const studio = strict({ draftId: text, itemId: text, versionId: text, contentHash: text, reopenedDraftId: text });
  return strict({
    schemaVersion: { const: 'honua.zero-to-map.console-receipt/v1' },
    journeyId: { const: '2026.1-zero-to-map' },
    releaseContract: { const: 'honua-release#123/D9.3' },
    status: { const: 'passed' },
    proposals: familiesOf(proposal),
    publications: familiesOf(publication),
    audit: familiesOf(audit),
    resources: strict({
      connectionId: text,
      serviceId: text,
      layerIds: strict({ parcels: { type: 'integer', minimum: 0 }, zoning: { type: 'integer', minimum: 0 } }),
      jobs: strict({ esriMcp: text, gpServer: text, directAnalysis: text }),
      gp: strict({ jobId: text, serviceId: text, taskName: text, processId: text, resultPackageId: text, artifactId: text }),
      artifactId: text,
      studio: familiesOf(studio),
    }),
    candidate: strict({ candidateId: text, releaseId: text }),
    checks: strict({ health: { const: 'passed' }, audit: { const: 'passed' }, recovery: { const: 'passed' } }),
    shareUrl: { type: 'string', pattern: '^https://' },
  });
}
