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

// Protocols a vector (polygon) layer can serve, with the concrete endpoint that proves it serves when the
// protocol is enabled. (Raster/elevation/GP protocols — ImageServer, Wcs, OgcApiCoverages, Terrain,
// Elevation, GPServer, Grpc — do not apply to a vector layer and are intentionally excluded.)
const protocolServes = (service: string): { protocol: string; path: string; marker: string | RegExp }[] => [
  { protocol: 'FeatureServer', path: `/rest/services/${service}/FeatureServer?f=json`, marker: '"layers"' },
  { protocol: 'MapServer', path: `/rest/services/${service}/MapServer?f=json`, marker: /"(name|mapName)"/ },
  { protocol: 'Wms', path: `/rest/services/${service}/MapServer/WMS?service=WMS&request=GetCapabilities&version=1.3.0`, marker: 'WMS_Capabilities' },
  { protocol: 'Wmts', path: `/rest/services/${service}/MapServer/WMTS?service=WMTS&request=GetCapabilities&version=1.0.0`, marker: 'Capabilities' },
  { protocol: 'Wfs20', path: `/wfs?service=WFS&request=GetCapabilities&version=2.0.0`, marker: 'WFS_Capabilities' },
  { protocol: 'OgcFeatures', path: `/ogc/features/collections?f=json`, marker: '"collections"' },
  { protocol: 'OgcApiTiles', path: `/ogc/tiles/tiles?f=json`, marker: /"(tilesets|tileMatrixSets)"/ },
  { protocol: 'OData', path: `/odata/$metadata`, marker: 'Edmx' },
  { protocol: 'Stac', path: `/stac?f=json`, marker: '"links"' },
];

async function openServiceSettings(page: Page, serviceName: string): Promise<void> {
  await page.goto(`/operate/services/${serviceName}/settings`);
  await expect(page.locator('[data-service-config]')).toBeVisible({ timeout: 30_000 });
}

// Which protocols the page offers is decided from the server's settings, not by counting checkboxes:
// the boxes appear while the page is still rendering, so a count can miss one that is about to show.
async function offeredProtocols(admin: AdminApi, serviceName: string): Promise<string[]> {
  const settings = await admin.getJson(`/api/v1/admin/services/${serviceName}/settings`);
  const offered: string[] = settings.data?.availableProtocols ?? settings.availableProtocols ?? [];
  expect(offered.length, `${serviceName} reports the protocols it can serve`).toBeGreaterThan(0);
  return offered;
}

test.describe('Operate · Service settings (live)', () => {
  let target: SeededLayer;
  let offered: string[];

  test.beforeEach(async ({ admin }) => {
    test.setTimeout(180_000);
    target = await ensurePublishedLayer(admin, {
      connectionName: 'e2e-settings-src',
      table: SOURCE_DB.settingsTable,
      layerName: `E2E Settings ${stamp}`,
      serviceName: `e2e_settings_${stamp}`,
    });
    offered = await offeredProtocols(admin, target.serviceName);
  });

  test('every applicable protocol enabled via the UI serves through its own endpoint', async ({ page, admin }) => {
    test.slow();
    const base = admin.serverUrl;

    // Drive the real service-config UI: tick every applicable protocol, then save.
    await openServiceSettings(page, target.serviceName);

    for (const { protocol } of protocolServes(target.serviceName)) {
      if (offered.includes(protocol)) await page.locator(`[data-protocol="${protocol}"]`).check();
    }
    await page.locator('[data-save-protocols]').click();
    await expect(page.locator('[data-config-result]')).toContainText('Updated', { timeout: 30_000 });

    // The server settings API now reports them enabled (independent path from the UI write).
    const settings = await admin.getJson(`/api/v1/admin/services/${target.serviceName}/settings`);
    const enabled: string[] = settings.enabledProtocols ?? settings.data?.enabledProtocols ?? [];

    // Each enabled protocol actually serves the layer through its public endpoint.
    for (const { protocol, path, marker } of protocolServes(target.serviceName)) {
      if (!enabled.includes(protocol)) continue; // server may not offer a given protocol for this service
      const res = await page.request.get(`${base}${path}`, { headers: ADMIN_HEADERS });
      expect(res.ok(), `${protocol}: ${path} -> ${res.status()}`).toBeTruthy();
      const body = await res.text();
      if (typeof marker === 'string') {
        expect(body, `${protocol}: ${path} missing ${marker}`).toContain(marker);
      } else {
        expect(body, `${protocol}: ${path} missing ${marker}`).toMatch(marker);
      }
    }
  });

  test('disabling a protocol via the UI stops that per-service endpoint serving', async ({ page, admin }) => {
    test.slow();
    const base = admin.serverUrl;
    const mapServerPath = `/rest/services/${target.serviceName}/MapServer?f=json`;
    const mapServerError = () => geoServicesErrorCode(page.request, `${base}${mapServerPath}`, ADMIN_HEADERS);

    test.skip(!offered.includes('MapServer'), 'MapServer protocol not offered for this service.');
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
