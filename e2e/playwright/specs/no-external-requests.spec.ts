import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';

import { test, expect } from '@playwright/test';

// Spec 6 (honua-console#333): the Console serves its own assets. Nothing it loads may come from an
// origin outside the app.
//
// This is asserted the only way it can be asserted honestly — by recording EVERY request a real
// browser makes while walking the Console and failing on any that leaves the host origin. A test
// that checked for a hard-coded CDN hostname would pass the day someone added a different one, and
// would say nothing about what the browser actually did. (honua-studio's PR #27 enforces the same
// invariant on the Studio surface the same way; console#324 embeds Studio here, so the two surfaces
// are deliberately held to one rule.)
//
// Scope and its limits, stated plainly rather than implied:
//
// The smoke host runs backend-free (missing-binding mode), so the routes below render their real
// chrome but no live map/scene/chart ever mounts. Every one of these interops loads its runtime
// LAZILY — only when real content is bound — so the page walk on its own would NOT have caught the
// #333 regression, and claiming otherwise would make this file lie about its own strength. Its job
// is the standing invariant: page load, boot, and the interactive circuit must stay on-origin, and
// any new always-on external fetch (a font, an analytics beacon, a stylesheet) fails here loudly.
//
// The #333 regression itself is caught by the last test, which forces the lazy path: it mounts a
// real map and asserts the library that executed came from this origin, at the pinned version, with
// nothing fetched off-origin while doing it. That combination is what makes the pair honest —
// breadth from the walk, depth where the CDN dependency actually lived.
//
// Still uncovered, and deliberately so: CDN fetches reachable only with a live honua-server bound —
// Cesium in scene-viewer.js and Vega in chart-preview.js (honua-console#334), plus the optional
// OpenStreetMap raster basemap in map-preview.js. All three are declared with their reasons in
// scripts/__tests__/vendored-assets.test.mjs, which fails if an undeclared origin ever appears.

const lockPath = fileURLToPath(new URL('../../../scripts/vendored-assets.lock.json', import.meta.url));
const lock = JSON.parse(readFileSync(lockPath, 'utf8'));
const MAPLIBRE_VERSION: string = lock.packages['maplibre-gl'].version;
const MAPLIBRE_JS = '/_content/Honua.Console.Shell/vendor/maplibre-gl/maplibre-gl.js';
const MAPLIBRE_CSS = '/_content/Honua.Console.Shell/vendor/maplibre-gl/maplibre-gl.css';
const MODULE_PATH = '/_content/Honua.Console.Shell/map-preview.js';

// The four product areas plus the routes whose components own a browser interop module
// (map preview, 3D scene viewer, chart preview) — i.e. every page that could plausibly reach
// for a third-party runtime.
const ROUTES = [
  '/studio',
  '/catalog',
  '/operate',
  '/share',
  '/operate/data',
  '/operate/scenes',
  '/operate/observability',
  '/operate/metrics',
] as const;

/**
 * Records every request the page issues and returns the ones that left `origin`.
 * `data:`/`blob:` URLs are same-document bytes with no network hop, so they are not "leaving".
 */
function recordOffOriginRequests(page: import('@playwright/test').Page, origin: string) {
  const offOrigin: string[] = [];
  page.on('request', (request) => {
    const url = request.url();
    if (url.startsWith('data:') || url.startsWith('blob:')) return;
    try {
      if (new URL(url).origin !== origin) offOrigin.push(`${request.method()} ${url}`);
    } catch {
      offOrigin.push(`unparseable ${url}`);
    }
  });
  return offOrigin;
}

test('no Console page issues a request to an origin outside the app', async ({ page, baseURL }) => {
  const origin = new URL(baseURL!).origin;
  const offOrigin = recordOffOriginRequests(page, origin);

  for (const route of ROUTES) {
    const response = await page.goto(route, { waitUntil: 'domcontentloaded' });
    expect(response?.status(), `${route} should render`).toBe(200);
    // Let the interactive circuit boot and any lazily-imported interop module run: a CDN fetch
    // triggered by module evaluation happens after DOMContentLoaded, not during it.
    await page.waitForTimeout(1500);
  }

  expect(
    offOrigin,
    'the Console fetched from an origin outside the app — vendor the asset instead ' +
      '(scripts/vendor-assets.mjs), do not widen the CSP',
  ).toEqual([]);
});

test('MapLibre is served from this origin as the pinned vendored asset', async ({ request }) => {
  const script = await request.get(MAPLIBRE_JS);
  expect(script.status(), 'the vendored MapLibre bundle must be served by the host').toBe(200);
  expect((await script.body()).byteLength).toBeGreaterThan(100_000);

  const stylesheet = await request.get(MAPLIBRE_CSS);
  expect(stylesheet.status(), 'the vendored MapLibre stylesheet must be served by the host').toBe(200);
  // The stylesheet's own assets are inline data: URIs — it pulls nothing further off-origin.
  expect(await stylesheet.text()).not.toMatch(/url\(\s*["']?https?:/i);
});

test('the CSP no longer admits the MapLibre CDN origin', async ({ request }) => {
  const response = await request.get('/studio');
  const csp = response.headers()['content-security-policy'];
  expect(csp, 'every response must carry a CSP').toBeTruthy();
  expect(csp).not.toContain('unpkg.com');
  expect(csp).toContain("script-src 'self'");
});

test('a mounted map runs the vendored MapLibre build, fetched from this origin', async ({ page, baseURL }) => {
  const origin = new URL(baseURL!).origin;
  const offOrigin = recordOffOriginRequests(page, origin);
  await page.goto('/studio', { waitUntil: 'domcontentloaded' });

  // Force the load path the backend-free walk above cannot reach: mount a real map. The style URL
  // is a same-origin document that is not a MapLibre style, so the map reports a style error a
  // moment later — irrelevant here. What matters is that `loadMapLibre()` ran, and where from.
  const result = await page.evaluate(
    async ({ modulePath, styleUrl }) => {
      const source = await (await fetch(modulePath)).text();
      const blobUrl = URL.createObjectURL(new Blob([source], { type: 'text/javascript' }));
      const mod = await import(/* @vite-ignore */ blobUrl);
      URL.revokeObjectURL(blobUrl);

      const container = document.createElement('div');
      container.style.width = '320px';
      container.style.height = '240px';
      document.body.appendChild(container);
      const mounted = await mod.init(container, { styleUrl, center: [0, 0], zoom: 1 });
      const scriptSrc = document.querySelector<HTMLScriptElement>('script[src*="maplibre-gl.js"]')?.src ?? null;
      const linkHref = document.querySelector<HTMLLinkElement>('link[data-honua-maplibre]')?.href ?? null;
      const version = (window as unknown as { maplibregl?: { getVersion(): string } }).maplibregl?.getVersion();
      mod.dispose(container);
      container.remove();
      return { mounted, scriptSrc, linkHref, version };
    },
    { modulePath: MODULE_PATH, styleUrl: '/version.json' },
  );

  // The library really loaded and really mounted a map (not the graceful-degradation path).
  expect(result.mounted, 'MapLibre failed to load — the vendored asset is not reachable').toBe(true);
  // …at the version this repo pins, from this repo's own origin.
  expect(result.version).toBe(MAPLIBRE_VERSION);
  expect(result.scriptSrc).toBe(`${origin}${MAPLIBRE_JS}`);
  expect(result.linkHref).toBe(`${origin}${MAPLIBRE_CSS}`);

  // And loading it pulled nothing from anywhere else.
  expect(offOrigin, 'mounting a map reached off-origin').toEqual([]);
});
