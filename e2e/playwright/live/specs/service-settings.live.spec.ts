import { randomUUID } from 'node:crypto';
import type { Page } from '@playwright/test';
import { test, expect, type AdminApi } from '../admin-api';
import { GEOSERVICES_NOT_FOUND, GEOSERVICES_TOKEN_REQUIRED, anonymousGeoServicesQuery, geoServicesErrorCode } from '../geoservices';
import { ensurePublishedLayer, type SeededLayer } from '../seeded-layer';
import { SOURCE_DB } from '../source-db';

// Live e2e for the Operate service-settings surface (/operate/services/{svc}/settings): it validates that
// EVERY service setting the console exposes actually takes effect on the live server — driven through the
// real UI and verified through an independent path (the public protocol endpoints + the admin settings API),
// per the ServerStateVerifier discipline.
//
// Two settings groups are covered:
//   1. Protocol enablement — toggling a protocol on makes the layer serve through that protocol's endpoint,
//      and toggling it off makes that per-service endpoint stop serving (negative check).
//   2. Access policy (the access tier) — the anonymous-read toggle gates the live FeatureServer query for an
//      unauthenticated caller (features when public, the GeoServices 499 error when private).
//
// Target: a service this spec seeds itself through the admin API from its own table (see seeded-layer.ts).

const stamp = randomUUID().replaceAll('-', '').slice(0, 10);
const ADMIN_KEY = process.env.HONUA_CONSOLE_E2E_ADMIN_KEY ?? 'honua-console-dev-key';
const ADMIN_HEADERS = { 'X-API-Key': ADMIN_KEY };

// Protocols a vector (polygon) layer can serve, each with the endpoint that serves it and the evidence that
// it serves THIS seeded layer. The global endpoints (WFS, OGC API, OData, STAC) list a service's layer only
// while that service enables the protocol, so finding the seeded layer there proves the setting took effect.
// (Raster/elevation/GP protocols such as ImageServer, Wcs, Terrain, Elevation, GPServer and Grpc do not
// apply to a vector layer.)
interface ProtocolCase {
  readonly protocol: string;
  readonly path: (layer: SeededLayer) => string;
  readonly exposes: (body: string, layer: SeededLayer) => boolean;
}

const listsLayer = (items: { id?: unknown }[] | undefined, layer: SeededLayer) =>
  (items ?? []).some((item) => String(item.id) === String(layer.layerId));

const VECTOR_PROTOCOLS: ProtocolCase[] = [
  { protocol: 'FeatureServer', path: (l) => `/rest/services/${l.serviceName}/FeatureServer?f=json`, exposes: (b, l) => listsLayer(JSON.parse(b).layers, l) },
  { protocol: 'MapServer', path: (l) => `/rest/services/${l.serviceName}/MapServer?f=json`, exposes: (b, l) => listsLayer(JSON.parse(b).layers, l) },
  { protocol: 'Wms', path: (l) => `/rest/services/${l.serviceName}/MapServer/WMS?service=WMS&request=GetCapabilities&version=1.3.0`, exposes: (b, l) => b.includes(`<Name>${l.layerName}</Name>`) },
  { protocol: 'Wmts', path: (l) => `/rest/services/${l.serviceName}/MapServer/WMTS?service=WMTS&request=GetCapabilities&version=1.0.0`, exposes: (b, l) => b.includes(`<ows:Identifier>${l.layerId}</ows:Identifier>`) },
  { protocol: 'Wfs20', path: () => '/wfs?service=WFS&request=GetCapabilities&version=2.0.0', exposes: (b, l) => b.includes(`<Name>honua:${l.serviceName}</Name>`) },
  { protocol: 'OgcFeatures', path: () => '/ogc/features/collections?f=json', exposes: (b, l) => listsLayer(JSON.parse(b).collections, l) },
  { protocol: 'OGC-API-Tiles', path: () => '/ogc/tiles/collections?f=json', exposes: (b, l) => listsLayer(JSON.parse(b).collections, l) },
  { protocol: 'OData', path: () => '/odata/Layers', exposes: (b, l) => (JSON.parse(b).value ?? []).some((v: any) => v.Id === l.layerId) },
  { protocol: 'Stac', path: () => '/stac/collections?f=json', exposes: (b, l) => listsLayer(JSON.parse(b).collections, l) },
];

async function openServiceSettings(page: Page, serviceName: string): Promise<void> {
  await page.goto(`/operate/services/${serviceName}/settings`);
  await expect(page.locator('[data-service-config]')).toBeVisible({ timeout: 30_000 });
}

// Read from the server rather than from the page: the checkboxes appear while the page is still rendering,
// so a count can miss one. A missing field fails the test instead of shrinking what it verifies.
async function protocolSettings(admin: AdminApi, serviceName: string): Promise<{ available: string[]; enabled: string[] }> {
  const settings = (await admin.getJson(`/api/v1/admin/services/${serviceName}/settings`)).data;
  expect(Array.isArray(settings?.availableProtocols), `${serviceName} settings report availableProtocols`).toBe(true);
  expect(Array.isArray(settings?.enabledProtocols), `${serviceName} settings report enabledProtocols`).toBe(true);
  return { available: settings.availableProtocols, enabled: settings.enabledProtocols };
}

test.describe('Operate · Service settings (live)', () => {
  let target: SeededLayer;
  let available: string[];

  test.beforeEach(async ({ admin }) => {
    test.setTimeout(180_000);
    target = await ensurePublishedLayer(admin, {
      connectionName: 'e2e-settings-src',
      table: SOURCE_DB.settingsTable,
      layerName: `E2E Settings ${stamp}`,
      serviceName: `e2e_settings_${stamp}`,
    });
    available = (await protocolSettings(admin, target.serviceName)).available;
  });

  test('every applicable protocol enabled via the UI serves through its own endpoint', async ({ page, admin }) => {
    test.slow();
    const base = admin.serverUrl;
    const expected = VECTOR_PROTOCOLS.filter(({ protocol }) => available.includes(protocol));
    const expectedIds = expected.map(({ protocol }) => protocol);
    expect(expectedIds, 'the server offers vector protocols to verify').not.toHaveLength(0);

    // Drive the real service-config UI: select exactly the vector protocols, then save.
    await openServiceSettings(page, target.serviceName);
    for (const protocol of available) {
      await page.locator(`[data-protocol="${protocol}"]`).setChecked(expectedIds.includes(protocol));
    }
    await page.locator('[data-save-protocols]').click();
    await expect(page.locator('[data-config-result]')).toContainText('Updated', { timeout: 30_000 });

    // The settings API reports exactly that selection (independent path from the UI write).
    const { enabled } = await protocolSettings(admin, target.serviceName);
    expect([...enabled].sort()).toEqual([...expectedIds].sort());

    // Each selected protocol serves the seeded layer through its public endpoint.
    for (const { protocol, path, exposes } of expected) {
      const url = `${base}${path(target)}`;
      await expect
        .poll(async () => {
          const res = await page.request.get(url, { headers: ADMIN_HEADERS });
          return res.ok() && exposes(await res.text(), target);
        }, { message: `${protocol}: ${path(target)} serves ${target.layerName}`, timeout: 20_000 })
        .toBe(true);
    }
  });

  test('disabling a protocol via the UI stops that per-service endpoint serving', async ({ page, admin }) => {
    test.slow();
    const base = admin.serverUrl;
    const mapServerPath = `/rest/services/${target.serviceName}/MapServer?f=json`;
    const mapServerError = () => geoServicesErrorCode(page.request, `${base}${mapServerPath}`, ADMIN_HEADERS);

    expect(available, 'this testbed offers MapServer').toContain('MapServer');
    await openServiceSettings(page, target.serviceName);
    const mapServer = page.locator('[data-protocol="MapServer"]');

    // Ensure MapServer is on first and serving.
    if (!(await mapServer.isChecked())) {
      await mapServer.check();
      await page.locator('[data-save-protocols]').click();
      await expect(page.locator('[data-config-result]')).toContainText('Updated', { timeout: 30_000 });
    }
    await expect.poll(mapServerError, { timeout: 20_000, intervals: [500, 1000, 2000] }).toBeNull();

    // Turn MapServer OFF through the UI and save.
    await mapServer.uncheck();
    await page.locator('[data-save-protocols]').click();
    await expect(page.locator('[data-config-result]')).toContainText('Updated', { timeout: 30_000 });

    // The per-service MapServer endpoint now answers not found, proving the toggle took effect.
    await expect.poll(mapServerError, { timeout: 20_000, intervals: [500, 1000, 2000] }).toBe(GEOSERVICES_NOT_FOUND);

    // Restore MapServer so the service ends the test as it started.
    await mapServer.check();
    await page.locator('[data-save-protocols]').click();
    await expect(page.locator('[data-config-result]')).toContainText('Updated', { timeout: 30_000 });
  });

  test('access policy: the anonymous-read toggle gates the live FeatureServer query', async ({ page, admin }) => {
    test.slow();
    const base = admin.serverUrl;
    const query = `/rest/services/${target.serviceName}/FeatureServer/${target.layerId}/query?where=1%3D1&outFields=*&f=json`;
    // An UNauthenticated caller (no X-API-Key) — its access is governed purely by the service access policy.
    const anon = () => anonymousGeoServicesQuery(page.request, `${base}${query}`);

    await openServiceSettings(page, target.serviceName);
    const allowAnon = page.locator('[data-allow-anonymous]');

    // 1) Make the service PRIVATE (anonymous read off) and save → the anonymous query is refused.
    if (await allowAnon.isChecked()) await allowAnon.uncheck();
    await page.locator('[data-save-access]').click();
    await expect(page.locator('[data-config-result]')).toContainText('Updated', { timeout: 30_000 });
    await expect
      .poll(anon, { timeout: 20_000, intervals: [500, 1000, 2000] })
      .toEqual({ errorCode: GEOSERVICES_TOKEN_REQUIRED });
    // A keyed caller still succeeds (the policy gates anonymous, not admin).
    expect(await geoServicesErrorCode(page.request, `${base}${query}`, ADMIN_HEADERS)).toBeNull();

    // 2) Make the service PUBLIC (anonymous read on) and save → the anonymous query now succeeds.
    await allowAnon.check();
    await page.locator('[data-save-access]').click();
    await expect(page.locator('[data-config-result]')).toContainText('Updated', { timeout: 30_000 });
    await expect
      .poll(anon, { timeout: 20_000, intervals: [500, 1000, 2000] })
      .toEqual({ features: expect.any(Number) });
  });
});
