import { randomUUID } from 'node:crypto';
import { test, expect } from '../admin-api';
import { ensurePublishedLayer } from '../seeded-layer';
import { SOURCE_DB } from '../source-db';

// Live e2e for the service CREATION paths — "can I create each service type, and do its layers function and
// appear in the catalog?". The Honua platform creates served layers three ways: (1) publish a table from a
// data CONNECTION, (2) IMPORT a file, (3) IMPORT an external service. This spec validates every path that has
// a reachable source on the testbed end-to-end (UI create -> catalog -> live query), and DOCUMENTS the paths
// that cannot be exercised here as explicit, reasoned skips so the coverage matrix is self-describing.
//
// Verification is always independent of the create path: the GeoServices /rest/services catalog must list the
// service, and its FeatureServer /query must return features — the same three-way discipline as
// services-layers.live.spec.ts (catalog + metadata + live query).

const ADMIN_KEY = process.env.HONUA_CONSOLE_E2E_ADMIN_KEY ?? 'honua-console-dev-key';
const ADMIN_HEADERS = { 'X-API-Key': ADMIN_KEY };
const SAMPLE_FEATURE_SERVER = 'https://sampleserver6.arcgisonline.com/arcgis/rest/services/Wildfire/FeatureServer';

// --- Path 1: data CONNECTION -> publish (PostGIS is the only provider with a reachable testbed source). ---
test.describe('Service types · connection publish (live)', () => {
  // services-layers.live.spec.ts drives this path through the UI; here the property is asserted directly on
  // a layer this spec seeds from its own table: the service is in the catalog and its layer answers a query.
  test('a published PostGIS layer is listed in the catalog and answers a live query', async ({ page, admin }) => {
    test.setTimeout(180_000);
    const base = admin.serverUrl;
    const seeded = await ensurePublishedLayer(admin, {
      connectionName: 'e2e-types-src',
      table: SOURCE_DB.typesTable,
      layerName: 'E2E Types',
      serviceName: `e2e_types_${randomUUID().replaceAll('-', '').slice(0, 10)}`,
    });
    const service = seeded.serviceName;

    const catalog = await (await page.request.get(`${base}/rest/services?f=json`, { headers: ADMIN_HEADERS })).json();
    expect((catalog.services ?? []).some((s: any) => s.name === service), `catalog lists ${service}`).toBeTruthy();

    const fsMeta = await (await page.request.get(`${base}/rest/services/${service}/FeatureServer?f=json`, { headers: ADMIN_HEADERS })).json();
    expect((fsMeta.layers ?? []).map((l: any) => l.id), `${service} exposes the seeded layer`).toContain(seeded.layerId);
    const query = await (await page.request.get(`${base}/rest/services/${service}/FeatureServer/${seeded.layerId}/query?where=1%3D1&outFields=*&f=json`, { headers: ADMIN_HEADERS })).json();
    expect(query.features?.length, 'live query returns the seeded features').toBe(3);
  });

  // The remaining connection providers have no reachable source on this testbed — documented so the matrix is
  // explicit. (Provider list: postgis ✓, mysql, sqlserver, oracle, duckdb, arcgis-rest.)
  for (const provider of ['mysql', 'sqlserver', 'oracle', 'duckdb']) {
    test.skip(`create a ${provider} connection + publish — no ${provider} source on this testbed`, () => {});
  }
});

// --- Path 2: external SERVICE import -> served layer. ---
test.describe('Service types · external service import (live)', () => {
  // Importing an external ArcGIS FeatureServer to a managed PostGIS table runs to completion and is covered by
  // service-import.live.spec.ts (auto-publish OFF). Driving the same import with AUTO-PUBLISH ON — which would
  // create a served, catalogued layer — does NOT reliably complete on this testbed: the publish step after the
  // table import did not reach "Completed" within 180s across retries (~9 min). This is a real gap (auto-publish
  // from an external import is slow/unreliable here); when it is fixed, the body below verifies catalog + live
  // query and should be re-enabled. Until then it is a documented skip rather than a flaky 10-minute test.
  test.fixme('import an ArcGIS FeatureServer with auto-publish, then verify the layer in the catalog and live', async ({ page, admin }) => {
    test.setTimeout(240_000);
    const base = admin.serverUrl;

    await page.goto('/operate/import/service');
    await page.getByPlaceholder(/rest\/services/).fill(SAMPLE_FEATURE_SERVER);
    await page.getByRole('button', { name: 'Discover service' }).click();
    await expect(page.getByRole('heading', { name: 'Service discovered' })).toBeVisible({ timeout: 90_000 });

    const autoPublish = page.getByLabel('Auto-publish imported layers');
    if ((await autoPublish.count()) > 0 && !(await autoPublish.isChecked())) {
      await autoPublish.check();
    }
    await page.locator('[data-layer-checkbox]').first().check();
    await expect(page.locator('[data-selection-count]')).toContainText('1 of');
    await page.locator('[data-import-selected]').click();

    await expect(page.locator('[data-import-job]').first()).toBeVisible({ timeout: 15_000 });
    await expect(page.locator('[data-import-job][data-import-status="Completed"]')).toHaveCount(1, { timeout: 180_000 });

    let svc: string | null = null;
    await expect
      .poll(
        async () => {
          const catalog = await (await page.request.get(`${base}/rest/services?f=json`, { headers: ADMIN_HEADERS })).json();
          svc = (catalog.services ?? []).find((s: any) => /wildfire/i.test(s.name ?? ''))?.name ?? null;
          return svc;
        },
        { timeout: 60_000, intervals: [2000, 5000, 10000] },
      )
      .not.toBeNull();

    const meta = await (await page.request.get(`${base}/rest/services/${svc}/FeatureServer?f=json`, { headers: ADMIN_HEADERS })).json();
    expect(Array.isArray(meta.layers) && meta.layers.length > 0, `${svc} exposes layers`).toBeTruthy();
    const layerId = meta.layers[0].id ?? 0;
    const q = await (await page.request.get(`${base}/rest/services/${svc}/FeatureServer/${layerId}/query?where=1%3D1&resultRecordCount=1&f=json`, { headers: ADMIN_HEADERS })).json();
    expect(Array.isArray(q.features), `${svc} layer ${layerId} answers a query`).toBeTruthy();
  });

  // WFS and OGC-API-Features sources are discoverable by the importer but no such source is hosted on the
  // testbed, so end-to-end import cannot be exercised here.
  for (const kind of ['WFS', 'OGC API Features']) {
    test.skip(`import a ${kind} service — no ${kind} source reachable from this testbed`, () => {});
  }
});

// --- Path 3: FILE import. ---
test.describe('Service types · file import (live)', () => {
  // The console file-import flow (format gating, upload, progress) is covered by file-import.live.spec.ts. The
  // server-side streamed import of a supported file currently FAILS on this server (a known server bug — the
  // console surfaces "Import failed" cleanly), so a file-imported layer cannot yet be validated as functioning
  // or appearing in the catalog. Documented as a skip until the server import is fixed.
  test.skip('import a GeoJSON file and verify the layer functions — blocked by the server-side import bug', () => {});
  for (const fmt of ['Shapefile (.zip)', 'GeoPackage (.gpkg)', 'CSV', 'KML', 'GML']) {
    test.skip(`import a ${fmt} file — blocked by the server-side import bug (and no fixture yet)`, () => {});
  }
});
