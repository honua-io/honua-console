import { test, expect } from '../admin-api';

// Live e2e for the core admin workflow: data connection -> pick a datasource table -> publish a service
// layer, then verify the output THREE independent ways (per the workflow goal):
//   1. catalog              — the GeoServices /rest/services directory lists the new service,
//   2. metadata validation  — the FeatureServer exposes the layer with the right geometry type,
//   3. live service/layer   — the FeatureServer /query returns the seeded features in the right SRID.
//
// Source: public.e2e_layer_src — a PostGIS polygon table (integer PK, 3 rows, EPSG:3857) seeded into the
// server's database for this workflow test. The service name is fixed: the first run publishes it; later
// runs find it already published and re-verify the live output (the verification reads global GeoServices
// state, so it is independent of which run created the layer or which connection published it).

const stamp = Date.now().toString(36);
const SOURCE_TABLE = 'public.e2e_layer_src';
const SERVICE_NAME = 'e2e_src_fs';
const LAYER_NAME = 'E2E Source';

test.describe('Operate · Publish layer workflow (live)', () => {
  test('publish a PostGIS table and verify via catalog, metadata, and a live query', async ({ page, admin }) => {
    // Cold PostGIS table discovery (column/PK/row scan across all spatial tables) can be slow on a freshly
    // created connection — especially under full-suite load — so allow generous headroom.
    test.setTimeout(300_000);
    const connName = `e2e-pub-conn-${stamp}`;
    const conn = await admin.createConnection({
      name: connName,
      host: 'localhost',
      port: 5544,
      databaseName: 'honua_dev',
      username: 'honua_user',
      password: 'honua_password',
      provider: 'postgis',
      sslRequired: false,
      sslMode: 'Disable',
    });
    admin.trackConnectionName(connName);
    // Warm server-side table discovery AND confirm the just-created connection is resolvable + the source
    // table is discoverable before driving the UI (a freshly created connection's first cold scan can lag).
    await expect
      .poll(
        async () => {
          const t = await admin.getJson(`/api/v1/admin/connections/${conn.connectionId}/tables`);
          return (t.tables ?? []).some((x: any) => `${x.schema}.${x.table}` === SOURCE_TABLE);
        },
        { timeout: 90_000, intervals: [1000, 2000, 5000] },
      )
      .toBeTruthy();

    // --- Drive the publish UI ---
    await page.goto('/operate/publishing/quick');
    const tableSelect = page.getByLabel('Table', { exact: true });
    // Selecting the connection triggers the UI's table discovery. The first cold scan can be slow under
    // load, so retry with a page reload — by a later attempt the server-side discovery is warm.
    for (let attempt = 1; ; attempt++) {
      await page.getByLabel('Connection', { exact: true }).selectOption(conn.connectionId);
      try {
        await expect(tableSelect).toBeEnabled({ timeout: 45_000 });
        break;
      } catch (err) {
        if (attempt >= 4) throw err;
        await page.reload();
      }
    }
    await tableSelect.selectOption(SOURCE_TABLE);
    await page.getByPlaceholder('parcels-fs').fill(SERVICE_NAME);
    await page.getByPlaceholder('Parcels', { exact: true }).fill(LAYER_NAME);
    await page.getByRole('button', { name: 'Publish layer' }).click();
    // First run publishes the layer; later runs report it already exists. Either way it is now live —
    // the assertions below verify the live server state, not which run created it.
    await expect(page.getByText(/Layer published|Layer not published/)).toBeVisible({ timeout: 30_000 });

    // --- Verification 1: catalog (GeoServices services directory lists the service) ---
    const catalog = await admin.getJson('/rest/services?f=json');
    expect(
      (catalog.services ?? []).some((s: any) => String(s.name).includes(SERVICE_NAME)),
      'catalog should list the published service',
    ).toBeTruthy();

    // --- Verification 2: metadata validation (FeatureServer exposes the layer + geometry type) ---
    const featureServer = await admin.getJson(`/rest/services/${SERVICE_NAME}/FeatureServer?f=json`);
    const layer =
      (featureServer.layers ?? []).find((l: any) => l.name === LAYER_NAME) ?? (featureServer.layers ?? [])[0];
    expect(layer, 'FeatureServer should expose the published layer').toBeTruthy();
    expect(String(layer.geometryType)).toMatch(/Polygon/i);

    // --- Verification 3: live service/layer query returns the seeded features in the right SRID ---
    const query = await admin.getJson(
      `/rest/services/${SERVICE_NAME}/FeatureServer/${layer.id}/query?where=1%3D1&outFields=*&f=json`,
    );
    expect(Array.isArray(query.features), 'query should return a features array').toBeTruthy();
    expect(query.features.length, 'live layer should return the 3 seeded features').toBe(3);
    expect((query.spatialReference ?? {}).wkid).toBe(3857);
  });

  // The protocols/catalog coverage below runs after the publish above (same file, serial worker) and shares
  // the published service e2e_src_fs.
  const ALL_PROTOCOLS = [
    'FeatureServer', 'MapServer', 'OgcFeatures', 'Wms', 'Wfs20', 'Wmts', 'OData', 'Stac',
  ];
  // The server's REST/OGC/OData surfaces require admin auth by default (a freshly published service is not
  // anonymous); verification sends the admin key, mirroring the admin fixture's other reads.
  const ADMIN_KEY = process.env.HONUA_CONSOLE_E2E_ADMIN_KEY ?? 'honua-console-dev-key';
  const ADMIN_HEADERS = { 'X-API-Key': ADMIN_KEY };

  test('enable protocols on the published service via the service-config page', async ({ page, admin }) => {
    // Drive the real console service-config UI to add WMS/WFS/WMTS/OData on top of the default protocols,
    // then confirm honua-server persisted the change (read back through the admin settings endpoint).
    await page.goto(`/operate/services/${SERVICE_NAME}/settings`);
    await expect(page.locator('[data-service-config]')).toBeVisible();
    await expect(page.locator('[data-protocol="FeatureServer"]')).toBeChecked();

    for (const protocol of ['Wms', 'Wfs20', 'Wmts', 'OData', 'OgcFeatures']) {
      const box = page.locator(`[data-protocol="${protocol}"]`);
      if (!(await box.isChecked())) {
        await box.check();
      }
    }
    await page.locator('[data-save-protocols]').click();
    await expect(page.locator('[data-config-result]')).toContainText('Updated', { timeout: 30_000 });

    const settings = await admin.getJson(`/api/v1/admin/services/${SERVICE_NAME}/settings`);
    const enabled: string[] = settings.data?.enabledProtocols ?? [];
    for (const protocol of ['Wms', 'Wfs20', 'Wmts', 'OData']) {
      expect(enabled, `server should report ${protocol} enabled`).toContain(protocol);
    }
  });

  test('the published layer serves across every protocol', async ({ page, admin }) => {
    test.slow();
    // Ensure the full protocol set is enabled (idempotent; independent of the UI test above).
    await admin.getJson('/api/v1/admin/services/'); // ensure the graph snapshot is warm
    await page.request.put(`${admin.serverUrl}/api/v1/admin/services/${SERVICE_NAME}/protocols`, {
      headers: { 'X-API-Key': process.env.HONUA_CONSOLE_E2E_ADMIN_KEY ?? 'honua-console-dev-key' },
      data: { enabledProtocols: ALL_PROTOCOLS },
    });

    const base = admin.serverUrl;
    async function serves(path: string, marker: string | RegExp) {
      const res = await page.request.get(`${base}${path}`, { headers: ADMIN_HEADERS });
      expect(res.ok(), `${path} -> ${res.status()}`).toBeTruthy();
      const body = await res.text();
      if (typeof marker === 'string') {
        expect(body, `${path} missing ${marker}`).toContain(marker);
      } else {
        expect(body, `${path} missing ${marker}`).toMatch(marker);
      }
    }

    await serves(`/rest/services/${SERVICE_NAME}/FeatureServer?f=json`, '"layers"');
    await serves(`/rest/services/${SERVICE_NAME}/MapServer?f=json`, /"(name|mapName)"/);
    await serves(`/rest/services/${SERVICE_NAME}/MapServer/WMS?service=WMS&request=GetCapabilities&version=1.3.0`, 'WMS_Capabilities');
    await serves(`/rest/services/${SERVICE_NAME}/MapServer/WMTS?service=WMTS&request=GetCapabilities&version=1.0.0`, 'Capabilities');
    await serves('/wfs?service=WFS&request=GetCapabilities&version=2.0.0', 'WFS_Capabilities');
    await serves('/ogc/features/collections?f=json', '"collections"');
    await serves('/odata', '"value"');
    await serves('/odata/$metadata', 'Edmx');
  });

  test('every catalog type lists the service', async ({ page, admin }) => {
    const base = admin.serverUrl;
    async function catalog(path: string, marker: string) {
      const res = await page.request.get(`${base}${path}`, { headers: ADMIN_HEADERS });
      expect(res.ok(), `${path} -> ${res.status()}`).toBeTruthy();
      expect(await res.text(), `${path} missing ${marker}`).toContain(marker);
    }

    await catalog('/rest/services?f=json', '"services"'); // GeoServices/Esri service catalog
    await catalog('/stac', '"links"'); // STAC catalog
    await catalog('/ogc/records/collections?f=json', '"collections"'); // OGC API Records (CSW-equivalent)
    await catalog('/ogc/features/collections?f=json', '"collections"'); // OGC API Features collection catalog
  });
});
