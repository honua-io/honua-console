import type { IncomingMessage, ServerResponse } from 'node:http';
import { fileURLToPath } from 'node:url';
import { startStaticBundleServer } from '../static-bundle-server.mjs';
import { respondToSharedAdminApi } from './shared-admin-fixture';

// Long unbroken identifiers, as an installed server reports them for a finished buffer job.
export const jobId = '0e6f1c2a9b8d4e7fa1c3b5d7e9f20486a1b3c5d7e9f2048';
const base = `/api/v1/admin/jobs/${jobId}`;
const artifactId = `artifact-${jobId}-result-geojson-0001`;
const reference = `/api/v1/admin/jobs/${jobId}/artifacts/${artifactId}/content`;
const at = '2026-11-17T06:30:00Z';

const links = { self: base, logs: `${base}/logs`, artifacts: `${base}/artifacts`, actions: `${base}/actions`,
  steps: `${base}/steps`, diagnostics: `${base}/diagnostics` };
const artifact = { artifactId, availability: 'Available', kind: 'result', label: artifactId,
  contentType: 'application/geo+json', sizeBytes: 512, providerLink: reference };
const artifacts = { state: 'complete', items: [artifact], availableCount: 1, expiredCount: 0, unavailableCount: 0 };
const evidence = { missing: [], truncated: [], notes: [] };

const detail = {
  jobId, kind: 'Geoprocessing', backend: 'postgis', targetKind: 'process', workloadName: 'geometry.buffer',
  definitionId: 'gpserver-geoprocessing-geometry-buffer', status: 'Succeeded', priority: 'Normal',
  requestedBy: 'fixture-operator', createdAt: at, updatedAt: at, completedAt: at, durationMs: 1200,
  percentComplete: 1, currentPhase: 'succeeded', attemptCount: 1, maxAttempts: 1,
  correlationId: `corr-${jobId}`, traceId: `trace-${jobId}`, resourceRefs: [`process:${artifactId}`],
  artifactCount: 1, links, providerOperationId: `operation-${jobId}`, warnings: [], childJobIds: [],
  selectedMetadata: { resultPackageId: `result-${jobId}` },
  stages: [{ name: 'Execute', status: 'Succeeded', startedAt: at, completedAt: at, percentComplete: 1 }],
  actions: [],
};

const diagnostics = {
  schema: 'honua.job-diagnostics/v1', jobId,
  lifecycle: { phase: 'succeeded', timingSource: 'inferred' },
  attempt: { number: 1, maxAttempts: 1, retry: { allowed: false, disabledReason: 'Only failed or cancelled jobs can be retried.', requiresApproval: false, wouldRepeat: '' } },
  steps: [],
  events: { state: 'available', items: [], totalCount: 0, limit: 100, firstCursor: 'start', mode: 'tail', truncated: false },
  artifacts, evidence: { ...evidence, notes: ['All 1 artifact(s) are accounted for; 1 redacted for your access.'] }, links,
};

const responses: Record<string, unknown> = {
  [base]: detail,
  [`${base}/logs`]: { jobId, state: 'complete', items: [] },
  [`${base}/artifacts`]: { jobId, items: [artifact] },
  [`${base}/steps`]: { jobId, state: 'complete', steps: [], artifacts, evidence, links },
  [`${base}/diagnostics`]: diagnostics,
};

export async function jobDetailFixture(options: { v4Enabled: boolean }) {
  const diagnosticCursors: (string | null)[] = [];
  const handleRequest = async (request: IncomingMessage, response: ServerResponse) => {
    response.setHeader('Content-Type', 'application/json');
    const url = new URL(request.url!, 'http://localhost');
    if (respondToSharedAdminApi(request, response)) return;
    if (url.pathname === `${base}/diagnostics`) diagnosticCursors.push(url.searchParams.get('eventCursor'));
    const body = request.method === 'GET' ? responses[url.pathname] : undefined;
    if (body === undefined) response.writeHead(404).end('{}');
    else response.end(JSON.stringify(body));
  };
  const server = await startStaticBundleServer(
    fileURLToPath(new URL('../../../artifacts/static-publish/wwwroot/console', import.meta.url)), 0, {
      configuration: { apiBaseUrl: '/', auth: { mode: 'builtin' }, v4Enabled: options.v4Enabled },
      handleApi: handleRequest,
    });
  return { server, diagnosticCursors };
}
