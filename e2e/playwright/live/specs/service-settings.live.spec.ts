import { test, expect } from '../admin-api';

// Live e2e for the Operate service-settings surface (/operate/services/{svc}/settings): it validates that
// EVERY service setting the console exposes actually takes effect on the live server — driven through the
// real UI and verified through an independent path (the public protocol endpoints + the admin settings API),
// per the ServerStateVerifier discipline.
//
// Two settings groups are covered:
//   1. Protocol enablement — toggling a protocol on makes the layer serve through that protocol's endpoint,
//      and toggling it off makes that per-service endpoint stop serving (negative check).
//   2. Access policy (the access tier) — the anonymous-read toggle gates the live FeatureServer query for an
//      unauthenticated caller (200 when public, 401 when private).
//
// Target: the published service `e2e_src_fs` (layer 1), seeded by services-layers.live.spec.ts. If it is not
// present (the publish spec has not run on this server), the whole describe is skipped with a clear reason.

const SERVICE_NAME = 'e2e_src_fs';
const LAYER_ID = 1;
const ADMIN_KEY = process.env.HONUA_CONSOLE_E2E_ADMIN_KEY ?? 'honua-console-dev-key';
const ADMIN_HEADERS = { 'X-API-Key': ADMIN_KEY };

// Protocols a vector (polygon) layer can serve, with the concrete endpoint that proves it serves when the
// protocol is enabled. (Raster/elevation/GP protocols — ImageServer, Wcs, OgcApiCoverages, Terrain,
// Elevation, GPServer, Grpc — do not apply to a vector layer and are intentionally excluded.)
const PROTOCOL_SERVES: { protocol: string; path: string; marker: string | RegExp }[] = [
  { protocol: 'FeatureServer', path: `/rest/services/${SERVICE_NAME}/FeatureServer?f=json`, marker: '"layers"' },
  { protocol: 'MapServer', path: `/rest/services/${SERVICE_NAME}/MapServer?f=json`, marker: /"(name|mapName)"/ },
  { protocol: 'Wms', path: `/rest/services/${SERVICE_NAME}/MapServer/WMS?service=WMS&request=GetCapabilities&version=1.3.0`, marker: 'WMS_Capabilities' },
  { protocol: 'Wmts', path: `/rest/services/${SERVICE_NAME}/MapServer/WMTS?service=WMTS&request=GetCapabilities&version=1.0.0`, marker: 'Capabilities' },
  { protocol: 'Wfs20', path: `/wfs?service=WFS&request=GetCapabilities&version=2.0.0`, marker: 'WFS_Capabilities' },
  { protocol: 'OgcFeatures', path: `/ogc/features/collections?f=json`, marker: '"collections"' },
  { protocol: 'OgcApiTiles', path: `/ogc/tiles/tiles?f=json`, marker: /"(tilesets|tileMatrixSets)"/ },
  { protocol: 'OData', path: `/odata/$metadata`, marker: 'Edmx' },
  { protocol: 'Stac', path: `/stac?f=json`, marker: '"links"' },
];

test.describe('Operate · Service settings (live)', () => {
  test.beforeEach(async ({ page, admin }) => {
    // Guard: the settings target must be a published service on this server. NOTE the absolute server URL —
    // a relative path would resolve against the Console baseURL, not honua-server.
    const res = await page.request.get(`${admin.serverUrl}/rest/services/${SERVICE_NAME}/FeatureServer?f=json`, { headers: ADMIN_HEADERS });
    test.skip(!res.ok(), `${SERVICE_NAME} is not published on this server — run services-layers.live.spec first.`);
  });

  test('every applicable protocol enabled via the UI serves through its own endpoint', async ({ page, admin }) => {
    test.slow();
    const base = admin.serverUrl;

    // Drive the real service-config UI: tick every applicable protocol, then save.
    await page.goto(`/operate/services/${SERVICE_NAME}/settings`);
    await expect(page.locator('[data-service-config]')).toBeVisible({ timeout: 30_000 });

    for (const { protocol } of PROTOCOL_SERVES) {
      const box = page.locator(`[data-protocol="${protocol}"]`);
      // Only protocols the server reports available for this service render a checkbox.
      if ((await box.count()) === 0) continue;
      if (!(await box.isChecked())) await box.check();
    }
    await page.locator('[data-save-protocols]').click();
    await expect(page.locator('[data-config-result]')).toContainText('Updated', { timeout: 30_000 });

    // The server settings API now reports them enabled (independent path from the UI write).
    const settings = await admin.getJson(`/api/v1/admin/services/${SERVICE_NAME}/settings`);
    const enabled: string[] = settings.enabledProtocols ?? settings.data?.enabledProtocols ?? [];

    // Each enabled protocol actually serves the layer through its public endpoint.
    for (const { protocol, path, marker } of PROTOCOL_SERVES) {
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
    const mapServerPath = `/rest/services/${SERVICE_NAME}/MapServer?f=json`;

    await page.goto(`/operate/services/${SERVICE_NAME}/settings`);
    await expect(page.locator('[data-service-config]')).toBeVisible({ timeout: 30_000 });
    const mapServer = page.locator('[data-protocol="MapServer"]');
    test.skip((await mapServer.count()) === 0, 'MapServer protocol not offered for this service.');

    // Ensure MapServer is on first and serving.
    if (!(await mapServer.isChecked())) {
      await mapServer.check();
      await page.locator('[data-save-protocols]').click();
      await expect(page.locator('[data-config-result]')).toContainText('Updated', { timeout: 30_000 });
    }
    await expect
      .poll(async () => (await page.request.get(`${base}${mapServerPath}`, { headers: ADMIN_HEADERS })).status(), { timeout: 20_000, intervals: [500, 1000, 2000] })
      .toBe(200);

    // Turn MapServer OFF through the UI and save.
    await mapServer.uncheck();
    await page.locator('[data-save-protocols]').click();
    await expect(page.locator('[data-config-result]')).toContainText('Updated', { timeout: 30_000 });

    // The per-service MapServer endpoint now stops serving (404/disabled), proving the toggle took effect.
    await expect
      .poll(async () => (await page.request.get(`${base}${mapServerPath}`, { headers: ADMIN_HEADERS })).status(), { timeout: 20_000, intervals: [500, 1000, 2000] })
      .not.toBe(200);

    // Restore: turn MapServer back on so the rest of the suite (and the testbed) sees it as before.
    await mapServer.check();
    await page.locator('[data-save-protocols]').click();
    await expect(page.locator('[data-config-result]')).toContainText('Updated', { timeout: 30_000 });
  });

  test('access policy: the anonymous-read toggle gates the live FeatureServer query', async ({ page, admin }) => {
    test.slow();
    const base = admin.serverUrl;
    const query = `/rest/services/${SERVICE_NAME}/FeatureServer/${LAYER_ID}/query?where=1%3D1&outFields=*&f=json`;
    // An UNauthenticated caller (no X-API-Key) — its access is governed purely by the service access policy.
    const anon = async () => (await page.request.get(`${base}${query}`)).status();

    await page.goto(`/operate/services/${SERVICE_NAME}/settings`);
    await expect(page.locator('[data-service-config]')).toBeVisible({ timeout: 30_000 });
    const allowAnon = page.locator('[data-allow-anonymous]');

    // 1) Make the service PRIVATE (anonymous read off) and save → the anonymous query is refused (401/403).
    if (await allowAnon.isChecked()) await allowAnon.uncheck();
    await page.locator('[data-save-access]').click();
    await expect(page.locator('[data-config-result]')).toContainText('Updated', { timeout: 30_000 });
    await expect
      .poll(anon, { timeout: 20_000, intervals: [500, 1000, 2000] })
      .toBeGreaterThanOrEqual(401);
    // A keyed caller still succeeds (the policy gates anonymous, not admin).
    expect((await page.request.get(`${base}${query}`, { headers: ADMIN_HEADERS })).status()).toBe(200);

    // 2) Make the service PUBLIC (anonymous read on) and save → the anonymous query now succeeds.
    await allowAnon.check();
    await page.locator('[data-save-access]').click();
    await expect(page.locator('[data-config-result]')).toContainText('Updated', { timeout: 30_000 });
    await expect
      .poll(anon, { timeout: 20_000, intervals: [500, 1000, 2000] })
      .toBe(200);
    // Leave the service public (its state on entry), so the rest of the suite sees it unchanged.
  });
});
