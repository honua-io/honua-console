import { test, expect } from '@playwright/test';
import http from 'node:http';

// An instrumented upstream proves Console transport behavior. This is deliberately not
// evidence of honua-server authorization: its real policy matrix is a separate release criterion.
let upstream: http.Server;
let requests: Array<{ url: string; bearer?: string; key?: string }> = [];
const tileBytes = Buffer.from([0x1a, 0x03, 0x0a, 0x01, 0x41]);
const edge = (actor: string, token?: string) => ({
  'X-Honua-Edge-Auth': 'operator-test-edge-secret',
  'X-Forwarded-User': actor,
  ...(token ? { 'X-Forwarded-Access-Token': token } : {}),
});

test.beforeAll(async () => {
  upstream = http.createServer((req, res) => {
    requests.push({ url: req.url!, bearer: req.headers.authorization, key: req.headers['x-api-key'] as string | undefined });
    res.setHeader('Content-Type', 'application/json');
    if (req.headers.authorization !== 'Bearer alice-valid') {
      res.writeHead(req.headers.authorization === 'Bearer expired' ? 401 : 403);
      // A hostile diagnostic must never be copied into proxy denial responses.
      res.end('{"detail":"secret upstream tenant and resource identifier"}');
    } else if (req.url!.startsWith('/tiles/')) {
      res.setHeader('Content-Type', 'application/vnd.mapbox-vector-tile');
      res.setHeader('Cache-Control', 'public, max-age=86400');
      res.end(tileBytes);
    } else if (req.url!.startsWith('/api/styles/')) {
      res.end(JSON.stringify({ version: 8, sources: { parcels: { type: 'vector', tiles: ['/tiles/7/{z}/{x}/{y}.mvt'] } }, layers: [] }));
    } else {
      res.end(JSON.stringify({ features: [{ attributes: { id: 7, count: 17 } }, { attributes: { id: 9, count: 25 } }] }));
    }
  });
  await new Promise<void>((resolve) => upstream.listen(5198, '127.0.0.1', resolve));
});
test.afterAll(async () => { await new Promise<void>((resolve) => upstream.close(() => resolve())); });
test.beforeEach(() => { requests = []; });

for (const route of ['/map-proxy/styles/7.json', '/map-proxy/tiles/7/0/0/0.mvt', '/map-proxy/features/parcels/7']) {
  test(`anonymous proxy request requires reauthentication: ${route}`, async ({ request }) => {
    const response = await request.get(route, { maxRedirects: 0 });
    expect(response.status()).toBe(401);
    expect(requests).toEqual([]);
  });

  test(`operator without a server bearer fails closed before transport: ${route}`, async ({ request }) => {
    const response = await request.get(route, { headers: edge(`missing-${route}`) });
    expect(response.status()).toBe(401);
    expect(await response.text()).not.toContain('secret');
    expect(requests).toEqual([]);
  });

  test(`preserves upstream denials with no key fallback or disclosure: ${route}`, async ({ request }) => {
    for (const [token, status] of [['expired', 401], ['bob-wrong-tenant', 403], ['insufficient-scope', 403]] as const) {
      const response = await request.get(route, { headers: edge(token, token) });
      expect(response.status()).toBe(status);
      if (status === 401) expect(await response.json()).toEqual({ message: 'Sign in to honua-server again.', signIn: '/auth/signin' });
      else expect(await response.text()).toBe('');
      expect(response.headers()['cache-control']).toBe('no-store');
      expect(requests.at(-1)?.bearer).toBe(`Bearer ${token}`);
      expect(requests.at(-1)?.key).toBeUndefined();
    }
    expect(requests).toHaveLength(3);
  });
}

test('browser fetches exact feature values and tile bytes as its own operator', async ({ page, baseURL }) => {
  await page.setExtraHTTPHeaders(edge('alice', 'alice-valid'));
  await page.goto('/version.json');
  const result = await page.evaluate(async () => {
    const features = await fetch('/map-proxy/features/parcels/7').then(r => r.json());
    const tile = await fetch('/map-proxy/tiles/7/0/0/0.mvt');
    const style = await fetch('/map-proxy/styles/7.json').then(r => r.json());
    return { features, tile: Array.from(new Uint8Array(await tile.arrayBuffer())), cache: tile.headers.get('cache-control'), style };
  });
  expect(result.features.features.map((f: { attributes: { count: number } }) => f.attributes.count)).toEqual([17, 25]);
  expect(result.tile).toEqual([0x1a, 0x03, 0x0a, 0x01, 0x41]);
  expect(result.cache).toBe('no-store');
  expect(result.style.sources.parcels.tiles).toEqual([`${baseURL}/map-proxy/tiles/7/{z}/{x}/{y}.mvt`]);
  expect(requests).toHaveLength(3);
  expect(requests.every(r => r.bearer === 'Bearer alice-valid' && !r.key)).toBe(true);
});


test('configured presentation is visible without changing the identity boundary', async ({ page }, testInfo) => {
  await page.setExtraHTTPHeaders(edge('presentation-operator', 'alice-valid'));
  await page.goto('/studio');
  await expect(page.locator('[data-console-mode]')).toHaveAttribute('data-console-mode', testInfo.project.name);
  await expect(page.locator('[data-focused-preview]')).toContainText('Preview');
  await expect(page.locator('nav[aria-label="Primary"] a[href="/inbox"]')).toBeVisible();
  await expect(page.locator('nav[aria-label="Primary"] a[href="/share/public"]')).toHaveCount(testInfo.project.name === 'full' ? 1 : 0);
});
