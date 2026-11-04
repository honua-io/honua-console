import { randomUUID } from 'node:crypto';
import { test, expect } from '../admin-api';
import { GEOSERVICES_TOKEN_REQUIRED, anonymousGeoServicesQuery } from '../geoservices';
import { ensurePublishedLayer } from '../seeded-layer';
import { SOURCE_DB } from '../source-db';

// The core customer journey: an existing published layer goes live through /operate/data/new.
// The spec seeds the layer through the admin API and closes it to anonymous reads, drives the flow's
// Go live step in the browser with Public visibility, then proves the data is served by querying
// honua-server WITHOUT credentials and reading back the service's protocols.

const stamp = randomUUID().replaceAll('-', '').slice(0, 10);
const CHOSEN_PROTOCOLS = ['FeatureServer', 'OgcFeatures'];
const SEEDED_FEATURES = 3;

test('an existing layer goes live through Apply & publish and is queryable anonymously', async ({ page, admin }) => {
  test.setTimeout(300_000);
  const seeded = await ensurePublishedLayer(admin, {
    connectionName: 'e2e-golive-src',
    table: SOURCE_DB.goLiveTable,
    layerName: `E2E Go Live ${stamp}`,
    serviceName: `e2e_golive_${stamp}`,
  });
  const featureServer = `/rest/services/${seeded.serviceName}/FeatureServer`;
  const query = `${featureServer}/${seeded.layerId}/query?where=1%3D1&outFields=*&f=json`;
  const anonymousQuery = () => anonymousGeoServicesQuery(page.request, `${admin.serverUrl}${query}`);
  await admin.putJson(`/api/v1/admin/services/${seeded.serviceName}/access-policy`, { allowAnonymous: false });
  await expect.poll(anonymousQuery, { timeout: 20_000 }).toEqual({ errorCode: GEOSERVICES_TOKEN_REQUIRED });

  await page.goto('/operate/data/new?source=existingresource');
  const resource = page.getByLabel('Existing resource', { exact: true });
  const continueButton = page.locator('[data-step-body="AddData"] button.console-button');
  await expect(async () => {
    await resource.selectOption({ label: seeded.layerName });
    await expect(continueButton).toBeEnabled({ timeout: 2_000 });
  }).toPass({ timeout: 60_000 });
  await continueButton.click();

  await expect(page.locator('[data-service-slot]')).toHaveText(seeded.serviceName);
  for (const box of await page.locator('[data-protocol]').all()) {
    const chosen = CHOSEN_PROTOCOLS.includes((await box.getAttribute('data-protocol')) ?? '');
    await box.setChecked(chosen);
    await expect(box).toBeChecked({ checked: chosen });
  }
  await page.getByRole('button', { name: 'Continue to style →' }).click();
  await page.getByRole('button', { name: 'Continue to go live →' }).click();

  await page.locator('input[name="visibility"][value="Public"]').check();
  await page.locator('[data-go-live-apply]').click();
  await expect(page.locator('[data-go-live-done]')).toBeVisible({ timeout: 60_000 });
  for (const protocol of CHOSEN_PROTOCOLS) {
    await expect(page.locator(`[data-protocol-result="${protocol}"]`)).toHaveAttribute('data-state', 'succeeded');
  }
  await expect(page.locator('[data-protocol-result="FeatureServer"] [data-live-route]')).toHaveText(featureServer);
  await expect(page.locator('[data-visibility-result]')).toHaveAttribute('data-state', 'succeeded');

  const settings = await admin.getJson(`/api/v1/admin/services/${seeded.serviceName}/settings`);
  const enabled: string[] = settings.data?.enabledProtocols ?? settings.enabledProtocols ?? [];
  expect([...enabled].sort()).toEqual([...CHOSEN_PROTOCOLS].sort());

  await expect
    .poll(anonymousQuery, { message: 'the live layer serves the seeded features anonymously', timeout: 20_000 })
    .toEqual({ features: SEEDED_FEATURES });
});
