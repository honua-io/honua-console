import { expect, test, type Page, type TestInfo } from '@playwright/test';
import { fileURLToPath } from 'node:url';
import { startStaticBundleServer } from '../static-bundle-server.mjs';
import { applyTextMode, textModes } from './reflow-typography';

async function checkSupportReflow(page: Page, context: { v4Enabled: boolean; testInfo: TestInfo }) {
  for (const mode of textModes) {
    const enlarged = mode === 'enlarged' || mode === 'combined';
    const style = await applyTextMode(page, mode);
    await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(page.viewportSize()!.width);
    expect(await page.locator('.support-context-panel').evaluate(panel => {
      const bounds = panel.getBoundingClientRect();
      return [...panel.querySelectorAll('h2, p, dt, dd, li')].flatMap(element => {
        const rect = element.getBoundingClientRect();
        return rect.left >= bounds.left - 1 && rect.right <= bounds.right + 1 && element.scrollWidth <= element.clientWidth + 1
          ? [] : [{ text: element.textContent, left: rect.left, right: rect.right, bounds }];
      });
    }), mode).toEqual([]);
    if (enlarged) {
      const panels = await page.locator('.support-layout').evaluate(layout => {
        const form = layout.querySelector('article')!.getBoundingClientRect();
        const context = layout.querySelector('aside')!.getBoundingClientRect();
        return { formBottom: form.bottom, contextTop: context.top };
      });
      expect(panels.contextTop, 'enlarged text stacks readable Support panels').toBeGreaterThanOrEqual(panels.formBottom);
    }
    await expect(page.getByRole('button', { name: 'Submit ticket', exact: true })).toBeDisabled();
    await context.testInfo.attach(`support-${mode}-v4-${context.v4Enabled}`, {
      body: await page.screenshot({ fullPage: true }), contentType: 'image/png',
    });
    await style.evaluate(element => element.remove());
  }
}

async function supportFixture(options: { v4Enabled: boolean }) {
  let releaseCapture: () => void = () => {};
  const captureReleased = new Promise<void>(resolve => { releaseCapture = resolve; });
  let captures = 0;
  const server = await startStaticBundleServer(
    fileURLToPath(new URL('../../../artifacts/static-publish/wwwroot/console', import.meta.url)), 0, {
      configuration: { apiBaseUrl: '/', auth: { mode: 'builtin' }, v4Enabled: options.v4Enabled },
      handleApi: async (request, response) => {
        response.setHeader('Content-Type', 'application/json');
        const path = new URL(request.url!, 'http://localhost').pathname;
        if (path.endsWith('/auth/session')) {
          response.end(JSON.stringify({ isAuthenticated: true, claims: [
            { type: 'http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier', value: 'fixture-operator' },
            { type: 'http://schemas.microsoft.com/ws/2008/06/identity/claims/role', value: 'admin' },
          ] }));
        } else if (path.endsWith('/auth/csrf')) {
          response.end(JSON.stringify({ requestToken: 'fixture-only' }));
        } else if (path.endsWith('/observability/errors')) {
          if (++captures > 1) await captureReleased;
          response.end(JSON.stringify({ errors: [
            { statusCode: 500, path: '/ogc/services/geoprocessing/wms', timestamp: '2026-01-01T00:00:00Z' },
            { statusCode: 502, path: `/ogc/services/${'long-service-name-'.repeat(12)}/wms`, timestamp: '2026-01-01T00:00:00Z' },
          ] }));
        } else {
          response.writeHead(404).end('{}');
        }
      },
    });
  return { server, releaseCapture, captureCount: () => captures };
}

async function checkPendingSubmission(page: Page, fixture: Awaited<ReturnType<typeof supportFixture>>, context: { v4Enabled: boolean; testInfo: TestInfo }) {
  await page.getByLabel('Requested action', { exact: true }).fill('Diagnose synthetic errors');
  await page.getByLabel('Symptoms', { exact: true }).fill('Synthetic failure');
  await page.getByLabel('Symptoms', { exact: true }).blur();
  await expect(page.getByRole('button', { name: 'Submit ticket', exact: true })).toBeEnabled();
  await page.getByRole('button', { name: 'Submit ticket', exact: true }).click();
  await expect.poll(fixture.captureCount).toBe(2);
  for (const [role, name] of [
    ['combobox', 'Severity'], ['textbox', 'Service'], ['textbox', 'Requested action'],
    ['combobox', 'Allowed access mode'], ['spinbutton', 'Access TTL (minutes)'],
    ['textbox', 'Scoped telemetry key (optional)'], ['textbox', 'Symptoms'],
  ] as const) {
    await expect(page.getByRole(role, { name, exact: true })).toBeDisabled();
  }
  await expect(page.locator('#support-rollback')).toBeDisabled();
  await context.testInfo.attach(`pending-support-v4-${context.v4Enabled}`, {
    body: await page.screenshot({ fullPage: true }), contentType: 'image/png',
  });
  fixture.releaseCapture();
  await expect(page.getByRole('alert')).toContainText('In-product support is not configured');
  await expect(page.getByLabel('Requested action', { exact: true })).toBeEnabled();
  await expect(page.getByLabel('Requested action', { exact: true })).toHaveValue('Diagnose synthetic errors');
  await expect(page.getByLabel('Symptoms', { exact: true })).toHaveValue('Synthetic failure');
}

for (const v4Enabled of [false, true]) {
  test(`support draft stays locked during context capture (v4=${v4Enabled})`, async ({ page }, testInfo) => {
    const fixture = await supportFixture({ v4Enabled });
    const { server, releaseCapture } = fixture;
    try {
      const address = server.address();
      if (!address || typeof address === 'string') throw new Error('Fixture did not bind TCP');
      await page.goto(`http://127.0.0.1:${address.port}/console/support`);
      await expect(page.locator('.support-context-list')).toBeVisible();
      await expect(page.locator('.support-context-errors li')).toHaveCount(2);
      await checkSupportReflow(page, { v4Enabled, testInfo });
      await checkPendingSubmission(page, fixture, { v4Enabled, testInfo });
    } finally {
      releaseCapture();
      await new Promise<void>((resolve, reject) => server.close(error => error ? reject(error) : resolve()));
    }
  });
}
