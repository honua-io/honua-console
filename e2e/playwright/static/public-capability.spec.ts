import { expect, test } from '@playwright/test';
import { fileURLToPath } from 'node:url';
import { startStaticBundleServer } from '../static-bundle-server.mjs';
import { createContractApi } from '../static-contract-server.mjs';

for (const status of [200, 503]) {
  test(`Anonymous public Share reads capability manifest (${status})`, async ({ page, context }, testInfo) => {
    const { server, origin, observations } = await startPublicCapabilityServer(status);
    try {
      await page.goto(`${origin}/console/share/public/items/public-layer`);
      const title = status === 200 ? 'Public layer' : 'Catalog and sharing are not available yet';
      await expect(page.getByRole('heading', { name: title, exact: true })).toBeVisible();
      expect(observations.manifestReads).toBeGreaterThan(0);
      await expect(page.locator('[data-share-opendata-landing]')).toHaveCount(status === 200 ? 1 : 0);
      expect(await context.cookies()).toEqual([]);
      expect(observations.authenticatedReads).toEqual([]);
      expect(observations.mutations).toEqual([]);
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
      await testInfo.attach(`anonymous-public-share-${status}`, {
        body: await page.screenshot({ fullPage: true }), contentType: 'image/png',
      });
    } finally {
      await new Promise<void>((resolve, reject) => server.close(error => error ? reject(error) : resolve()));
    }
  });
}

async function startPublicCapabilityServer(manifestStatus: number) {
  const observations = { mutations: [] as string[], authenticatedReads: [] as string[], manifestReads: 0 };
  let contract: ReturnType<typeof createContractApi>;
  const server = await startStaticBundleServer(
    fileURLToPath(new URL('../../../artifacts/static-publish/wwwroot/console', import.meta.url)), 0, {
      configuration: { apiBaseUrl: '/', auth: { mode: 'builtin' }, v4Enabled: true },
      handleApi: async (request, response) => {
        const path = new URL(request.url!, 'http://localhost').pathname;
        if (request.method !== 'GET') {
          observations.mutations.push(`${request.method} ${path}`);
          response.writeHead(405).end('{}');
          return;
        }
        if (request.headers.authorization || request.headers.cookie) observations.authenticatedReads.push(path);
        if (path === '/api/v1/capabilities/manifest') {
          observations.manifestReads++;
          response.writeHead(manifestStatus, { 'Content-Type': 'application/json' });
          response.end(JSON.stringify({ schemaVersion: 'honua.capability_manifest.v1', capabilities: [{
            id: 'console.content.persistent', lifecycle: 'Implemented', optInRequired: false,
            available: true, supported: true,
          }] }));
          return;
        }
        if (path === '/api/v1/console/content/public-layer') {
          response.writeHead(200, { 'Content-Type': 'application/json' });
          response.end(JSON.stringify({ success: true, data: {
            id: 'public-layer', name: 'public-layer', title: 'Public layer', itemType: 'layer',
            visibility: 'public', ownerId: 'publisher', actions: ['view'], tags: [],
          } }));
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
