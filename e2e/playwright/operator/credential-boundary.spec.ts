import { test, expect } from '@playwright/test';
import http from 'node:http';
import { humanSessionCookie, serveHumanSignIn } from './human-review-fixture';

// An instrumented upstream proves Console transport behavior. This is deliberately not
// evidence of honua-server authorization: its real policy matrix is a separate release criterion.
let upstream: http.Server;
let requests: Array<{ url: string; method: string; bearer?: string; key?: string; cookie?: string }> = [];
let consoleOrigin = '';
const tileBytes = Buffer.from([0x1a, 0x03, 0x0a, 0x01, 0x41]);
const edge = (actor: string, token?: string) => ({
  'X-Honua-Edge-Auth': 'operator-test-edge-secret',
  'X-Forwarded-User': actor,
  ...(token ? { 'X-Forwarded-Access-Token': token } : {}),
});

test.beforeAll(async () => {
  upstream = http.createServer((req, res) => {
    requests.push({ url: req.url!, method: req.method!, bearer: req.headers.authorization, key: req.headers['x-api-key'] as string | undefined, cookie: req.headers.cookie });
    res.setHeader('Content-Type', 'application/json');
    if (serveHumanSignIn(req, res, consoleOrigin)) return;
    const privateProposal = req.url!.startsWith('/api/v1/admin/proposals/')
      && req.headers.cookie?.includes(`honua_admin_session=${humanSessionCookie}`);
    if (req.headers.authorization !== 'Bearer alice-valid' && !privateProposal) {
      res.writeHead(req.headers.authorization === 'Bearer expired' ? 401 : 403);
      // A hostile diagnostic must never be copied into proxy denial responses.
      res.end('{"detail":"secret upstream tenant and resource identifier"}');
    } else if (req.url!.startsWith('/api/v1/admin/proposals')) {
      const proposal = {proposalId: 'hydration-probe', kind: 'Deploy', status: 'AwaitingApproval',
        requestedBy: 'hydration-operator', summary: 'Hydration fixture', riskLevel: 'Low',
        createdAt: '2026-01-01T00:00:00Z', updatedAt: '2026-01-01T00:00:00Z', blockingReasons: [],
        version: 7, planHash: 'hydration-plan'};
      if (req.url!.endsWith('/approval-review')) {
        res.setHeader('Cache-Control', 'no-store');
        res.end(JSON.stringify({ proposal, review: { proposalId: proposal.proposalId, proposalVersion: 7,
          planHash: proposal.planHash, capability: 'operator-fixture-capability', tenantId: null,
          expiresAt: new Date(Date.now() + 5 * 60 * 1000).toISOString() } }));
      } else if (req.method === 'POST') {
        res.writeHead(403); res.end(JSON.stringify({message: 'The requester cannot approve this proposal.'}));
      } else res.end(JSON.stringify(req.url!.includes('/hydration-probe') ? proposal : {proposals: [proposal]}));
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
test.beforeEach(({}, testInfo) => { requests = []; consoleOrigin = testInfo.project.use.baseURL!; });

// Proxy URLs are pinned to the environment that rendered them; the configured-server seed profile is "local-dev".
for (const route of ['/map-proxy/local-dev/styles/7.json', '/map-proxy/local-dev/tiles/7/0/0/0.mvt', '/map-proxy/local-dev/features/parcels/7']) {
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

  test(`a URL pinned to another environment fails closed before transport: ${route}`, async ({ request }) => {
    const response = await request.get(route.replace('/local-dev/', '/environment-a/'), { headers: edge('alice', 'alice-valid') });
    expect(response.status()).toBe(409);
    expect(await response.json()).toEqual({ message: 'The active Honua environment changed. Reload this view.', reload: true });
    expect(response.headers()['cache-control']).toBe('no-store');
    expect(requests).toEqual([]);
  });
}

test('browser fetches exact feature values and tile bytes as its own operator', async ({ page, baseURL }) => {
  await page.setExtraHTTPHeaders(edge('alice', 'alice-valid'));
  await page.goto('/version.json');
  const result = await page.evaluate(async () => {
    const features = await fetch('/map-proxy/local-dev/features/parcels/7').then(r => r.json());
    const tile = await fetch('/map-proxy/local-dev/tiles/7/0/0/0.mvt');
    const style = await fetch('/map-proxy/local-dev/styles/7.json').then(r => r.json());
    return { features, tile: Array.from(new Uint8Array(await tile.arrayBuffer())), cache: tile.headers.get('cache-control'), style };
  });
  expect(result.features.features.map((f: { attributes: { count: number } }) => f.attributes.count)).toEqual([17, 25]);
  expect(result.tile).toEqual([0x1a, 0x03, 0x0a, 0x01, 0x41]);
  // Tiles use MapProxySupport.ApplyTileCacheHeaders, which forwards upstream validators (ETag,
  // Last-Modified) and forces a private/must-revalidate policy rather than the "no-store" default
  // set before the upstream call — see MapProxyCacheHeaderTests for the header-builder coverage.
  expect(result.cache).toBe('private, no-cache, must-revalidate');
  expect(result.style.sources.parcels.tiles).toEqual([`${baseURL}/map-proxy/local-dev/tiles/7/{z}/{x}/{y}.mvt`]);
  expect(requests).toHaveLength(3);
  expect(requests.every(r => r.bearer === 'Bearer alice-valid' && !r.key)).toBe(true);
});


test('configured presentation is visible without changing the identity boundary', async ({ page }, testInfo) => {
  await page.setExtraHTTPHeaders(edge('presentation-operator', 'alice-valid'));
  await page.goto('/studio');
  await expect(page.locator('[data-console-mode]')).toHaveAttribute('data-console-mode', testInfo.project.name);
  await expect(page.locator('[data-focused-preview]')).toContainText('Preview');
  await page.locator('[data-navigation-menu="Operate"] > summary').click();
  await expect(page.locator('nav[aria-label="Primary"] a[href="/inbox"]')).toBeVisible();
  await expect(page.locator('nav[aria-label="Primary"] a[href="/share/public"]')).toHaveCount(0);
});

test('approval controls wait for hydration before dispatching a decision and retain server denial', async ({page}) => {
  await page.setExtraHTTPHeaders(edge('hydration-operator'));
  await page.goto('/auth/server/login?profileId=local-dev&returnTo=%2Fversion.json');
  await page.waitForURL('**/version.json');
  const proof = (await page.context().cookies()).find(cookie => cookie.name.startsWith('__Host-HonuaApproval.'));
  expect(proof).toMatchObject({ secure: true, httpOnly: true, sameSite: 'Strict' });
  await page.route('**/inbox?proposalId=hydration-probe', async route => {
    const response = await route.fetch(); const html = await response.text();
    const manual = html.replace(/<script src="([^"]*_framework\/blazor\.web\.[^"]+)"/, '<script autostart="false" src="$1"');
    expect(manual).not.toBe(html); await route.fulfill({response, body: manual});
  });
  await page.goto('/inbox?proposalId=hydration-probe');
  const approve = page.locator('[data-proposal-approve]');
  await expect(approve).toBeVisible(); await expect(approve).toBeDisabled();
  await expect(page.locator('[data-proposal-reason]')).toBeDisabled();
  expect(requests.filter(request => request.method === 'POST' && request.url.endsWith('/approve'))).toHaveLength(0);
  const renderedStatus = await page.locator('[data-proposal-status]').elementHandle();
  await page.evaluate(() => (window as any).Blazor.start());
  await expect.poll(() => renderedStatus!.evaluate(node => node.isConnected)).toBe(false);
  await expect(approve).toBeEnabled(); await approve.click();
  await expect(page.locator('[data-proposal-rbac-denied]')).toBeVisible();
  await expect(approve).toBeDisabled();
  expect(requests.filter(request => request.method === 'POST' && request.url.endsWith('/approve'))).toHaveLength(1);
  expect(requests.find(request => request.method === 'POST' && request.url.endsWith('/approve')))
    .toMatchObject({ cookie: `honua_admin_session=${humanSessionCookie}`, bearer: undefined, key: undefined });
});
