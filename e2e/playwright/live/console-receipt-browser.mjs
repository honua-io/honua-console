import { createHash, randomUUID } from 'node:crypto';
import { validatePinnedJsonSchema } from './json-schema-validator.mjs';

const FAMILIES = ['map', 'app', 'dashboard'];
const AGGREGATE_SCHEMA = 'honua.zero-to-map.console-receipt/v1';
const EVIDENCE_SCHEMA = 'honua.console.ai-arc-evidence/v1';

/**
 * Canonical producer: every candidate observation and mutation is driven through
 * the published Console in a real browser. The direct API producer remains a
 * separate read-only witness and is not called from this path.
 */
export async function produceConsoleReceiptInBrowser({
  page,
  consoleOrigin,
  serverEndpoint,
  mode = 'full',
  boundary,
  receiptSchema,
}) {
  if (!['full', 'witness'].includes(mode)) throw new Error('mode must be full or witness');
  const consoleBase = validateOrigin(consoleOrigin, boundary.target, 'Console origin');
  const serverBase = validateEndpoint(serverEndpoint, boundary.target);
  const components = boundary.components;
  if (!components || !boundary.handoffDigest || !boundary.endpointSha256) {
    throw new Error('canonical browser production requires the sealed Studio handoff component boundary');
  }
  equal(sha256(serverBase.replace(/\/$/, '')), boundary.endpointSha256, 'Studio handoff endpointSha256');

  const response = await page.goto(new URL('/operate/health', consoleBase).toString(), { waitUntil: 'domcontentloaded' });
  if (!response?.ok()) throw new Error(`Console health UI returned HTTP ${response?.status() ?? 'unknown'}`);
  await visible(page.locator('#health-checks'), 'Console health checks');
  await visible(page.locator('.approval-inbox-toolbar .console-status.console-state-success'), 'healthy deployment status');

  const version = await page.evaluate(async () => {
    const response = await fetch('/version.json', { headers: { accept: 'application/json' } });
    return { status: response.status, value: response.ok ? await response.json() : null };
  });
  if (version.status !== 200 || !version.value) throw new Error(`Console /version.json returned HTTP ${version.status}`);
  const consoleCommit = revision(version.value.commit, 'Console /version.json.commit');
  equal(consoleCommit, components['honua-console'], 'running Console source revision');

  await inspectResources(page, consoleBase, boundary.resources);

  const proposals = {};
  const publications = {};
  const audit = {};
  let serverSourceRevision;
  for (const family of FAMILIES) {
    const facts = boundary.families[family];
    const proposalId = await selectProposalInUi(page, consoleBase, facts);
    const proposal = await approveProposalInUi(page, consoleBase, proposalId, facts, mode);
    const witness = await inspectReleaseWitnessUi(page, consoleBase, family, facts, proposalId);
    equal(witness.auditOperationId, proposal.executionOperationId,
      `${family} audit executionOperationId observed independently`);
    serverSourceRevision ??= witness.serverSourceRevision;
    equal(witness.serverSourceRevision, serverSourceRevision, `${family} server source revision`);
    equal(witness.serverSourceRevision, components['honua-server'], 'running server source revision');

    await page.goto(
      new URL(`/operate/publishing?publicationId=${encodeURIComponent(witness.publicationId)}`, consoleBase).toString(),
      { waitUntil: 'domcontentloaded' },
    );
    await visible(
      page.locator(`[data-publishing-review][data-publication-id="${attributeValue(witness.publicationId)}"]`),
      `${family} publication review`,
    );
    await assertPublicUrl(page, witness.publicUrl, serverBase, boundary.target);

    proposals[family] = {
      draftId: facts.reopenedDraftId,
      generation: facts.generation,
      route: facts.route,
      proposalId,
      executionOperationId: proposal.executionOperationId,
    };
    publications[family] = {
      requestId: proposalId,
      itemId: facts.itemId,
      versionId: facts.versionId,
      status: 'published',
      publicationId: witness.publicationId,
      publicUrl: witness.publicUrl,
    };
    audit[family] = {
      correlationId: witness.auditCorrelationId,
      operationId: witness.auditOperationId,
    };
  }

  const recovery = await assertActionableRecovery(page, consoleBase, boundary.resources.jobs.esriMcp);
  const aggregate = {
    schemaVersion: AGGREGATE_SCHEMA,
    journeyId: boundary.journeyId,
    releaseContract: boundary.releaseContract,
    status: 'passed',
    proposals,
    publications,
    audit,
    resources: boundary.resources,
    candidate: boundary.candidate,
    checks: { health: 'passed', audit: 'passed', recovery: 'passed' },
    shareUrl: publications.app.publicUrl,
  };
  if (receiptSchema?.properties?.schemaVersion?.const !== AGGREGATE_SCHEMA) {
    throw new Error('pinned SDK Console receipt schema has an unexpected schemaVersion contract');
  }
  validatePinnedJsonSchema(aggregate, receiptSchema);
  validateRequestedReceipt(boundary.receiptRequest, aggregate);
  rejectSecretSerialization(aggregate, 'Console aggregate');

  const observations = {
    consoleCommit,
    serverSourceRevision,
    proposals,
    publications,
    audit,
    recovery,
  };
  return { aggregate, observations };
}

export function buildConsoleEvidence({ boundary, aggregateSha256, observations }) {
  const recovery = {
    status: 'passed',
    deliberateFailureJobId: observations.recovery.deliberateFailureJobId,
    resumedJobId: observations.recovery.resumedJobId,
    actionableDiagnostics: true,
  };
  const evidence = {
    schemaVersion: EVIDENCE_SCHEMA,
    status: 'passed',
    target: boundary.target,
    candidate: boundary.candidate,
    endpointSha256: boundary.endpointSha256,
    components: boundary.components,
    handoffDigest: boundary.handoffDigest,
    checkpointDigest: boundary.checkpointDigest,
    aggregateSha256,
    runtime: {
      consoleCommit: observations.consoleCommit,
      serverSourceRevision: observations.serverSourceRevision,
    },
    publications: Object.fromEntries(FAMILIES.map((family) => [family, {
      proposalId: observations.proposals[family].proposalId,
      executionOperationId: observations.proposals[family].executionOperationId,
      publicationId: observations.publications[family].publicationId,
      publicUrl: observations.publications[family].publicUrl,
      auditCorrelationId: observations.audit[family].correlationId,
      recovery,
    }])),
    checks: {
      browser: 'passed',
      approval: 'passed',
      publication: 'passed',
      audit: 'passed',
      recovery: 'passed',
      ...(observations.keyRecipe ? { adminReadApproveKeyRecipe: 'passed' } : {}),
    },
  };
  evidence.integrity = {
    algorithm: 'sha256',
    digest: sha256(canonicalJson(evidence)),
  };
  rejectSecretSerialization(evidence, 'Console evidence sidecar');
  return evidence;
}

async function inspectResources(page, consoleBase, resources) {
  await page.goto(new URL(`/operate/connections/${encodeURIComponent(resources.connectionId)}`, consoleBase).toString());
  const connection = page.locator(`[data-connection-id="${attributeValue(resources.connectionId)}"]`);
  await attribute(connection, 'data-connection-loaded', 'True', 'connection identity');

  await page.goto(new URL(`/operate/services/${encodeURIComponent(resources.serviceId)}/settings`, consoleBase).toString());
  const service = page.locator(`[data-service-name="${attributeValue(resources.serviceId)}"]`);
  await attribute(service, 'data-service-loaded', 'True', 'service identity');
  for (const layerId of Object.values(resources.layerIds)) {
    await visible(service.locator(`[data-layer-id="${layerId}"]`), `service layer ${layerId}`);
  }

  await page.goto(new URL('/operate/layers', consoleBase).toString());
  for (const layerId of Object.values(resources.layerIds)) {
    await visible(page.locator(`[data-layer-id="${layerId}"]`), `layer inventory ${layerId}`);
  }

  await openJob(page, consoleBase, resources.jobs.esriMcp);
  await textEqual(page.locator('[data-gpserver-service-id]'), resources.gp.serviceId, 'Esri GPServer service alias');
  await textEqual(page.locator('[data-gpserver-task-name]'), resources.gp.taskName, 'Esri GPServer task');
  await textEqual(page.locator('[data-canonical-process-id]'), resources.gp.processId, 'canonical process');
  await textEqual(page.locator('[data-result-package-id]'), resources.gp.resultPackageId, 'result package');
  await visible(page.locator(`[data-evidence-id="${attributeValue(resources.gp.artifactId)}"]`), 'Esri GP artifact');

  await openJob(page, consoleBase, resources.jobs.gpServer);
  await textEqual(page.locator('[data-gpserver-task-name]'), resources.gp.taskName, 'GPServer adapter task');
  await textEqual(page.locator('[data-canonical-process-id]'), resources.gp.processId, 'GPServer canonical process');

  await openJob(page, consoleBase, resources.jobs.directAnalysis);
  await visible(page.locator(`[data-evidence-id="${attributeValue(resources.artifactId)}"]`), 'direct analysis artifact');
}

async function selectProposalInUi(page, consoleBase, facts) {
  if (facts.proposalId) {
    await assertProposalBindingUi(page, consoleBase, facts.proposalId, facts);
    return facts.proposalId;
  }
  await page.goto(new URL('/inbox', consoleBase).toString(), { waitUntil: 'domcontentloaded' });
  const ids = [...new Set((await page.locator('[data-proposal-id]').evaluateAll((nodes) =>
    nodes.map((node) => node.getAttribute('data-proposal-id')).filter(Boolean))).map(String))];
  const candidates = [];
  for (const id of ids) {
    if (await proposalBindsInUi(page, consoleBase, id, facts)) candidates.push(id);
  }
  if (candidates.length !== 1) {
    throw new Error(`expected exactly one ${facts.familyTitle} proposal bound in Console UI; found ${candidates.length}`);
  }
  return candidates[0];
}

async function proposalBindsInUi(page, consoleBase, proposalId, facts) {
  await page.goto(new URL(`/inbox?proposalId=${encodeURIComponent(proposalId)}`, consoleBase).toString(),
    { waitUntil: 'domcontentloaded' });
  const proposal = proposalDetail(page, proposalId);
  if (await proposal.count() === 0) return false;
  const rendered = await proposal.textContent() ?? '';
  return [facts.itemId, facts.versionId, facts.reopenedDraftId, facts.route]
    .every((identity) => rendered.includes(String(identity)));
}

async function assertProposalBindingUi(page, consoleBase, proposalId, facts) {
  if (!await proposalBindsInUi(page, consoleBase, proposalId, facts)) {
    throw new Error(`${facts.familyTitle} proposal ${proposalId} is not bound to exact checkpoint identities in Console UI`);
  }
}

async function approveProposalInUi(page, consoleBase, proposalId, facts, mode) {
  await assertProposalBindingUi(page, consoleBase, proposalId, facts);
  const proposal = proposalDetail(page, proposalId);
  let status = await requiredAttribute(proposal.locator('[data-proposal-status]'), 'data-proposal-status', 'proposal status');
  const approve = proposal.locator('[data-proposal-approve]');
  if (status === 'AwaitingApproval') {
    if (mode === 'witness') throw new Error(`witness mode cannot approve pending ${facts.familyTitle} proposal ${proposalId}`);
    if (await approve.count() !== 1) throw new Error(`Console UI does not expose one approve action for ${proposalId}`);
    await approve.click();
    await proposal.locator('[data-proposal-action-message]').waitFor({ state: 'visible' });
    const message = await proposal.locator('[data-proposal-action-message]').textContent() ?? '';
    if (!message.includes('Approved')) throw new Error(`Console approval failed for ${proposalId}: ${message.trim()}`);
    status = await requiredAttribute(proposal.locator('[data-proposal-status]'), 'data-proposal-status', 'resolved proposal status');
  }
  if (!['Submitted', 'Succeeded'].includes(status)) throw new Error(`proposal ${proposalId} resolved as ${status}`);
  const operationChip = proposal.locator('[data-correlation-chip][data-correlation-kind="OperationId"]');
  const executionOperationId = await requiredAttribute(operationChip, 'data-correlation-id', 'proposal executionOperationId');
  return { executionOperationId };
}

async function inspectReleaseWitnessUi(page, consoleBase, family, facts, proposalId) {
  const query = new URLSearchParams({
    family,
    itemId: facts.itemId,
    versionId: facts.versionId,
    contentHash: facts.contentHash,
    proposalId,
  });
  await page.goto(new URL(`/operate/release-witness?${query}`, consoleBase).toString(), { waitUntil: 'domcontentloaded' });
  const root = page.locator('[data-release-witness]');
  await visible(root, `${family} release witness UI`);
  equal(await requiredAttribute(root, 'data-family', 'release family'), family, 'release family');
  equal(await requiredAttribute(root, 'data-item-id', 'release item'), facts.itemId, `${family} release item`);
  equal(await requiredAttribute(root, 'data-version-id', 'release version'), facts.versionId, `${family} release version`);
  equal(await requiredAttribute(root, 'data-content-hash', 'release content hash'), facts.contentHash, `${family} content hash`);
  equal(await requiredAttribute(root, 'data-proposal-id', 'release proposal'), proposalId, `${family} release proposal`);
  equal(await requiredAttribute(root, 'data-audit-verified', 'audit integrity'), 'true', `${family} audit integrity`);
  return {
    serverSourceRevision: revision(await requiredAttribute(root, 'data-server-source-revision', 'server source revision'),
      'server source revision'),
    publicationId: await requiredAttribute(root, 'data-publication-id', 'publicationId'),
    publicUrl: httpsUrl(await requiredAttribute(root, 'data-public-url', 'publicUrl'), 'publicUrl'),
    auditCorrelationId: await requiredAttribute(root, 'data-audit-correlation-id', 'audit correlationId'),
    auditOperationId: await requiredAttribute(root, 'data-audit-operation-id', 'audit executionOperationId'),
  };
}

async function assertActionableRecovery(page, consoleBase, resumedJobId) {
  const deliberateFailureJobId = `console-recovery-missing-${randomUUID()}`;
  await page.goto(new URL(`/operate/geoprocessing/${encodeURIComponent(deliberateFailureJobId)}`, consoleBase).toString(),
    { waitUntil: 'domcontentloaded' });
  const denied = page.locator('.operate-status-denied');
  await visible(denied, 'deliberate missing-job failure');
  const message = (await denied.textContent() ?? '').trim();
  if (!/not found|missing/i.test(message)) throw new Error('missing-job recovery did not expose actionable not-found diagnostics');
  await visible(denied.locator('[data-console-diagnostics]'), 'technical recovery diagnostics');
  await visible(page.locator('a[href="/operate/geoprocessing"]'), 'recovery path back to jobs');
  await openJob(page, consoleBase, resumedJobId);
  return { deliberateFailureJobId, resumedJobId };
}

async function openJob(page, consoleBase, jobId) {
  await page.goto(new URL(`/operate/geoprocessing/${encodeURIComponent(jobId)}`, consoleBase).toString(),
    { waitUntil: 'domcontentloaded' });
  await textEqual(page.locator('#job-detail-heading'), jobId, `job ${jobId}`);
}

async function assertPublicUrl(page, publicUrl, serverBase, target) {
  const url = new URL(publicUrl);
  if (target === 'aws-ecs' && url.protocol !== 'https:') throw new Error(`AWS public URL is not HTTPS: ${publicUrl}`);
  equal(url.origin, new URL(serverBase).origin, 'publication endpoint origin');
  const publicPage = await page.context().newPage();
  try {
    const response = await publicPage.goto(publicUrl, { waitUntil: 'domcontentloaded' });
    if (!response?.ok()) throw new Error(`public publication ${publicUrl} returned HTTP ${response?.status() ?? 'unknown'}`);
  } finally {
    await publicPage.close();
  }
}

function proposalDetail(page, proposalId) {
  return page.locator(`[data-proposal-id="${attributeValue(proposalId)}"]:has([data-proposal-status])`).last();
}

function validateRequestedReceipt(request, receipt) {
  equal(receipt.schemaVersion, AGGREGATE_SCHEMA, 'aggregate receipt schemaVersion');
  for (const [pointer, expected] of Object.entries(request.matches)) {
    equal(jsonPointer(receipt, pointer), expected, `Console receipt request ${pointer}`);
  }
  for (const pointer of request.requiredPointers) {
    const value = jsonPointer(receipt, pointer);
    if (value === undefined || value === null || value === '') throw new Error(`Console receipt request is missing ${pointer}`);
  }
  for (const [left, right] of request.equalPointers) {
    equal(jsonPointer(receipt, left), jsonPointer(receipt, right), `Console receipt equality ${left} = ${right}`);
  }
}

function jsonPointer(value, pointer) {
  return pointer.slice(1).split('/').reduce((current, segment) => current?.[segment.replaceAll('~1', '/').replaceAll('~0', '~')], value);
}

async function visible(locator, label) {
  try { await locator.waitFor({ state: 'visible' }); }
  catch { throw new Error(`${label} was not visible in the Console UI`); }
}

async function textEqual(locator, expected, label) {
  await visible(locator, label);
  equal((await locator.textContent() ?? '').trim(), String(expected), label);
}

async function attribute(locator, name, expected, label) {
  await visible(locator, label);
  equal(await locator.getAttribute(name), expected, label);
}

async function requiredAttribute(locator, name, label) {
  await visible(locator, label);
  const value = await locator.getAttribute(name);
  if (typeof value !== 'string' || !value.trim()) throw new Error(`${label} is missing ${name}`);
  return value.trim();
}

function revision(value, label) {
  if (typeof value !== 'string' || !/^[0-9a-f]{40}$/.test(value)) throw new Error(`${label} must be a full lowercase Git SHA`);
  return value;
}

function httpsUrl(value, label) {
  const url = new URL(value);
  if (url.protocol !== 'https:') throw new Error(`${label} must use HTTPS`);
  return url.toString();
}

function validateOrigin(value, target, label) {
  const url = new URL(required(value, label));
  if (url.username || url.password || url.search || url.hash || url.pathname !== '/') {
    throw new Error(`${label} must be a credential-free origin`);
  }
  if (target === 'aws-ecs' && url.protocol !== 'https:') throw new Error(`${label} must use HTTPS for AWS`);
  if (!['http:', 'https:'].includes(url.protocol)) throw new Error(`${label} must use HTTP or HTTPS`);
  return url.toString();
}

function validateEndpoint(value, target) {
  const url = new URL(required(value, 'server endpoint'));
  if (url.username || url.password || url.search || url.hash) throw new Error('server endpoint must not contain credentials, query, or fragment');
  if (target === 'aws-ecs' && url.protocol !== 'https:') throw new Error('AWS server endpoint must use HTTPS');
  return url.toString();
}

function required(value, label) {
  if (typeof value !== 'string' || !value.trim()) throw new Error(`${label} is required`);
  return value.trim();
}

function equal(actual, expected, label) {
  if (canonicalJson(actual) !== canonicalJson(expected)) {
    throw new Error(`${label} mismatch: expected ${JSON.stringify(expected)}, received ${JSON.stringify(actual)}`);
  }
}

function rejectSecretSerialization(value, label, path = label) {
  if (!value || typeof value !== 'object') return;
  for (const [key, child] of Object.entries(value)) {
    if (/(password|authorization|api[_-]?key|admin[_-]?key|secretstring|access[_-]?token)/i.test(key)) {
      throw new Error(`${label} contains forbidden secret-shaped field at ${path}.${key}`);
    }
    rejectSecretSerialization(child, label, `${path}.${key}`);
  }
}

function canonicalJson(value) {
  if (Array.isArray(value)) return `[${value.map(canonicalJson).join(',')}]`;
  if (value && typeof value === 'object') {
    return `{${Object.keys(value).sort().map((key) => `${JSON.stringify(key)}:${canonicalJson(value[key])}`).join(',')}}`;
  }
  return JSON.stringify(value);
}

function sha256(value) {
  return createHash('sha256').update(value).digest('hex');
}

function attributeValue(value) {
  return String(value).replaceAll('\\', '\\\\').replaceAll('"', '\\"');
}
