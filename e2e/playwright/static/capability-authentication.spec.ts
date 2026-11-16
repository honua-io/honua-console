import { expect, test } from '@playwright/test';
import { fileURLToPath } from 'node:url';
import { startStaticBundleServer } from '../static-bundle-server.mjs';
import { createContractApi, fixtureCookie } from '../static-contract-server.mjs';

test('Share capability follows direct sign-in and restored authentication', async ({ page }, testInfo) => {
  const { server, origin, observations } = await startAuthenticatedCapabilityServer();
  try {
    await page.goto(`${origin}/console/auth/signin?returnTo=${encodeURIComponent('/share/manage')}`);
    await page.getByLabel('Password', { exact: true }).fill('fixture-password');
    await page.getByRole('button', { name: 'Continue', exact: true }).click();
    await page.waitForURL(url => url.pathname === '/console/share/manage');
    const heading = page.getByRole('heading', { name: 'Share access', exact: true });
    await expect(heading).toBeVisible();
    expect(observations.manifestReads).toBeGreaterThan(0);
    const readsAfterSignIn = observations.manifestReads;

    await page.reload();
    await expect(heading).toBeVisible();
    expect(observations.manifestReads).toBeGreaterThan(readsAfterSignIn);
    const input = page.getByRole('textbox', { name: 'Item ID', exact: true });
    await input.focus();
    await page.keyboard.press('Tab');
    await expect(page.getByRole('button', { name: 'Open share access', exact: true })).toBeFocused();
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    await testInfo.attach('share-after-restored-authentication', {
      body: await page.screenshot({ fullPage: true }), contentType: 'image/png',
    });
    expect(observations.mutations).toEqual([]);
  } finally {
    await new Promise<void>((resolve, reject) => server.close(error => error ? reject(error) : resolve()));
  }
});

async function startAuthenticatedCapabilityServer() {
  const observations = { mutations: [] as string[], manifestReads: 0 };
  let contract: ReturnType<typeof createContractApi>;
  const server = await startStaticBundleServer(
    fileURLToPath(new URL('../../../artifacts/static-publish/wwwroot/console', import.meta.url)), 0, {
      configuration: { apiBaseUrl: '/', auth: { mode: 'builtin' }, v4Enabled: true },
      handleApi: async (request, response) => {
        const path = new URL(request.url!, 'http://localhost').pathname;
        if (request.method !== 'GET' && !(request.method === 'POST' && path === '/api/v1/admin/auth/session')) {
          observations.mutations.push(`${request.method} ${path}`);
          response.writeHead(405).end('{}');
          return;
        }
        if (request.method === 'GET' && path === '/api/v1/capabilities/manifest') {
          observations.manifestReads++;
          if (!request.headers.cookie?.split('; ').includes(fixtureCookie)) {
            response.writeHead(401).end('{}');
            return;
          }
          response.setHeader('Content-Type', 'application/json');
          response.end(JSON.stringify({ schemaVersion: 'honua.capability_manifest.v1', capabilities: [{
            id: 'console.content.persistent', lifecycle: 'Implemented', optInRequired: false,
            available: true, supported: true,
          }] }));
          return;
        }
        await contract(request, response);
      },
    });
  const address = server.address();
  if (!address || typeof address === 'string') throw new Error('Console fixture did not bind a TCP port');
  const origin = `http://127.0.0.1:${address.port}`;
  contract = createContractApi({ allowedOrigin: origin });
  return { server, origin, observations };
}
