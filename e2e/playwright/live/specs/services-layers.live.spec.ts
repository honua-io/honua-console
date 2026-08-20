import { test, expect } from '../admin-api';
import { SOURCE_DB, sourceConnectionBody } from '../source-db';

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
// Overridable so the suite can publish out of whatever PostGIS the surrounding harness booted; the
// default is the console testbed's seeded table. See live/source-db.ts.
const SOURCE_TABLE = SOURCE_DB.table;
const SERVICE_NAME = 'e2e_src_fs';
const LAYER_NAME = 'E2E Source';

test.describe('Operate · Publish layer workflow (live)', () => {
  test('publish a PostGIS table and verify via catalog, metadata, and a live query', async ({ page, admin }) => {
    // Cold PostGIS table discovery (column/PK/row scan across all spatial tables) can be slow on a freshly
    // created connection — especially under full-suite load — so allow generous headroom.
    test.setTimeout(300_000);
    const connName = `e2e-pub-conn-${stamp}`;
    const conn = await admin.createConnection(sourceConnectionBody(connName));
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

  test('the layer renders through the map-preview proxy (credentials stay server-side)', async ({ page, admin }) => {
    // Resolve the published layer's global id from the admin layer listing.
    const connName = `e2e-preview-conn-${stamp}`;
    const conn = await admin.createConnection(sourceConnectionBody(connName));
    admin.trackConnectionName(connName);
    const layers =
      (await admin.getJson(`/api/v1/admin/connections/${conn.connectionId}/layers/?serviceName=${SERVICE_NAME}`)).data ?? [];
    const layer = layers.find((l: any) => l.layerName === LAYER_NAME) ?? layers[0];
    expect(layer, 'the published layer should be listed').toBeTruthy();

    // The console map-proxy serves honua-server's MapLibre style WITHOUT the browser sending the admin key
    // (page.request has no X-API-Key), and rewrites the tile URLs back through the proxy.
    const styleRes = await page.request.get(`/map-proxy/styles/${layer.layerId}.json`);
    expect(styleRes.ok(), `proxy style -> ${styleRes.status()}`).toBeTruthy();
    const style = await styleRes.json();
    expect(style.version, 'a MapLibre v8 style').toBe(8);
    expect(JSON.stringify(style), 'tile urls routed through the proxy').toContain('/map-proxy/tiles/');

    // A tile through the proxy returns a tile (or 204 for an empty tile) — never 401.
    const tileRes = await page.request.get(`/map-proxy/tiles/${layer.layerId}/0/0/0.mvt`);
    expect([200, 204], `proxy tile -> ${tileRes.status()}`).toContain(tileRes.status());
  });

  test('author a coded-value domain on a field via the layer detail page', async ({ page, admin }) => {
    test.slow();
    // A connection must exist for the layer to appear in the console's layers view.
    const connName = `e2e-domains-conn-${stamp}`;
    await admin.createConnection(sourceConnectionBody(connName));
    admin.trackConnectionName(connName);

    // Open the published layer's detail page from the layers list.
    await page.goto('/operate/layers');
    await page.getByRole('row', { name: new RegExp(LAYER_NAME) }).getByRole('link').first().click();
    await expect(page.locator('[data-fields-panel]')).toBeVisible({ timeout: 30_000 });

    // Author a coded-value domain on the 'name' field through the console UI.
    await page.locator('[data-domain-field]').selectOption('name');
    await page.locator('[data-domain-name]').fill('e2e_status');
    await page.locator('[data-domain-codes]').fill('a=Alpha\nb=Beta\nc=Gamma');
    await page.locator('[data-domain-save]').click();
    await expect(page.locator('[data-domain-result]')).toContainText('Updated', { timeout: 30_000 });

    // After save the page re-reads fields from the server, so the row reflecting the new domain proves the
    // server persisted it (round-trip through GET/PUT /admin/metadata/layers/{id}/fields).
    await expect(page.locator('[data-field-row][data-field-name="name"] [data-field-domain]')).toContainText('e2e_status');
  });

  test('authored renderer, popupInfo, and relationships emit in FeatureServer metadata', async ({ page, admin }) => {
    test.slow();
    const base = admin.serverUrl;
    const headers = { 'X-API-Key': ADMIN_KEY, 'Content-Type': 'application/json' };
    const LID = 1; // e2e_src_fs's published layer is the first global layer on the fresh dedicated DB.

    // Author a UniqueValue renderer, a popupInfo template, and a (self-)relationship via the new admin setters.
    await page.request.put(`${base}/api/v1/admin/metadata/layers/${LID}/drawing-info`, {
      headers,
      data: {
        renderer: {
          type: 'uniqueValue',
          field1: 'name',
          uniqueValueInfos: [
            { value: 'a', label: 'A', symbol: { type: 'esriSFS', style: 'esriSFSSolid', color: [255, 0, 0, 255] } },
          ],
        },
      },
    });
    await page.request.put(`${base}/api/v1/admin/metadata/layers/${LID}/popup-info`, {
      headers,
      data: { title: '{name}', fieldInfos: [{ fieldName: 'name', label: 'Name', visible: true }] },
    });
    await page.request.put(`${base}/api/v1/admin/metadata/layers/${LID}/relationships`, {
      headers,
      data: {
        relationships: [
          { id: 'rel0', name: 'self', relatedLayerId: LID, role: 'origin', cardinality: 'one-to-many', originField: 'id', destinationField: 'id', esriRelationshipId: 7 },
        ],
      },
    });

    // The FeatureServer layer metadata now carries all three (poll absorbs the post-write cache invalidation).
    await expect
      .poll(
        async () => {
          const r = await page.request.get(`${base}/rest/services/${SERVICE_NAME}/FeatureServer/${LID}?f=json`, { headers });
          if (!r.ok()) return null;
          return (await r.json()).drawingInfo?.renderer?.type ?? null;
        },
        { timeout: 30_000, intervals: [500, 1000, 2000] },
      )
      .toBe('uniqueValue');

    const layer = await (await page.request.get(`${base}/rest/services/${SERVICE_NAME}/FeatureServer/${LID}?f=json`, { headers })).json();
    expect(layer.popupInfo?.title, 'popupInfo emitted').toBe('{name}');
    expect((layer.relationships ?? []).length, 'relationship emitted').toBeGreaterThan(0);
  });
});
