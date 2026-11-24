import { AgentMcpClient } from './agent-mcp-client.mjs';

const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));
async function poll(read, predicate, timeoutMs = 180_000) {
  const deadline = Date.now() + timeoutMs;
  while (Date.now() < deadline) {
    const value = await read();
    if (predicate(value)) return value;
    await sleep(2_000);
  }
  throw new Error('Timed out waiting for the real server state');
}
async function api(base, key, path, data) {
  const response = await fetch(new URL(path, base), {
    method: data === undefined ? 'GET' : 'POST',
    headers: { 'X-API-Key': key, ...(data === undefined ? {} : { 'Content-Type': 'application/json' }) },
    ...(data === undefined ? {} : { body: JSON.stringify(data) }), signal: AbortSignal.timeout(30_000),
  });
  if (!response.ok) throw new Error(`Server HTTP ${response.status} on ${path.split('?')[0]}`);
  return response.json();
}

/** Human approval refuses an explicit credential. The proposal must still be waiting afterwards. */
export async function refuseApiKeyApproval(base, key, proposalPath) {
  const response = await fetch(new URL(`${proposalPath}/approve`, base), {
    method: 'POST',
    headers: { 'X-API-Key': key, 'Content-Type': 'application/json' },
    body: '{}',
    signal: AbortSignal.timeout(30_000),
  });
  await response.arrayBuffer();
  if (response.status !== 403)
    throw new Error(`API-key approval must be refused with HTTP 403, got ${response.status} on ${proposalPath}/approve`);
  const pending = await api(base, key, proposalPath);
  if (String(pending.status ?? '').toLowerCase() !== 'awaitingapproval')
    throw new Error('API-key approval changed the proposal');
}

/** A missing approval-review route predates the human-session rule and still accepts an API key. */
export async function approveRecordedProposal(request) {
  if (typeof request.approve !== 'function') throw new Error('A Console approval is required');
  const review = await fetch(new URL(`${request.proposalPath}/approval-review`, request.base), {
    headers: { 'X-API-Key': request.key },
    signal: AbortSignal.timeout(30_000),
  });
  await review.arrayBuffer();
  if (review.status === 404) {
    await api(request.base, request.key, `${request.proposalPath}/approve`, {});
    return;
  }
  await refuseApiKeyApproval(request.base, request.key, request.proposalPath);
  await request.approve({ proposalId: request.proposalId, consoleProposalId: request.consoleProposalId });
}

/** Call the manager's actual MCP tools and let the customer-facing test observe each state. */
export async function exerciseAgentRecovery({ managerUrl, proposerKey, approverKey, service, stopService, observe, approve }) {
  if (!service) throw new Error('A managed Compose service to stop is required');
  if (!proposerKey || !approverKey || proposerKey === approverKey) throw new Error('Separate proposer and approver credentials are required');
  if (typeof approve !== 'function') throw new Error('A Console approval is required');
  const mcp = new AgentMcpClient(managerUrl, proposerKey);
  await mcp.initialize();
  const release = await mcp.call('honua_platform_release_status', {});
  const target = release.serving?.find(item => item.id === 'compose-local');
  if (target?.observationStatus !== 'observed' || !target.observedImageId || !target.serverVersion)
    throw new Error('Manager did not report a live serving image and version');
  const preflight = await api(managerUrl, approverKey, '/api/v1/admin/deploy/preflight?includeDiagnostics=true');
  const readback = preflight.platformRelease?.serving?.find(item => item.id === target.id);
  if (readback?.observedImageId !== target.observedImageId || readback?.serverVersion !== target.serverVersion ||
      readback?.observationStatus !== 'observed') throw new Error('MCP release status differs from manager API readback');
  await observe('revision', target);
  await stopService();
  const findingResult = await poll(() => mcp.call('honua_ops_findings', { rule: 'managed-compose-service-unhealthy' }),
    result => result.findings?.some(item => item.subject?.workloadId === service));
  const finding = findingResult.findings.find(item => item.subject?.workloadId === service);
  const unhealthy = await poll(() => api(managerUrl, approverKey, '/api/v1/admin/observability/ops-health'), value =>
    value.managedTargets?.some(item => item.targetId === 'compose-local' && item.services?.some(entry =>
      entry.name === service && entry.status?.toLowerCase() === 'unhealthy')));
  await observe('finding', { finding, health: unhealthy });
  const proposed = await mcp.call('honua_propose_finding', { findingId: finding.id, candidateId: 'compose-local' });
  if (!proposed.proposalId) throw new Error('MCP did not create a restart proposal');
  const path = `/api/v1/admin/proposals/${encodeURIComponent(proposed.proposalId)}`;
  const pending = await poll(() => api(managerUrl, approverKey, path), value => value.status?.toLowerCase() === 'awaitingapproval');
  await observe('pending', pending);
  await approveRecordedProposal({ base: managerUrl, key: approverKey, proposalPath: path, approve, proposalId: proposed.proposalId, consoleProposalId: `manager:${proposed.proposalId}` });
  const completed = await poll(() => api(managerUrl, approverKey, path),
    value => ['completed', 'succeeded', 'failed', 'rejected', 'cancelled'].includes(value.status?.toLowerCase()));
  if (!['completed', 'succeeded'].includes(completed.status.toLowerCase())) throw new Error('The approved restart failed');
  const remaining = await poll(() => mcp.call('honua_ops_findings', { rule: 'managed-compose-service-unhealthy' }),
    value => !value.findings?.some(item => item.subject?.workloadId === service));
  const healthy = await poll(() => api(managerUrl, approverKey, '/api/v1/admin/observability/ops-health'), value =>
    value.managedTargets?.some(item => item.targetId === 'compose-local' && item.services?.some(entry =>
      entry.name === service && entry.status?.toLowerCase() === 'healthy')));
  await observe('recovered', { proposal: completed, findings: remaining, health: healthy });
  return { target, finding, proposalId: proposed.proposalId, completed };
}

/** Why an import is not publishable, without echoing dataset content. */
export function importSummary(imported) {
  const missing = ['connectionId', 'schema', 'table', 'geometryColumn', 'primaryKey'].filter(name => !imported?.[name]);
  return JSON.stringify({ success: imported?.success ?? null, errorCode: imported?.errorCode ?? null,
    errorMessage: imported?.errorMessage ?? null, missing, warnings: imported?.warnings ?? [] });
}

async function resolveRegisteredConnection(serverUrl, verifierKey, connectionRef) {
  const connections = await api(serverUrl, verifierKey, '/api/v1/admin/connections/');
  const registered = connections.data?.filter(connection =>
    connection.name === connectionRef || connection.connectionId === connectionRef) ?? [];
  const connectionId = registered[0]?.connectionId;
  if (registered.length !== 1 || !connectionId)
    throw new Error(`Connection ${connectionRef} does not resolve to one registered connection`);
  return connectionId;
}

async function resolveConsoleLayer(serverUrl, verifierKey, { imported, serviceName, connectionId }) {
  const layerQuery = new URLSearchParams({ serviceName });
  const layersPath = `/api/v1/admin/connections/${encodeURIComponent(connectionId)}/layers/`;
  const inventory = await api(serverUrl, verifierKey, `${layersPath}?${layerQuery}`);
  const matches = inventory.data?.items?.filter(item =>
    item.serviceName === serviceName && item.schema === imported.schema && item.table === imported.table) ?? [];
  const storageLayerId = matches[0]?.storageLayerId ?? matches[0]?.layerId;
  if (matches.length !== 1 || !Number.isInteger(storageLayerId))
    throw new Error(`Published ${serviceName} does not resolve to one stored layer for its imported table`);
  return storageLayerId;
}

/** Ingest and publish through MCP, then validate the resulting server metadata. */
export async function exerciseAgentPublication({ serverUrl, proposerKey, verifierKey, expectedActor, catalogConnectionName, datasets, observe, approve }) {
  if (!expectedActor) throw new Error('The server MCP actor identity is required');
  if (typeof approve !== 'function') throw new Error('A Console approval is required');
  const mcp = new AgentMcpClient(serverUrl, proposerKey);
  await mcp.initialize();
  const output = [];
  for (const dataset of datasets) {
    const imported = await mcp.call('honua_ingest_dataset', { format: dataset.format, data: dataset.data, datasetName: dataset.name });
    const connectionRef = imported.connectionId || catalogConnectionName;
    if (!imported.success || !connectionRef || !imported.schema || !imported.table || !imported.geometryColumn || !imported.primaryKey)
      throw new Error(`MCP ${dataset.format} import did not return a publishable table: ${importSummary(imported)}`);
    const connectionId = await resolveRegisteredConnection(serverUrl, verifierKey, connectionRef);
    const result = await mcp.call('honua_publish_service', {
      connectionId, schema: imported.schema, table: imported.table,
      layerName: dataset.name, serviceName: dataset.name, geometryColumn: imported.geometryColumn,
      geometryType: dataset.geometryType, primaryKey: imported.primaryKey, srid: 4326,
    });
    if (result.proposalId) {
      if (!verifierKey || proposerKey === verifierKey) throw new Error('A separate publisher approver is required');
      const proposalPath = `/api/v1/admin/proposals/${encodeURIComponent(result.proposalId)}`;
      await poll(() => api(serverUrl, verifierKey, proposalPath), value => value.status?.toLowerCase() === 'awaitingapproval');
      await approveRecordedProposal({
        base: serverUrl, key: verifierKey, proposalPath, approve,
        proposalId: result.proposalId, consoleProposalId: result.proposalId,
      });
      const terminal = await poll(() => api(serverUrl, verifierKey, proposalPath),
        value => ['completed', 'succeeded', 'failed', 'rejected', 'cancelled'].includes(value.status?.toLowerCase()));
      if (!['completed', 'succeeded'].includes(terminal.status.toLowerCase())) throw new Error('The approved publication failed');
    }
    const servicePath = `/rest/services/${encodeURIComponent(dataset.name)}/FeatureServer`;
    const service = await api(serverUrl, verifierKey, `${servicePath}?f=json`);
    const id = service.layers?.[0]?.id;
    if (!Number.isInteger(id)) throw new Error(`Published ${dataset.format} has no layer`);
    const layer = await api(serverUrl, verifierKey, `${servicePath}/${id}?f=json`);
    if (layer.geometryType !== `esriGeometry${dataset.geometryType}`) throw new Error(`Published ${dataset.format} has the wrong geometry type`);
    for (const [field, type] of Object.entries(dataset.fields))
      if (!layer.fields?.some(item => item.name === field && item.type === type)) throw new Error(`Published ${dataset.format} is missing typed field ${field}`);
    const storageLayerId = await resolveConsoleLayer(serverUrl, verifierKey, { imported, serviceName: dataset.name, connectionId });
    if (!result.operationInstanceId) throw new Error('Publication did not report its operation identity');
    const auditQuery = new URLSearchParams({ resourceType: 'operation_instance', resourceId: result.operationInstanceId, pageSize: '100' });
    const audit = await poll(() => api(serverUrl, verifierKey, `/api/v1/admin/observability/audit?${auditQuery}`),
      value => value.items?.some(item => item.action === 'operation.completed'));
    if (!audit.items.some(item => item.action === 'operation.completed' && item.actor === expectedActor))
      throw new Error('Published operation audit does not identify the MCP actor');
    const record = {
      dataset: dataset.name, format: dataset.format, connectionId,
      layerId: id, storageLayerId, geometryType: layer.geometryType, fields: layer.fields,
      actorOperationId: result.operationInstanceId, actor: expectedActor,
    };
    await observe('published', record);
    output.push(record);
  }
  return output;
}
