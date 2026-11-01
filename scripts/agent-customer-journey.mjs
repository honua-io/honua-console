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

/** Call the manager's actual MCP tools and let the customer-facing test observe each state. */
export async function exerciseAgentRecovery({ managerUrl, proposerKey, approverKey, stopRedis, observe }) {
  if (!proposerKey || !approverKey || proposerKey === approverKey) throw new Error('Separate proposer and approver credentials are required');
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
  await stopRedis();
  const findingResult = await poll(() => mcp.call('honua_ops_findings', { rule: 'managed-compose-service-unhealthy' }),
    result => result.findings?.some(item => item.subject?.workloadId === 'redis'));
  const finding = findingResult.findings.find(item => item.subject?.workloadId === 'redis');
  await observe('finding', finding);
  const proposed = await mcp.call('honua_propose_finding', { findingId: finding.id, candidateId: 'compose-local' });
  if (!proposed.proposalId) throw new Error('MCP did not create a restart proposal');
  const path = `/api/v1/admin/proposals/${encodeURIComponent(proposed.proposalId)}`;
  const pending = await poll(() => api(managerUrl, approverKey, path), value => value.status?.toLowerCase() === 'awaitingapproval');
  await observe('pending', pending);
  await api(managerUrl, approverKey, `${path}/approve`, {});
  const completed = await poll(() => api(managerUrl, approverKey, path),
    value => ['completed', 'succeeded', 'failed', 'rejected', 'cancelled'].includes(value.status?.toLowerCase()));
  if (!['completed', 'succeeded'].includes(completed.status.toLowerCase())) throw new Error('The approved restart failed');
  const remaining = await poll(() => mcp.call('honua_ops_findings', { rule: 'managed-compose-service-unhealthy' }),
    value => !value.findings?.some(item => item.subject?.workloadId === 'redis'));
  await observe('recovered', { proposal: completed, findings: remaining });
  return { target, finding, proposalId: proposed.proposalId, completed };
}

/** Ingest and publish through MCP, then validate the resulting server metadata. */
export async function exerciseAgentPublication({ serverUrl, proposerKey, verifierKey, datasets, observe }) {
  const mcp = new AgentMcpClient(serverUrl, proposerKey);
  await mcp.initialize();
  const output = [];
  for (const dataset of datasets) {
    const imported = await mcp.call('honua_ingest_dataset', { format: dataset.format, data: dataset.data, datasetName: dataset.name });
    if (!imported.success || !imported.connectionId || !imported.table || !imported.geometryColumn || !imported.primaryKey)
      throw new Error(`MCP ${dataset.format} import did not return a publishable table`);
    const result = await mcp.call('honua_publish_service', {
      connectionId: imported.connectionId, schema: imported.schema, table: imported.table,
      layerName: dataset.name, serviceName: dataset.name, geometryColumn: imported.geometryColumn,
      geometryType: dataset.geometryType, primaryKey: imported.primaryKey, srid: 4326,
    });
    if (result.proposalId) {
      if (!verifierKey || proposerKey === verifierKey) throw new Error('A separate publisher approver is required');
      const proposalPath = `/api/v1/admin/proposals/${encodeURIComponent(result.proposalId)}`;
      await poll(() => api(serverUrl, verifierKey, proposalPath), value => value.status?.toLowerCase() === 'awaitingapproval');
      await api(serverUrl, verifierKey, `${proposalPath}/approve`, {});
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
    const record = { dataset: dataset.name, format: dataset.format, geometryType: layer.geometryType, fields: layer.fields, actorOperationId: result.operationInstanceId };
    await observe('published', record);
    output.push(record);
  }
  return output;
}
