import { createHash } from 'node:crypto';

const CHECKPOINT_SCHEMA = 'honua.zero-to-map.checkpoint/v1';
const STUDIO_HANDOFF_SCHEMA = 'honua.studio.real-model-ai-arc-handoff/v1';
const CONSOLE_RECEIPT_SCHEMA = 'honua.zero-to-map.console-receipt/v1';
const REQUEST_SCHEMA = 'honua.zero-to-map.console-receipt-request/v1';
const FAMILIES = ['map', 'app', 'dashboard'];

export function readConsoleBoundary(checkpointValue, studioHandoffValue) {
  const checkpoint = record(checkpointValue, 'checkpoint');
  const studioHandoff = record(studioHandoffValue, 'Studio handoff');
  rejectSecretSerialization(checkpoint, 'checkpoint');
  rejectSecretSerialization(studioHandoff, 'Studio handoff');
  equal(checkpoint.schemaVersion, CHECKPOINT_SCHEMA, 'checkpoint.schemaVersion');
  equal(checkpoint.state, 'paused', 'checkpoint.state');
  if (!['local-docker', 'aws-ecs'].includes(checkpoint.target)) {
    throw new Error('checkpoint.target must be local-docker or aws-ecs');
  }

  const resume = record(checkpoint.resume, 'checkpoint.resume');
  const resumeAt = record(resume.resumeAt, 'checkpoint.resume.resumeAt');
  equal(resumeAt.stageId, 'console', 'checkpoint.resume.resumeAt.stageId');
  equal(resumeAt.actionId, 'console-approval', 'checkpoint.resume.resumeAt.actionId');
  const request = record(checkpoint.consoleReceiptRequest, 'checkpoint.consoleReceiptRequest');
  exactKeySet(request, ['schemaVersion', 'actionId', 'receiptSchema', 'matches', 'requiredPointers', 'equalPointers'],
    'checkpoint.consoleReceiptRequest');
  equal(request.schemaVersion, REQUEST_SCHEMA, 'checkpoint.consoleReceiptRequest.schemaVersion');
  equal(request.actionId, 'console-approval', 'checkpoint.consoleReceiptRequest.actionId');
  equal(request.receiptSchema, CONSOLE_RECEIPT_SCHEMA, 'checkpoint.consoleReceiptRequest.receiptSchema');
  verifyCheckpointIntegrity(checkpoint);

  const checkpointCaptures = record(resume.capturedVariables, 'checkpoint.resume.capturedVariables');
  if (studioHandoff.schemaVersion !== STUDIO_HANDOFF_SCHEMA) {
    throw new Error('Studio handoff input must be the sealed immutable paused handoff');
  }
  const endpointSha256 = verifyStudioHandoff(studioHandoff, checkpoint, checkpointCaptures);
  const handoffDigest = studioHandoff.integrity.digest;
  const components = { ...studioHandoff.components };
  const captures = { ...checkpointCaptures };
  const matches = record(request.matches, 'checkpoint.consoleReceiptRequest.matches');
  const capture = (name) => requiredCapture(captures, name);
  const familyFacts = Object.fromEntries(FAMILIES.map((family) => {
    const title = family[0].toUpperCase() + family.slice(1);
    const route = captureOrMatch(captures, `${family}Route`, matches, `/proposals/${family}/route`,
      family === 'app' ? captureOrMatch(captures, 'route', matches, '/proposal/route') : undefined);
    return [family, {
      draftId: stringValue(capture(`${family}DraftId`), `${family}DraftId`),
      itemId: stringValue(capture(`${family}ItemId`), `${family}ItemId`),
      versionId: stringValue(captures[`${family}PublicationVersionId`] ?? capture(`${family}VersionId`), `${family} publication version`),
      contentHash: stringValue(captures[`${family}PublicationContentHash`] ?? capture(`${family}ContentHash`), `${family} publication content hash`),
      reopenedDraftId: stringValue(capture(`${family}ReopenedDraftId`), `${family}ReopenedDraftId`),
      generation: positiveIntegerValue(captures[`${family}ProposalGeneration`] ?? captures[`${family}ReopenedGeneration`] ?? (family === 'app' ? captures.proposalGeneration : undefined), `${family} proposal generation`),
      route: stringValue(route, `${family} publication route`),
      proposalId: optionalString(captures[`${family}ProposalId`] ?? matches[`/proposals/${family}/proposalId`]),
      familyTitle: title,
    }];
  }));

  const boundary = {
    target: checkpoint.target,
    ...(endpointSha256 ? { endpointSha256 } : {}),
    ...(handoffDigest ? { handoffDigest } : {}),
    ...(components ? { components } : {}),
    checkpointDigest: stringValue(record(checkpoint.integrity, 'checkpoint.integrity').digest, 'checkpoint.integrity.digest'),
    journeyId: stringValue(checkpoint.journeyId, 'checkpoint.journeyId'),
    releaseContract: stringValue(checkpoint.releaseContract, 'checkpoint.releaseContract'),
    candidate: {
      candidateId: stringValue(checkpoint.candidateId ?? captures.candidateId, 'checkpoint.candidateId'),
      releaseId: stringValue(checkpoint.releaseId ?? captures.releaseId, 'checkpoint.releaseId'),
    },
    receiptRequest: {
      matches,
      requiredPointers: stringArrayOrEmpty(request.requiredPointers),
      equalPointers: pointerPairsOrEmpty(request.equalPointers),
    },
    families: familyFacts,
    resources: {
      connectionId: stringValue(capture('connectionId'), 'connectionId'),
      serviceId: stringValue(capture('serviceName'), 'serviceName'),
      layerIds: {
        parcels: integerValue(capture('parcelsLayerId'), 'parcelsLayerId'),
        zoning: integerValue(capture('zoningLayerId'), 'zoningLayerId'),
      },
      jobs: {
        esriMcp: stringValue(capture('esriMcpJobId'), 'esriMcpJobId'),
        gpServer: stringValue(capture('gpServerJobId'), 'gpServerJobId'),
        directAnalysis: stringValue(capture('directAnalysisJobId'), 'directAnalysisJobId'),
      },
      gp: {
        jobId: stringValue(capture('esriMcpJobId'), 'esriMcpJobId'),
        serviceId: stringValue(capture('esriMcpServiceId'), 'esriMcpServiceId'),
        taskName: stringValue(capture('esriMcpTaskName'), 'esriMcpTaskName'),
        processId: stringValue(capture('esriMcpProcessId'), 'esriMcpProcessId'),
        resultPackageId: stringValue(capture('esriMcpResultPackageId'), 'esriMcpResultPackageId'),
        artifactId: stringValue(capture('esriMcpArtifactId'), 'esriMcpArtifactId'),
      },
      artifactId: stringValue(capture('bufferArtifactId'), 'bufferArtifactId'),
      studio: Object.fromEntries(FAMILIES.map((family) => [family, {
        draftId: familyFacts[family].draftId,
        itemId: familyFacts[family].itemId,
        versionId: familyFacts[family].versionId,
        contentHash: familyFacts[family].contentHash,
        reopenedDraftId: familyFacts[family].reopenedDraftId,
      }])),
    },
  };
  const gpServerAction = checkpointActions(resume)
    .find((action) => action.id === 'buffer-esri-gpserver');
  const resultNames = gpServerAction ? stringArrayOrEmpty(gpServerAction.evidence?.resultNames) : [];
  if (resultNames.length > 0) boundary.resources.gpServerResultNames = resultNames;
  return boundary;
}

export async function observeConsoleApiWitness({ endpoint, credential, mode = 'witness', boundary, transport = fetchTransport }) {
  const baseUrl = validateEndpoint(endpoint, boundary.target);
  if (boundary.endpointSha256) {
    const actualEndpointSha256 = createHash('sha256').update(baseUrl.replace(/\/$/, '')).digest('hex');
    equal(actualEndpointSha256, boundary.endpointSha256, 'Studio handoff endpointSha256');
  }
  const token = stringValue(credential, 'scoped Console credential');
  if (!['full', 'witness'].includes(mode)) throw new Error('mode must be full or witness');
  if (mode !== 'witness') {
    throw new Error('direct server API production is witness-only; canonical approval must run through the Console browser');
  }
  const client = createClient(baseUrl, token, transport);

  await client.json('/healthz/ready', { auth: false });
  const version = await client.json('/api/v1/admin/version');
  stringValue(version.data?.version ?? version.version, 'candidate admin release version');
  const serverSourceRevision = stringValue(version.data?.sourceRevision, 'candidate admin sourceRevision');
  equal(serverSourceRevision, boundary.components['honua-server'], 'running server source revision');
  await assertResources(client, boundary.resources);
  // Witness mode must also be able to select already-resolved, still-active
  // proposals. Filtering the inventory to AwaitingApproval would make that
  // read-only path impossible after a successful approval.
  const listed = proposalList(await client.json('/api/v1/admin/proposals'));
  const proposals = {};
  const publications = {};
  const audit = {};

  for (const family of FAMILIES) {
    const facts = boundary.families[family];
    const proposal = await selectProposal(client, listed, facts);
    if (proposal.status === 'AwaitingApproval') {
      throw new Error(`witness mode cannot approve pending ${family} proposal ${proposal.proposalId}`);
    }
    assertResolvedProposal(proposal, family);
    const executionOperationId = stringValue(proposal.executionOperationId, `${family} executionOperationId`);

    const publication = await observePublication(client, baseUrl, family, facts, proposal.proposalId);
    const auditRow = await observeAudit(client, proposal.proposalId);
    proposals[family] = {
      draftId: facts.reopenedDraftId,
      generation: facts.generation,
      route: facts.route,
      proposalId: proposal.proposalId,
      executionOperationId,
    };
    publications[family] = publication;
    equal(auditRow.executionOperationId, executionOperationId, `${family} audit executionOperationId`);
    audit[family] = { correlationId: auditRow.correlationId, operationId: auditRow.executionOperationId };
  }

  const integrity = await client.json('/api/v1/admin/observability/audit/verify');
  if (integrity.verified !== true) throw new Error('audit integrity verification did not pass');
  for (const family of FAMILIES) {
    await client.ok(publications[family].publicUrl, { auth: false });
  }

  const witness = {
    schemaVersion: 'honua.console.api-witness/v1',
    status: 'observed',
    candidate: boundary.candidate,
    runtime: { serverSourceRevision },
    proposals,
    publications,
    audit,
    resources: boundary.resources,
    checks: { health: 'observed', audit: 'observed', publication: 'observed' },
  };
  rejectSecretSerialization(witness, 'Console API witness');
  return witness;
}

export async function exerciseConsoleReadApproveKeyRecipe({ endpoint, apiKey, boundary, transport = fetchTransport }) {
  const baseUrl = validateEndpoint(endpoint, boundary.target);
  const client = createClient(baseUrl, stringValue(apiKey, 'scoped Console API key'), transport, 'api-key');
  const listed = proposalList(await client.json('/api/v1/admin/proposals'));
  const proposals = {};

  for (const family of FAMILIES) {
    const facts = boundary.families[family];
    const proposal = await selectProposal(client, listed, facts);
    if (proposal.status === 'AwaitingApproval') {
      await client.json(
        `/api/v1/admin/proposals/${encodeURIComponent(proposal.proposalId)}/approve`,
        { method: 'POST' },
      );
    }
    const resolved = await client.json(
      `/api/v1/admin/proposals/${encodeURIComponent(proposal.proposalId)}`,
    );
    assertProposalBinding(resolved, facts);
    assertResolvedProposal(resolved, family);
    proposals[family] = {
      proposalId: stringValue(resolved.proposalId, `${family} proposalId`),
      executionOperationId: stringValue(resolved.executionOperationId, `${family} executionOperationId`),
    };
  }

  return {
    credential: 'api-key',
    grants: ['admin:read', 'admin:approve'],
    proposals,
  };
}

async function fetchTransport({ url, method, headers }) {
  const response = await fetch(url, { method, headers, redirect: 'error' });
  return { status: response.status, body: await response.text() };
}

function createClient(baseUrl, credential, transport, credentialKind = 'bearer') {
  return {
    async json(pathOrUrl, { method = 'GET', auth = true } = {}) {
      const { response, url } = await send(pathOrUrl, method, auth);
      if (!response.body) return {};
      try { return record(JSON.parse(response.body), `${method} ${url.pathname} response`); }
      catch { throw new Error(`${method} ${url.pathname} returned unreadable JSON`); }
    },
    async ok(pathOrUrl, { method = 'GET', auth = true } = {}) {
      await send(pathOrUrl, method, auth);
    },
  };

  async function send(pathOrUrl, method, auth) {
    const url = new URL(pathOrUrl, baseUrl);
    const sameOrigin = url.origin === new URL(baseUrl).origin;
    const headers = { accept: 'application/json', 'user-agent': 'honua-console-receipt/1' };
    if (auth) {
      if (!sameOrigin) throw new Error('refusing to send the Console credential to a different origin');
      if (credentialKind === 'api-key') headers['x-api-key'] = credential;
      else headers.authorization = `Bearer ${credential}`;
    }
    const response = await transport({ url: url.toString(), method, headers });
    if (response.status < 200 || response.status >= 300) {
      throw new Error(`${method} ${url.pathname} returned HTTP ${response.status}`);
    }
    return { response, url };
  }
}

async function assertResources(client, resources) {
  const connection = await client.json('/api/v1/admin/connections/');
  requireJsonIdentity(connection, resources.connectionId, 'connection inventory');
  const service = await client.json('/api/v1/admin/services/');
  requireJsonIdentity(service, resources.serviceId, 'service inventory');
  const layers = await client.json(
    `/api/v1/admin/connections/${encodeURIComponent(resources.connectionId)}/layers/?serviceName=${encodeURIComponent(resources.serviceId)}`,
  );
  for (const layerId of Object.values(resources.layerIds)) {
    requireJsonIdentity(layers, layerId, 'connection layer inventory');
  }
  for (const jobId of Object.values(resources.jobs)) {
    const job = await client.json(`/api/v1/admin/jobs/${encodeURIComponent(jobId)}`);
    requireJsonIdentity(job, jobId, 'job');
  }
  const gp = await client.json(`/api/v1/admin/jobs/${encodeURIComponent(resources.gp.jobId)}`);
  for (const value of Object.values(resources.gp)) requireJsonIdentity(gp, value, 'Esri GP job');
}

async function selectProposal(client, listed, facts) {
  if (facts.proposalId) {
    const detail = await client.json(`/api/v1/admin/proposals/${encodeURIComponent(facts.proposalId)}`);
    assertProposalBinding(detail, facts);
    return detail;
  }
  const candidates = [];
  for (const summary of listed) {
    const id = optionalString(summary.proposalId);
    if (!id) continue;
    const detail = await client.json(`/api/v1/admin/proposals/${encodeURIComponent(id)}`);
    if (proposalBinds(detail, facts)) candidates.push(detail);
  }
  if (candidates.length !== 1) {
    throw new Error(`expected exactly one ${facts.familyTitle} proposal bound to checkpoint identities; found ${candidates.length}`);
  }
  return candidates[0];
}

function proposalBinds(proposal, facts) {
  return [facts.itemId, facts.versionId, facts.reopenedDraftId, facts.route]
    .every((value) => containsIdentityToken(proposal, value));
}

function assertProposalBinding(proposal, facts) {
  if (!proposalBinds(proposal, facts)) throw new Error(`${facts.familyTitle} proposal is not bound to exact checkpoint identities`);
}

function assertResolvedProposal(proposal, family) {
  const status = stringValue(proposal.status, `${family} proposal status`);
  if (!['Submitted', 'Succeeded'].includes(status)) throw new Error(`${family} proposal resolved as ${status}, not Submitted/Succeeded`);
  stringValue(proposal.proposalId, `${family} proposalId`);
  stringValue(proposal.executionOperationId, `${family} executionOperationId`);
}

async function observePublication(client, baseUrl, family, facts, proposalId) {
  const response = await client.json(`/api/v1/studio/content-items?family=${family}&limit=100`);
  const rows = arrayValue(response.items ?? response.data?.items, `${family} Studio items`);
  const row = rows.find((candidate) => candidate?.itemId === facts.itemId);
  if (!row) throw new Error(`Studio ${family} item ${facts.itemId} was not found after approval`);
  equal(row.publishedVersionId, facts.versionId, `${family} publishedVersionId`);
  const badge = record(row.publication, `${family} publication badge`);
  const publicationId = stringValue(badge.publicationId, `${family} publicationId`);
  const detail = await client.json(`/api/v1/console/publications/${encodeURIComponent(publicationId)}`);
  const route = record(detail.route, `${family} publication route`);
  equal(route.publicationId, publicationId, `${family} publication route id`);
  const activeVersionId = stringValue(route.activeVersionId, `${family} activeVersionId`);
  const versions = arrayValue(detail.versions, `${family} publication versions`);
  const active = versions.find((version) => version?.versionId === activeVersionId);
  if (!active) throw new Error(`${family} publication omits its active immutable version`);
  equal(active.sourceContentId, facts.itemId, `${family} sourceContentId`);
  equal(active.contentVersionId, facts.versionId, `${family} contentVersionId`);
  equal(active.contentHash, facts.contentHash, `${family} contentHash`);
  const routePath = stringValue(route.routePath ?? badge.routePath, `${family} public route`);
  const publicUrl = new URL(routePath, baseUrl).toString();
  return { requestId: proposalId, itemId: facts.itemId, versionId: facts.versionId, status: 'published', publicationId, publicUrl };
}

async function observeAudit(client, proposalId) {
  const query = new URLSearchParams({ resourceType: 'operation_proposal', resourceId: proposalId, action: 'operation.applied', pageSize: '25' });
  const response = await client.json(`/api/v1/admin/observability/audit?${query}`);
  const rows = arrayValue(response.items, 'proposal audit rows');
  const row = rows.find((candidate) => candidate?.resourceId === proposalId && candidate?.action === 'operation.applied');
  if (!row) throw new Error(`audit trail omits approved proposal ${proposalId}`);
  if (String(row.outcome).toLowerCase() !== 'success') throw new Error(`audit trail did not record success for ${proposalId}`);
  let details;
  try { details = record(JSON.parse(stringValue(row.details, `audit details for ${proposalId}`)), 'audit details'); }
  catch { throw new Error(`audit details for ${proposalId} must be structured JSON`); }
  return {
    correlationId: stringValue(row.correlationId, `audit correlation for ${proposalId}`),
    executionOperationId: stringValue(details.executionOperationId, `audit executionOperationId for ${proposalId}`),
  };
}

function verifyCheckpointIntegrity(checkpoint) {
  const integrity = record(checkpoint.integrity, 'checkpoint.integrity');
  equal(integrity.algorithm, 'sha256', 'checkpoint.integrity.algorithm');
  const expected = stringValue(integrity.digest, 'checkpoint.integrity.digest');
  const unsigned = { ...checkpoint };
  delete unsigned.integrity;
  const actual = createHash('sha256').update(canonicalJson(unsigned)).digest('hex');
  equal(actual, expected, 'checkpoint.integrity.digest');
}

function verifyStudioHandoff(handoff, checkpoint, checkpointCaptures) {
  exactKeySet(handoff, [
    'schemaVersion', 'status', 'target', 'candidateId', 'releaseId', 'endpointSha256', 'source', 'components',
    'model', 'promptVersion', 'evalVersion', 'transcriptSha256', 'deterministic', 'lanes', 'joins',
    'consoleReceiptRequest', 'integrity',
  ], 'Studio handoff');
  equal(handoff.status, 'paused', 'Studio handoff.status');
  equal(handoff.target, checkpoint.target, 'Studio handoff.target');
  equal(handoff.candidateId, checkpoint.candidateId, 'Studio handoff.candidateId');
  equal(handoff.releaseId, checkpoint.releaseId, 'Studio handoff.releaseId');
  equal(canonicalJson(handoff.consoleReceiptRequest), canonicalJson(checkpoint.consoleReceiptRequest),
    'Studio handoff.consoleReceiptRequest');

  const integrity = record(handoff.integrity, 'Studio handoff.integrity');
  equal(integrity.algorithm, 'sha256', 'Studio handoff.integrity.algorithm');
  const unsigned = { ...handoff };
  delete unsigned.integrity;
  equal(
    createHash('sha256').update(canonicalJson(unsigned)).digest('hex'),
    stringValue(integrity.digest, 'Studio handoff.integrity.digest'),
    'Studio handoff.integrity.digest',
  );

  const deterministic = record(handoff.deterministic, 'Studio handoff.deterministic');
  exactKeySet(deterministic,
    checkpoint.target === 'aws-ecs' ? ['target', 'provisionReceiptSha256', 'checkpointDigest'] : ['target', 'checkpointDigest'],
    'Studio handoff.deterministic');
  equal(deterministic.target, checkpoint.target, 'Studio handoff.deterministic.target');
  equal(deterministic.checkpointDigest, checkpoint.integrity.digest, 'Studio handoff.deterministic.checkpointDigest');
  if (checkpoint.target === 'aws-ecs') {
    equal(deterministic.provisionReceiptSha256, checkpoint.provisionReceiptSha256,
      'Studio handoff.deterministic.provisionReceiptSha256');
    sha256Value(deterministic.provisionReceiptSha256, 'Studio handoff.deterministic.provisionReceiptSha256');
  } else if (deterministic.provisionReceiptSha256 !== undefined) {
    throw new Error('local-Docker Studio handoff must not contain provisionReceiptSha256');
  }

  const endpointSha256 = stringValue(handoff.endpointSha256, 'Studio handoff.endpointSha256');
  if (!/^[a-f0-9]{64}$/.test(endpointSha256)) throw new Error('Studio handoff.endpointSha256 must be SHA-256');
  const source = record(handoff.source, 'Studio handoff.source');
  exactKeySet(source, ['repository', 'sha'], 'Studio handoff.source');
  equal(source.repository, 'honua-io/honua-studio', 'Studio handoff.source.repository');
  revision(source.sha, 'Studio handoff.source.sha');
  const components = record(handoff.components, 'Studio handoff.components');
  const componentNames = ['honua-server', 'honua-sdk-js', 'honua-console', 'honua-studio', 'honua-devops', 'honua-iac'];
  if (canonicalJson(Object.keys(components).sort()) !== canonicalJson(componentNames.sort())) {
    throw new Error('Studio handoff.components must contain the exact platform component set');
  }
  for (const name of componentNames) revision(components[name], `Studio handoff.components.${name}`);
  equal(components['honua-sdk-js'], checkpoint.sourceRevision, 'Studio handoff.components.honua-sdk-js');
  equal(source.sha, components['honua-studio'], 'Studio handoff.source.sha');
  const model = record(handoff.model, 'Studio handoff.model');
  exactKeySet(model, ['provider', 'modelId'], 'Studio handoff.model');
  stringValue(model.provider, 'Studio handoff.model.provider');
  stringValue(model.modelId, 'Studio handoff.model.modelId');
  stringValue(handoff.promptVersion, 'Studio handoff.promptVersion');
  stringValue(handoff.evalVersion, 'Studio handoff.evalVersion');
  sha256Value(handoff.transcriptSha256, 'Studio handoff.transcriptSha256');

  const lanes = record(handoff.lanes, 'Studio handoff.lanes');
  const laneNames = ['admin', 'esriGp', 'nativeAnalysis', 'studioPublication'];
  if (canonicalJson(Object.keys(lanes).sort()) !== canonicalJson(laneNames.sort())) {
    throw new Error('Studio handoff.lanes must contain the exact real-model lane set');
  }
  for (const name of laneNames) {
    const lane = record(lanes[name], `Studio handoff.lanes.${name}`);
    exactKeySet(lane, ['promptSha256', 'transcriptSha256', 'calls'], `Studio handoff.lanes.${name}`);
    sha256Value(lane.promptSha256, `Studio handoff.lanes.${name}.promptSha256`);
    sha256Value(lane.transcriptSha256, `Studio handoff.lanes.${name}.transcriptSha256`);
    const calls = arrayValue(lane.calls, `Studio handoff.lanes.${name}.calls`);
    if (calls.some((call) => call?.status !== 'passed')) throw new Error(`Studio handoff lane ${name} is not fully passed`);
  }

  const joins = record(handoff.joins, 'Studio handoff.joins');
  for (const [name, value] of Object.entries(joins)) {
    const expected = name === 'candidateId' ? checkpoint.candidateId
      : name === 'releaseId' ? checkpoint.releaseId
        : checkpointCaptures[name];
    if (expected === undefined || canonicalJson(value) !== canonicalJson(expected)) {
      throw new Error(`Studio handoff join ${name} is not owned by the SDK checkpoint`);
    }
  }
  return endpointSha256;
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

function checkpointActions(resume) {
  const result = [];
  for (const stage of resume.completedStages ?? []) {
    for (const action of stage.actions ?? []) result.push(action);
  }
  return result;
}

function requiredCapture(captures, name) {
  if (!Object.hasOwn(captures, name)) throw new Error(`candidate boundary is missing capture ${name}`);
  return captures[name];
}

function captureOrMatch(captures, name, matches, pointer, fallback) {
  return captures[name] ?? matches[pointer] ?? fallback;
}

function proposalList(value) {
  return arrayValue(value.proposals ?? value.data?.proposals, 'proposal list');
}

function requireJsonIdentity(value, identity, label) {
  if (!containsExactScalar(value, identity)) throw new Error(`${label} response omits exact identity ${identity}`);
}

function containsExactScalar(value, identity) {
  if (value === identity) return true;
  if (Array.isArray(value)) return value.some((child) => containsExactScalar(child, identity));
  if (value && typeof value === 'object') return Object.values(value).some((child) => containsExactScalar(child, identity));
  return false;
}

function containsIdentityToken(value, identity) {
  if (containsExactScalar(value, identity)) return true;
  if (Array.isArray(value)) return value.some((child) => containsIdentityToken(child, identity));
  if (value && typeof value === 'object') return Object.values(value).some((child) => containsIdentityToken(child, identity));
  if (typeof value !== 'string') return false;
  const escaped = String(identity).replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
  return new RegExp(`(^|[^A-Za-z0-9_-])${escaped}($|[^A-Za-z0-9_-])`).test(value);
}

function validateEndpoint(value, target) {
  const url = new URL(stringValue(value, 'Console endpoint'));
  if (url.username || url.password || url.search || url.hash) throw new Error('Console endpoint must not contain credentials, query, or fragment');
  if (target === 'aws-ecs' && url.protocol !== 'https:') throw new Error('AWS Console receipt endpoint must use HTTPS');
  if (target === 'local-docker' && !['http:', 'https:'].includes(url.protocol)) throw new Error('local Console endpoint must use HTTP or HTTPS');
  return url.toString();
}

function record(value, label) {
  if (!value || typeof value !== 'object' || Array.isArray(value)) throw new Error(`${label} must be an object`);
  return value;
}

function arrayValue(value, label) {
  if (!Array.isArray(value)) throw new Error(`${label} must be an array`);
  return value;
}

function stringValue(value, label) {
  if (typeof value !== 'string' || !value.trim()) throw new Error(`${label} must be a non-empty string`);
  return value.trim();
}

function optionalString(value) {
  return typeof value === 'string' && value.trim() ? value.trim() : undefined;
}

function integerValue(value, label) {
  const parsed = typeof value === 'string' ? Number(value) : value;
  if (!Number.isInteger(parsed) || parsed < 0) throw new Error(`${label} must be a non-negative integer`);
  return parsed;
}

function positiveIntegerValue(value, label) {
  const parsed = integerValue(value, label);
  if (parsed < 1) throw new Error(`${label} must be a positive integer`);
  return parsed;
}

function revision(value, label) {
  const text = stringValue(value, label);
  if (!/^[a-f0-9]{40}$|^[a-f0-9]{64}$/.test(text)) throw new Error(`${label} must be a full Git or SHA-256 identity`);
  return text;
}

function sha256Value(value, label) {
  const text = stringValue(value, label);
  if (!/^[a-f0-9]{64}$/.test(text)) throw new Error(`${label} must be SHA-256`);
  return text;
}

function exactKeySet(value, expected, label) {
  const actual = Object.keys(value).sort();
  const wanted = [...expected].sort();
  if (canonicalJson(actual) !== canonicalJson(wanted)) throw new Error(`${label} has an unexpected field set`);
}

function stringArrayOrEmpty(value) {
  if (value === undefined) return [];
  const array = arrayValue(value, 'GPServer result names');
  return array.map((item, index) => stringValue(item, `GPServer result name ${index}`));
}

function pointerPairsOrEmpty(value) {
  if (value === undefined) return [];
  return arrayValue(value, 'Console receipt equalPointers').map((pair, index) => {
    const values = arrayValue(pair, `Console receipt equalPointers[${index}]`);
    if (values.length !== 2) throw new Error(`Console receipt equalPointers[${index}] must contain two pointers`);
    return values.map((pointer, pointerIndex) => stringValue(pointer, `Console receipt equalPointers[${index}][${pointerIndex}]`));
  });
}

function equal(actual, expected, label) {
  if (actual !== expected) throw new Error(`${label} must be ${String(expected)}; received ${String(actual)}`);
}
