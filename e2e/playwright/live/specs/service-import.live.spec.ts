import { test, expect } from '../admin-api';

// Live e2e for the Operate "Import from a service" flow. Discovers a remote Esri/OGC service — or an ArcGIS
// catalog root — via honua-server (POST /api/v1/admin/external-services/discover) and drives the catalog tree
// picker. Verifies:
//   - the URL is validated as https before any request,
//   - selecting an auth mode requires its credentials before discovering,
//   - discovering a single FeatureServer lists its layers in the tree and select-all/deselect-all works,
//   - discovering a catalog root enumerates many services into the tree.
//
// The discovery target is the public ArcGIS sample server (stable, used widely for samples). The server must
// have outbound HTTPS; the catalog test is generous on timeout for the multi-service scan.

const SAMPLE_FEATURE_SERVER = 'https://sampleserver6.arcgisonline.com/arcgis/rest/services/Wildfire/FeatureServer';
const SAMPLE_CATALOG_ROOT = 'https://sampleserver6.arcgisonline.com/arcgis/rest/services';

test.describe('Operate · Import from a service (live)', () => {
  test('requires an https URL before discovering', async ({ page }) => {
    await page.goto('/operate/import/service');
    await page.getByPlaceholder(/rest\/services/).fill('http://insecure.example.com/FeatureServer');
    await page.getByRole('button', { name: 'Discover service' }).click();

    await expect(page.locator('[data-discover-validation]')).toBeVisible();
    await expect(page.locator('[data-discover-validation]')).toContainText(/must be an absolute https/i);
  });

  test('requires credentials when an auth mode is selected', async ({ page }) => {
    await page.goto('/operate/import/service');
    await page.getByPlaceholder(/rest\/services/).fill(SAMPLE_FEATURE_SERVER);
    await page.getByLabel('Authentication mode').selectOption('token');
    // Leave the token blank, then attempt discovery.
    await page.getByRole('button', { name: 'Discover service' }).click();

    await expect(page.locator('[data-discover-validation]')).toBeVisible();
    await expect(page.getByText(/enter a token/i)).toBeVisible();
  });

  test('discovers a single FeatureServer and supports select-all / deselect-all', async ({ page }) => {
    test.slow();
    await page.goto('/operate/import/service');
    await page.getByPlaceholder(/rest\/services/).fill(SAMPLE_FEATURE_SERVER);
    await page.getByRole('button', { name: 'Discover service' }).click();

    // A progress indicator is shown while honua-server scans the remote service.
    await expect(page.locator('[data-discover-progress]')).toBeVisible({ timeout: 10_000 });

    await expect(page.getByRole('heading', { name: 'Service discovered' })).toBeVisible({ timeout: 60_000 });
    await expect(page.locator('[data-discovered-type]')).toContainText('FeatureServer');

    // The tree shows the service and (auto-expanded for a small result) its layers.
    await expect(page.locator('[data-service-row]')).toHaveCount(1);
    const layers = page.locator('[data-layer-checkbox]');
    const layerCount = await layers.count();
    expect(layerCount).toBeGreaterThan(0);

    // Select all selects every layer; deselect all clears them.
    await page.locator('[data-select-all]').click();
    await expect(page.locator('[data-selection-count]')).toHaveText(
      new RegExp(`^${layerCount} of ${layerCount} layers selected$`),
    );

    await page.locator('[data-deselect-all]').click();
    await expect(page.locator('[data-selection-count]')).toHaveText(
      new RegExp(`^0 of ${layerCount} layers selected$`),
    );
  });

  test('imports a selected layer and the job runs to completion', async ({ page }) => {
    test.setTimeout(180_000);
    await page.goto('/operate/import/service');
    await page.getByPlaceholder(/rest\/services/).fill(SAMPLE_FEATURE_SERVER);
    await page.getByRole('button', { name: 'Discover service' }).click();

    await expect(page.getByRole('heading', { name: 'Service discovered' })).toBeVisible({ timeout: 60_000 });

    // Import to a PostGIS table only (auto-publish off) so the assertion isn't coupled to publish-side state.
    await page.getByLabel('Auto-publish imported layers').uncheck();
    await page.locator('[data-layer-checkbox]').first().check();
    await expect(page.locator('[data-selection-count]')).toContainText('1 of');

    await page.locator('[data-import-selected]').click();

    // A job row appears and the server-side import runs to completion.
    const jobRow = page.locator('[data-import-job]').first();
    await expect(jobRow).toBeVisible({ timeout: 10_000 });
    await expect(page.locator('[data-import-job][data-import-status="Completed"]')).toHaveCount(1, { timeout: 120_000 });
    await expect(jobRow).toContainText(/imp_wildfire/i);
  });

  test('discovers an ArcGIS catalog root and lists many services in the tree', async ({ page }) => {
    test.setTimeout(300_000);
    await page.goto('/operate/import/service');
    await page.getByPlaceholder(/rest\/services/).fill(SAMPLE_CATALOG_ROOT);
    await page.getByRole('button', { name: 'Discover service' }).click();

    await expect(page.locator('[data-discover-progress]')).toBeVisible({ timeout: 10_000 });

    // The catalog scan enumerates every service; allow plenty of time.
    await expect(page.getByRole('heading', { name: 'Service discovered' })).toBeVisible({ timeout: 180_000 });
    await expect(page.locator('[data-discovered-type]')).toContainText('Catalog');

    // Many services are listed as tree rows.
    const serviceRows = page.locator('[data-service-row]');
    expect(await serviceRows.count()).toBeGreaterThan(5);

    // Select all selects every layer across all services; the counter shows selected === total (non-zero).
    await page.locator('[data-select-all]').click();
    const countText = (await page.locator('[data-selection-count]').textContent())?.trim() ?? '';
    const match = countText.match(/^(\d+) of (\d+) layers selected$/);
    expect(match).not.toBeNull();
    expect(Number(match![1])).toBeGreaterThan(0);
    expect(match![1]).toBe(match![2]);

    await page.locator('[data-deselect-all]').click();
    await expect(page.locator('[data-selection-count]')).toHaveText(/^0 of \d+ layers selected$/);
  });
});
