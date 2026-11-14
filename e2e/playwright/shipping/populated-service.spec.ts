import { expect, test, type Page, type TestInfo } from '@playwright/test';
import { readFileSync } from 'node:fs';

test('populated service endpoints use the public HTTPS origin and serve live features', async ({ page }, info) => {
  const passwordFile = process.env.HONUA_SHIPPING_PASSWORD_FILE;
  if (!passwordFile) throw new Error('HONUA_SHIPPING_PASSWORD_FILE is required');
  const password = readFileSync(passwordFile, 'utf8').trim();
  if (!password) throw new Error('The quickstart password file is empty');
  const errors: string[] = [];
  page.on('pageerror', error => errors.push(error.message));
  await page.goto('/console/auth/signin');
  await page.locator('input[type="password"]').fill(password);
  await page.getByRole('button', { name: 'Continue', exact: true }).click();
  await expect(page).not.toHaveURL(/\/auth\/signin/);
  const services = await page.evaluate(async () => {
    const response = await fetch('/api/v1/admin/services');
    return { status: response.status, body: await response.json() };
  });
  expect(services.status).toBe(200);
  expect(JSON.stringify(services.body)).toContain('public-endpoint-check');
  await checkEndpoints(page, info);
  await expect(page.locator('#blazor-error-ui')).toBeHidden();
  expect(errors).toEqual([]);
});

async function checkEndpoints(page: Page, info: TestInfo) {
  await page.goto('/console/operate/services/public-endpoint-check/settings');
  await page.getByRole('tab', { name: 'Protocols', exact: true }).click();
  const urls = page.locator('.console-protocol-pill small');
  await expect(urls.first()).toBeVisible();
  const endpoints = await urls.allTextContents();
  expect(endpoints.length).toBeGreaterThan(0);
  for (const endpoint of endpoints.filter(value => value.startsWith('http'))) {
    expect(new URL(endpoint).origin).toBe('https://localhost:18083');
  }
  const featureUrl = endpoints.find(value => value.endsWith('/public-endpoint-check/FeatureServer'));
  expect(featureUrl).toBeTruthy();
  const query = await page.evaluate(async url => {
    const response = await fetch(url + '/946800/query?where=1%3D1&returnCountOnly=true&f=json');
    return { status: response.status, body: await response.json() };
  }, featureUrl!);
  expect(query.status).toBe(200);
  expect(query.body.count).toBe(1);
  await info.attach('populated-service', { body: await page.screenshot({ fullPage: true }), contentType: 'image/png' });
}
