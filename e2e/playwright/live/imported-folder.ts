import { randomUUID } from 'node:crypto';
import { expect, type AdminApi, type AdminConnection } from './admin-api';
import { sourceConnectionBody } from './source-db';

const IMPORT_COMPLETED = 8;
const IMPORT_PATH = '/api/v1/admin/import/geoservices';
const SOURCE_SERVICE = 'https://sampleserver6.arcgisonline.com/arcgis/rest/services/Wildfire/FeatureServer';
const MANAGED_CONNECTION_ID = '77460000-0000-4000-8000-000000007746';
const MANAGED_CONNECTION_NAMES = new Set(['Imported data', 'Imported data (built-in)']);

interface ImportedService {
  name: string;
  layerId: number;
}

export interface ImportedFolder {
  id: string;
  connectionId: string;
  name: string;
  services: ImportedService[];
}

export async function seedImportedFolder(admin: AdminApi): Promise<ImportedFolder> {
  const suffix = randomUUID().replaceAll('-', '').slice(0, 12);
  const name = `e2e-folder-${suffix}`;
  const listing = await admin.request.get('/api/v1/admin/folders');
  expect(listing.ok(), 'read the current folder snapshot').toBeTruthy();
  const etag = listing.headers().etag;
  expect(etag, 'folder creation requires a real snapshot ETag').toBeTruthy();
  const created = await admin.request.post('/api/v1/admin/folders', {
    headers: { 'If-Match': etag }, data: { name },
  });
  expect(created.status(), 'create the isolated journey folder').toBe(201);
  const id = (await created.json()).data.metadata.id;
  expect(id).toEqual(expect.any(String));
  expect(id.length).toBeGreaterThan(0);

  let connectionId: string | undefined;
  const services: ImportedService[] = [];
  for (const index of [1, 2]) {
    const service = await importFolderService(admin, { folderId: id, name: `e2e_tree_${suffix}_${index}` });
    if (!connectionId) {
      connectionId = await resolveImportOwnerConnectionId(admin, service);
    } else {
      const layers = await admin.listLayers(connectionId, service.name);
      expect(layers.map(layer => layer.layerId)).toContain(service.layerId);
    }
    services.push(service);
  }
  expect(connectionId, 'resolve the connection owning imported folder layers').toBeTruthy();
  return { id, connectionId: connectionId!, name, services };
}

function isManagedConnection(connection: AdminConnection): boolean {
  return connection.connectionId.toLowerCase() === MANAGED_CONNECTION_ID
    || MANAGED_CONNECTION_NAMES.has(connection.name);
}

// Auto-published imports belong to the server's built-in managed connection, not a separate
// source connection. Prefer the managed connection first so we match the owner Console renders.
async function resolveImportOwnerConnectionId(admin: AdminApi, service: ImportedService): Promise<string> {
  const databaseConnections = (await admin.listConnections())
    .filter(connection => connection.storageType !== 'ObjectStorage');
  const candidates = databaseConnections.length > 0
    ? [...databaseConnections].sort((a, b) => Number(isManagedConnection(b)) - Number(isManagedConnection(a)))
    : [await admin.createConnection(sourceConnectionBody(`e2e-folder-${service.name}`))];
  for (const candidate of candidates) {
    const layers = await admin.listLayers(candidate.connectionId, service.name);
    if (layers.some(layer => layer.layerId === service.layerId)) return candidate.connectionId;
  }
  throw new Error(`No admin connection listed imported layer ${service.layerId} for service ${service.name}.`);
}

async function importFolderService(admin: AdminApi, target: { folderId: string; name: string }) {
  const response = await admin.request.post(`${IMPORT_PATH}/start`, { data: {
    serviceUrl: SOURCE_SERVICE, layerId: 0, tableName: target.name,
    serviceName: target.name, folderId: target.folderId,
    autoPublish: true, importAttachments: false,
  } });
  expect(response.status(), 'admit the real folder import').toBe(202);
  const accepted = await response.json();
  expect(accepted.jobId).toEqual(expect.any(String));
  expect(accepted.plan.folderId).toBe(target.folderId);
  expect(accepted.plan.serviceName).toBe(target.name);
  const statusPath = `${IMPORT_PATH}/jobs/${encodeURIComponent(accepted.jobId)}`;
  await expect.poll(async () => (await admin.getJson(statusPath)).status >= IMPORT_COMPLETED,
    { timeout: 180_000, intervals: [1000, 2000, 5000], message: `import ${target.name} completes` },
  ).toBe(true);
  const progress = await admin.getJson(statusPath);
  expect(progress.status, progress.errorMessage ?? 'import completed successfully').toBe(IMPORT_COMPLETED);
  expect(progress.failedFeatures).toBe(0);
  expect(progress.featuresProcessed).toBeGreaterThan(0);
  expect(progress.serviceName).toBe(target.name);
  expect(Number.isInteger(progress.publishedLayerId)).toBe(true);
  return { name: progress.serviceName as string, layerId: progress.publishedLayerId as number };
}
