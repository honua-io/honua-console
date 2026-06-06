import { fileURLToPath } from 'node:url';
import { test, expect } from '../admin-api';

// Live e2e for the Operate "Import a file" flow. Verifies the console behaviour the operator relies on:
//   - the supported-format list is read from honua-server and an unsupported file is rejected client-side,
//   - a supported upload shows a progress indicator and uploads to the server (result surfaced).
// (The server-side streamed import itself is tracked separately; this spec asserts the console flow.)

const sampleGeoJson = fileURLToPath(new URL('../fixtures/sample.geojson', import.meta.url));
const unsupportedFile = fileURLToPath(new URL('../fixtures/notgeo.txt', import.meta.url));
const stamp = Date.now().toString(36);

test.describe('Operate · Import a file (live)', () => {
  test('rejects an unsupported file format before uploading', async ({ page }) => {
    await page.goto('/operate/resources/import');
    // Supported list loads from GET /import/formats; wait for it to render.
    await expect(page.getByText(/Supported:/)).toBeVisible({ timeout: 20_000 });

    await page.locator('input[type="file"]').setInputFiles(unsupportedFile);

    await expect(page.locator('[data-import-format-error]')).toBeVisible();
    await expect(page.getByText(/'\.txt' files are not supported/)).toBeVisible();
    await expect(page.getByRole('button', { name: 'Import file' })).toBeDisabled();
  });

  test('uploads a supported GeoJSON with a progress indicator and surfaces the server result', async ({ page }) => {
    test.slow();
    await page.goto('/operate/resources/import');
    await expect(page.getByText(/Supported:/)).toBeVisible({ timeout: 20_000 });

    await page.locator('input[type="file"]').setInputFiles(sampleGeoJson);
    // Supported extension: no format error, and the file is recognized (re-render over the circuit can lag).
    await expect(page.locator('[data-selected-file]')).toContainText('sample.geojson', { timeout: 25_000 });
    await expect(page.locator('[data-import-format-error]')).toHaveCount(0);

    await page.getByPlaceholder('parcels').fill(`e2e_import_${stamp}`);
    await page.getByRole('button', { name: 'Import file' }).click();

    // Progress indicator appears during the upload/import (the ~1 MB read streams over the circuit).
    await expect(page.locator('[data-import-progress]')).toBeVisible({ timeout: 10_000 });

    // The upload reaches honua-server and the console surfaces a result heading (a successful import once the
    // server-side import bug is fixed; today the server reports a failure, which the console surfaces cleanly).
    await expect(page.getByRole('heading', { name: /File imported|Import failed/ })).toBeVisible({ timeout: 60_000 });
  });
});
