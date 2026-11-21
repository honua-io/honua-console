import { expect, test, type Page, type TestInfo } from '@playwright/test';
import type { IncomingMessage, ServerResponse } from 'node:http';
import { fileURLToPath } from 'node:url';
import { startStaticBundleServer } from '../static-bundle-server.mjs';

// Export as files, the Exports list and an object-storage Connection, against a fixture admin API.
const connectionId = '6f1b2c9e-0000-4000-8000-000000000001';
const exportRecord = (status: string) => ({
  id: 'parcels-2026', title: 'Parcels', status, currentVersion: status === 'running' ? null : 'v1',
  source: { kind: 'layer', serviceId: 'county', layerId: 3, name: 'Parcels' },
  updatedAt: '2026-11-17T18:00:00Z',
  collectionUrl: 'http://127.0.0.1/api/v1/exports/parcels-2026/collection.json',
  publicUrl: null, notPublicReason: 'The Layer has a row filter, so its files stay on signed-in routes.',
  versions: status === 'running' ? [] : [{ version: 'v1', createdAt: '2026-11-17T18:00:00Z', featureCount: 1200, artifacts: [
    { kind: 'geoparquet', href: 'http://127.0.0.1/api/v1/exports/parcels-2026/v1/data.parquet', bytes: 3145728 },
    { kind: 'pmtiles', href: 'http://127.0.0.1/api/v1/exports/parcels-2026/v1/tiles.pmtiles', bytes: 2097152 },
  ] }],
});

type Call = { url: URL; method: string; send: (body: unknown, code?: number) => void };

const operatorClaims = [
  { type: 'http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier', value: 'fixture-operator' },
  { type: 'http://schemas.microsoft.com/ws/2008/06/identity/claims/role', value: 'admin' },
];

const bucketObjects = [
  { key: 'ortho/index.pmtiles', kind: 'pmtiles', bytes: 52428800, lastModified: '2026-11-16T10:00:00Z' },
  { key: 'ortho/parcels.parquet', kind: 'geoparquet', bytes: 7340032, lastModified: '2026-11-16T10:00:00Z' },
  { key: 'ortho/north.tif', kind: 'cog', bytes: 1073741824, lastModified: '2026-11-16T10:00:00Z' },
  { key: 'ortho/readme.txt', kind: 'other', bytes: 512, lastModified: '2026-11-16T10:00:00Z' },
];

// The fixture admin API; status follows the Export through create and unpublish.
class FixtureApi {
  status = 'running';
  readonly mutations: string[] = [];

  readonly handle = async (request: IncomingMessage, response: ServerResponse) => {
    const call: Call = {
      url: new URL(request.url!, 'http://localhost'),
      method: request.method ?? 'GET',
      send: (body, code = 200) => response.writeHead(code, { 'Content-Type': 'application/json' }).end(JSON.stringify(body)),
    };
    if (call.method !== 'GET' && !call.url.pathname.endsWith('/auth/csrf')) this.mutations.push(`${call.method} ${call.url.pathname}`);
    if (!(this.auth(call) || this.exports(call) || this.bucket(call))) {
      response.writeHead(404, { 'Content-Type': 'application/json' }).end('{}');
    }
  };

  private auth({ url, send }: Call) {
    if (url.pathname.endsWith('/auth/session')) send({ isAuthenticated: true, claims: operatorClaims });
    else if (url.pathname.endsWith('/auth/csrf')) send({ requestToken: 'fixture-only' });
    else return false;
    return true;
  }

  private exports({ url, method, send }: Call) {
    if (url.pathname === '/api/v1/admin/exports' && method === 'POST') {
      this.status = 'ready';
      send({ success: true, data: exportRecord('running'), message: 'Export started.' }, 202);
    } else if (url.pathname === '/api/v1/admin/exports') {
      send({ success: true, data: { items: [exportRecord(this.status)] } });
    } else if (url.pathname === '/api/v1/admin/exports/parcels-2026') {
      if (method === 'DELETE') this.status = 'unpublished';
      send({ success: true, data: exportRecord(this.status) });
    } else return false;
    return true;
  }

  private bucket({ url, method, send }: Call) {
    if (url.pathname === '/api/v1/admin/connections/object-storage/setup' && method === 'POST') {
      send({ success: true, data: { externalId: 'honua-ext-7f3a', roleNamePrefix: 'honua-connection-',
        quickCreateUrl: `https://quick-create.example/?bucket=${url.searchParams.get('bucket')}` } });
    } else if (url.pathname === '/api/v1/admin/connections/test' && method === 'POST') {
      send({ success: true, data: { isHealthy: true, message: 'Listed one key under ortho/.' } });
    } else if (url.pathname === '/api/v1/admin/connections' && method === 'POST') {
      send({ success: true, data: { id: connectionId, name: 'county-imagery' } }, 201);
    } else if (url.pathname === `/api/v1/admin/connections/${connectionId}/objects`) {
      send({ success: true, data: { bucket: 'county-imagery-2026', prefix: url.searchParams.get('prefix') ?? '',
        folders: ['ortho/2026/'], objects: bucketObjects, truncated: false } });
    } else return false;
    return true;
  }
}

async function capture(page: Page, testInfo: TestInfo, name: string) {
  await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(page.viewportSize()!.width);
  await testInfo.attach(`${name}-${testInfo.project.name}`, { body: await page.screenshot({ fullPage: true }), contentType: 'image/png' });
}

async function exportJourney(page: Page, testInfo: TestInfo, origin: string) {
  await page.goto(`${origin}/console/operate/exports/new?serviceId=county&layerId=3&name=Parcels`);
  await expect(page.locator('[data-export-source]')).toContainText('Parcels');
  await page.locator('[data-export-id]').fill('parcels-2026');
  await capture(page, testInfo, 'export-new');
  await page.getByRole('button', { name: 'Export as files', exact: true }).click();
  await expect(page.locator('[data-export-started]')).toContainText('Export started.');
  await page.locator('[data-export-link]').click();
  await expect(page.locator('[data-export-version="v1"]')).toContainText('GeoParquet · 3 MB');
  await expect(page.locator('[data-export-access]')).toContainText('row filter');
  await capture(page, testInfo, 'export-detail');

  await page.goto(`${origin}/console/operate/exports`);
  await expect(page.locator('[data-export-row="parcels-2026"]')).toContainText('Signed-in');
  await capture(page, testInfo, 'exports-list');
  await page.locator('[data-export-unpublish="parcels-2026"]').click();
  await page.locator('[data-console-confirm-accept]').click();
  await expect(page.locator('[data-export-notice]')).toContainText('Unpublished parcels-2026.');
  await expect(page.locator('[data-export-unpublish="parcels-2026"]')).toBeDisabled();
}

async function bucketJourney(page: Page, testInfo: TestInfo, origin: string) {
  await page.goto(`${origin}/console/operate/connections/new/object-storage`);
  await page.locator('[data-object-storage-field="name"]').fill('county-imagery');
  await page.locator('[data-object-storage-field="bucket"]').fill('county-imagery-2026');
  await page.locator('[data-object-storage-field="prefix"]').fill('ortho/');
  await page.locator('[data-object-storage-field="region"]').fill('us-west-2');
  await page.getByRole('button', { name: 'Get the quick-create link', exact: true }).click();
  await expect(page.locator('[data-object-storage-setup-result]')).toContainText('honua-ext-7f3a');
  await page.locator('[data-object-storage-field="role"]').fill('arn:aws:iam::123456789012:role/honua-connection-imagery');
  await page.getByRole('button', { name: 'Test', exact: true }).click();
  await expect(page.locator('[data-object-storage-test-result]')).toContainText('Bucket reachable');
  await capture(page, testInfo, 'object-storage-new');
  await page.getByRole('button', { name: 'Add connection', exact: true }).click();
  await page.locator('[data-object-storage-created] a').click();
  await expect(page.locator('[data-objects-file="ortho/north.tif"]')).toContainText('Cloud-optimized GeoTIFF');
  await expect(page.locator('[data-objects-file="ortho/index.pmtiles"]')).toContainText('PMTiles');
  await capture(page, testInfo, 'bucket-browser');
}

test('Export as files, list, unpublish and connect a bucket', async ({ page }, testInfo) => {
  const api = new FixtureApi();
  const server = await startStaticBundleServer(
    fileURLToPath(new URL('../../../artifacts/static-publish/wwwroot/console', import.meta.url)), 0, {
      configuration: { apiBaseUrl: '/', auth: { mode: 'builtin' }, v4Enabled: true },
      handleApi: api.handle,
    });
  try {
    const address = server.address();
    if (!address || typeof address === 'string') throw new Error('Fixture did not bind TCP');
    const origin = `http://127.0.0.1:${address.port}`;
    await exportJourney(page, testInfo, origin);
    await bucketJourney(page, testInfo, origin);
    expect(api.mutations).toEqual([
      'POST /api/v1/admin/exports',
      'DELETE /api/v1/admin/exports/parcels-2026',
      'POST /api/v1/admin/connections/object-storage/setup',
      'POST /api/v1/admin/connections/test',
      'POST /api/v1/admin/connections',
    ]);
  } finally {
    await new Promise<void>((resolve, reject) => server.close(error => error ? reject(error) : resolve()));
  }
});
