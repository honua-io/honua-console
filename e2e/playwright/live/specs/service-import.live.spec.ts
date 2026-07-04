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

// The "Discover service" button and the service URL input both go over the Blazor SignalR circuit.
// On a freshly loaded page the circuit may not be established yet, meaning:
//   - The oninput event from fill() is lost (server _serviceUrl stays empty), AND
//   - The button @onclick fires but DiscoverAsync returns early with "Enter a service URL."
// Re-fill the URL on every retry so _serviceUrl is current when the click reaches the server.
async function fillAndDiscover(
  page: import('@playwright/test').Page,
  url: string,
): Promise<void> {
  await expect(async () => {
    await page.getByPlaceholder(/rest\/services/).fill(url);
    await page.getByRole('button', { name: 'Discover service' }).click();
    await expect(page.locator('[data-discover-progress]')).toBeVisible({ timeout: 3_000 });
  }).toPass({ timeout: 30_000 });
}

test.describe('Operate · Import from a service (live)', () => {
  test('requires an https URL before discovering', async ({ page }) => {
    await page.goto('/operate/import/service');
    // Re-fill and click on each retry until the circuit processes the click and
    // the validation error appears. Without this, a cold circuit drops the click.
    await expect(async () => {
      await page.getByPlaceholder(/rest\/services/).fill('http://insecure.example.com/FeatureServer');
      await page.getByRole('button', { name: 'Discover service' }).click();
      await expect(page.locator('[data-discover-validation]')).toBeVisible({ timeout: 3_000 });
    }).toPass({ timeout: 30_000 });
    await expect(page.locator('[data-discover-validation]')).toContainText(/must be an absolute https/i);
  });

  test('requires credentials when an auth mode is selected', async ({ page }) => {
    await page.goto('/operate/import/service');
    // Both the validation appearance AND its text depend on the Blazor circuit processing
    // the auth-mode selection. Include the text check inside the retry wrapper so that a
    // cold-circuit run that shows the wrong validation message retries the full interaction.
    await expect(async () => {
      await page.getByPlaceholder(/rest\/services/).fill(SAMPLE_FEATURE_SERVER);
      await page.getByLabel('Authentication mode').selectOption('token');
      // Leave the token blank, then attempt discovery.
      await page.getByRole('button', { name: 'Discover service' }).click();
      await expect(page.locator('[data-discover-validation]')).toBeVisible({ timeout: 3_000 });
      await expect(page.getByText(/enter a token/i)).toBeVisible({ timeout: 3_000 });
    }).toPass({ timeout: 30_000 });
  });

  test('discovers a single FeatureServer and supports select-all / deselect-all', async ({ page }) => {
    test.slow();
    await page.goto('/operate/import/service');
    await fillAndDiscover(page, SAMPLE_FEATURE_SERVER);

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
    // This test exercises the full import-to-completion path, which requires honua-server to
    // make an outbound HTTPS connection to the sample ArcGIS FeatureServer, download all
    // features, and write them to PostGIS — a multi-step job that can take 2+ minutes on a
    // cold server and depends on the external sampleserver6.arcgisonline.com being reachable
    // and responsive. The minimal compose stack does not guarantee that timing or network
    // access. Gate behind HONUA_E2E_IMPORT_SOURCE=true so the discovery/tree tests always
    // run but the long-running completion assertion only runs in environments prepared for it.
    test.skip(
      !process.env.HONUA_E2E_IMPORT_SOURCE,
      'Import-to-completion test requires HONUA_E2E_IMPORT_SOURCE=true (long-running job + external ArcGIS outbound access).',
    );
    test.setTimeout(180_000);
    await page.goto('/operate/import/service');
    await fillAndDiscover(page, SAMPLE_FEATURE_SERVER);

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
    await fillAndDiscover(page, SAMPLE_CATALOG_ROOT);

    // The catalog scan enumerates every service; allow plenty of time.
    await expect(page.getByRole('heading', { name: 'Service discovered' })).toBeVisible({ timeout: 180_000 });
    await expect(page.locator('[data-discovered-type]')).toContainText('Catalog');

    // Many services are listed as tree rows.
    const serviceRows = page.locator('[data-service-row]');
    expect(await serviceRows.count()).toBeGreaterThan(5);

    // Select all selects every layer across all services; the counter shows selected === total (non-zero).
    // Use click+toHaveText (not click+textContent) so Playwright waits for the Blazor circuit to
    // process the SelectAll event and re-render before reading the count.
    await page.locator('[data-select-all]').click();
    await expect(page.locator('[data-selection-count]')).toHaveText(/^\d+ of \d+ layers selected$/);
    const countText = (await page.locator('[data-selection-count]').textContent())?.trim() ?? '';
    const match = countText.match(/^(\d+) of (\d+) layers selected$/);
    expect(match).not.toBeNull();
    expect(Number(match![1])).toBeGreaterThan(0);
    expect(match![1]).toBe(match![2]);

    await page.locator('[data-deselect-all]').click();
    await expect(page.locator('[data-selection-count]')).toHaveText(/^0 of \d+ layers selected$/);
  });
});
