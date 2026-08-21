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
// The #333 and #334 regressions themselves are caught by the final tests, which force the lazy
// path: they mount a real map, chart, and 3D Tiles document and assert the libraries that executed
// came from this origin, at the pinned versions, with nothing fetched off-origin. That combination
// gives breadth from the walk and depth where the CDN dependency actually lived.
//
// The optional OpenStreetMap raster basemap in map-preview.js remains the one deliberately declared
// external imagery origin. scripts/__tests__/vendored-assets.test.mjs fails if another origin appears.

const lockPath = fileURLToPath(new URL('../../../scripts/vendored-assets.lock.json', import.meta.url));
const lock = JSON.parse(readFileSync(lockPath, 'utf8'));
const MAPLIBRE_VERSION: string = lock.packages['maplibre-gl'].version;
const MAPLIBRE_JS = '/_content/Honua.Console.Shell/vendor/maplibre-gl/maplibre-gl.js';
const MAPLIBRE_CSS = '/_content/Honua.Console.Shell/vendor/maplibre-gl/maplibre-gl.css';
const MODULE_PATH = '/_content/Honua.Console.Shell/map-preview.js';

const VEGA_VERSION: string = lock.packages['vega'].version;
const VEGA_LITE_VERSION: string = lock.packages['vega-lite'].version;
const VEGA_EMBED_VERSION: string = lock.packages['vega-embed'].version;
const VEGA_JS = '/_content/Honua.Console.Shell/vendor/vega/vega.min.js';
const VEGA_LITE_JS = '/_content/Honua.Console.Shell/vendor/vega-lite/vega-lite.min.js';
const VEGA_EMBED_JS = '/_content/Honua.Console.Shell/vendor/vega-embed/vega-embed.min.js';
const CHART_MODULE_PATH = '/_content/Honua.Console.Shell/chart-preview.js';
const SCENE_MODULE_PATH = '/_content/Honua.Console.Shell/scene-viewer.js';

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

test('the three vendored Vega bundles are served from this origin', async ({ request }) => {
  for (const [asset, minimumBytes] of [
    [VEGA_JS, 400_000],
    [VEGA_LITE_JS, 200_000],
    [VEGA_EMBED_JS, 40_000],
  ] as const) {
    const script = await request.get(asset);
    expect(script.status(), `${asset} must be served by the host`).toBe(200);
    expect((await script.body()).byteLength).toBeGreaterThan(minimumBytes);
  }
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

test('a mounted chart runs the vendored Vega build, fetched from this origin', async ({ page, baseURL }) => {
  const origin = new URL(baseURL!).origin;
  const offOrigin = recordOffOriginRequests(page, origin);
  await page.goto('/studio', { waitUntil: 'domcontentloaded' });

  // The same forced-lazy-path trick as the map test above, for the other interop that used to reach
  // for jsdelivr. The spec carries inline data so no features proxy (and so no live server) is
  // needed, and it keeps its real `$schema` URL: vega-embed parses that string to pick its mode and
  // must never fetch it — if it ever did, the recorder below would catch it.
  const result = await page.evaluate(
    async ({ modulePath, spec }) => {
      const source = await (await fetch(modulePath)).text();
      const blobUrl = URL.createObjectURL(new Blob([source], { type: 'text/javascript' }));
      const mod = await import(/* @vite-ignore */ blobUrl);
      URL.revokeObjectURL(blobUrl);

      const container = document.createElement('div');
      container.style.width = '320px';
      container.style.height = '240px';
      document.body.appendChild(container);
      const mounted = await mod.init(container, { spec });
      const scriptSrc = (file: string) =>
        document.querySelector<HTMLScriptElement>(`script[src*="${file}"]`)?.src ?? null;
      const globals = window as unknown as {
        vega?: { version: string };
        vegaLite?: { version: string };
        vegaEmbed?: { version: string };
      };
      const rendered = container.querySelector('svg') !== null;
      const versions = {
        vega: globals.vega?.version,
        vegaLite: globals.vegaLite?.version,
        vegaEmbed: globals.vegaEmbed?.version,
      };
      const sources = {
        vega: scriptSrc('vega.min.js'),
        vegaLite: scriptSrc('vega-lite.min.js'),
        vegaEmbed: scriptSrc('vega-embed.min.js'),
      };
      mod.dispose(container);
      container.remove();
      return { mounted, rendered, versions, sources };
    },
    {
      modulePath: CHART_MODULE_PATH,
      spec: JSON.stringify({
        $schema: 'https://vega.github.io/schema/vega-lite/v6.json',
        mark: 'bar',
        data: { values: [{ category: 'a', amount: 28 }, { category: 'b', amount: 55 }] },
        encoding: {
          x: { field: 'category', type: 'nominal' },
          y: { field: 'amount', type: 'quantitative' },
        },
      }),
    },
  );

  // All three libraries really loaded and really drew a chart (not the graceful-degradation path).
  expect(result.mounted, 'Vega failed to load — the vendored assets are not reachable').toBe(true);
  expect(result.rendered, 'vega-embed did not render an SVG into the container').toBe(true);
  // …at the versions this repo pins, from this repo's own origin.
  expect(result.versions).toEqual({
    vega: VEGA_VERSION,
    vegaLite: VEGA_LITE_VERSION,
    vegaEmbed: VEGA_EMBED_VERSION,
  });
  expect(result.sources).toEqual({
    vega: `${origin}${VEGA_JS}`,
    vegaLite: `${origin}${VEGA_LITE_JS}`,
    vegaEmbed: `${origin}${VEGA_EMBED_JS}`,
  });

  // And loading them pulled nothing from anywhere else — including the $schema URL.
  expect(offOrigin, 'mounting a chart reached off-origin').toEqual([]);
});

test('a mounted 3D Tiles scene uses vendored Cesium, no Ion base layer, and only this origin', async ({ page, baseURL }) => {
  test.setTimeout(60_000);
  const origin = new URL(baseURL!).origin;
  const offOrigin = recordOffOriginRequests(page, origin);
  const tilesetRequests: string[] = [];
  await page.route('**/scene-proxy/scenes/reviewed/tileset.json', async (route) => {
    tilesetRequests.push(route.request().url());
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        asset: { version: '1.1' },
        geometricError: 0,
        root: {
          boundingVolume: { region: [-0.001, -0.001, 0.001, 0.001, 0, 20] },
          geometricError: 0,
          refine: 'ADD',
        },
      }),
    });
  });
  await page.goto('/operate/scenes', { waitUntil: 'domcontentloaded' });

  const result = await page.evaluate(async ({ modulePath, splitHostTileset }) => {
    const source = await (await fetch(modulePath)).text();
    const blobUrl = URL.createObjectURL(new Blob([source], { type: 'text/javascript' }));
    const mod = await import(/* @vite-ignore */ blobUrl);
    URL.revokeObjectURL(blobUrl);

    const container = document.createElement('div');
    container.style.width = '640px';
    container.style.height = '360px';
    document.body.appendChild(container);
    const mounted = await mod.init(container, splitHostTileset);
    const inspection = mod.inspect(container);
    const version = (window as unknown as { Cesium?: { VERSION?: string } }).Cesium?.VERSION;
    const canvas = container.querySelector('canvas') !== null;
    mod.dispose(container);
    container.remove();
    return { mounted, inspection, version, canvas };
  }, {
    modulePath: SCENE_MODULE_PATH,
    splitHostTileset: 'https://server.example/scenes/reviewed/tileset.json',
  });

  expect(result.mounted, 'Cesium failed to mount the reviewed 3D Tiles document').toBe(true);
  expect(result.canvas, 'Cesium did not create its WebGL canvas').toBe(true);
  expect(result.version).toBe('1.119');
  expect(result.inspection).toEqual({
    imageryLayerCount: 0,
    tilesetUrl: `${origin}/scene-proxy/scenes/reviewed/tileset.json`,
  });
  expect(tilesetRequests).toEqual([`${origin}/scene-proxy/scenes/reviewed/tileset.json`]);
  expect(offOrigin, 'mounting a 3D Tiles scene reached off-origin (including Cesium Ion)').toEqual([]);
});
