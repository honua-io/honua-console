import { expect, test } from '@playwright/test';
import type { IncomingMessage, ServerResponse } from 'node:http';
import { fileURLToPath } from 'node:url';
import { startStaticBundleServer } from '../static-bundle-server.mjs';
import { createContractApi } from '../static-contract-server.mjs';
import { signInWithFixturePassword } from './fixture-sign-in';

const total = 250;

function servicePage(page: number, pageSize: number) {
  const start = (page - 1) * pageSize;
  const items = Array.from({ length: Math.max(0, Math.min(pageSize, total - start)) }, (_, offset) => ({
    serviceName: `service-${String(start + offset).padStart(3, '0')}`, layerCount: 2,
  }));
  return { success: true, data: { items, page, pageSize, total, layerTotal: total * 2 } };
}

// Serves 250 services in pages; page 2 answers 503 until the fixture is healed.
async function startPagedServiceFixture() {
  const state = { failSecondPage: true };
  let contract: (request: IncomingMessage, response: ServerResponse) => Promise<void>;
  const handleApi = async (request: IncomingMessage, response: ServerResponse) => {
    const url = new URL(request.url ?? '/', 'http://localhost');
    if (url.pathname.replace(/\/$/, '') !== '/api/v1/admin/services' || request.method !== 'GET') return contract(request, response);
    const pageNumber = Number(url.searchParams.get('page') ?? '1');
    const failed = pageNumber === 2 && state.failSecondPage;
    const body = failed ? { success: false, message: 'Service page unavailable.' }
      : servicePage(pageNumber, Number(url.searchParams.get('pageSize') ?? '100'));
    response.writeHead(failed ? 503 : 200, { 'Content-Type': 'application/json' });
    response.end(JSON.stringify(body));
  };
  const server = await startStaticBundleServer(
    fileURLToPath(new URL('../../../artifacts/static-publish/wwwroot/console', import.meta.url)), 0,
    { configuration: { apiBaseUrl: '/', auth: { mode: 'builtin' }, v4Enabled: true }, handleApi },
  );
  const origin = `http://127.0.0.1:${(server.address() as { port: number }).port}`;
  contract = createContractApi({ allowedOrigin: origin });
  return { origin, state, close: () => new Promise<void>((resolve, reject) => server.close(error => error ? reject(error) : resolve())) };
}

test('v4 tree keeps loaded services when a later service page fails, and retries the rest', async ({ page }, testInfo) => {
  const fixture = await startPagedServiceFixture();
  const errors: string[] = [];
  page.on('pageerror', error => errors.push(error.message));
  try {
    await page.setViewportSize({ width: 1280, height: 900 });
    await signInWithFixturePassword(page, fixture.origin, '/operate/services');
    const tree = page.locator('[data-publication-tree]');
    const retry = page.locator('[data-tree-page-retry]');
    const footer = page.locator('.console-tree-foot');
    await expect(retry).toBeVisible();
    await expect(tree.locator("[data-publication-key='service:service-099']")).toBeVisible();
    await expect(page.locator('.console-tree-live')).toHaveText('Incomplete');
    await expect(footer).toContainText('100 of 250 services');
    await testInfo.attach('tree-page-failure-1280', { body: await page.screenshot(), contentType: 'image/png' });

    fixture.state.failSecondPage = false;
    await retry.click();
    await expect(page.locator('.console-tree-live')).toHaveText('Live');
    await expect(footer).toContainText('250 services');
    await expect(footer).not.toContainText('100 of');
    await expect(retry).toHaveCount(0);
    expect(errors).toEqual([]);
  } finally {
    await page.close();
    await fixture.close();
  }
});
