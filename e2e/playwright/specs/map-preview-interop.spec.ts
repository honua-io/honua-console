import { test, expect } from '@playwright/test';

// Spec 4: the MapPreview JS interop module (`map-preview.js`) — the browser-side surface
// bUnit cannot cover (bUnit fakes JS interop and never loads the real ES module).
//
// In missing-binding / no-style mode the .NET component keeps its inline SVG schematic and never
// mounts a live MapLibre map. The module's contract for that path is: every entry point degrades
// gracefully (returns false / no-throw) when no style URL is bound. We pull the real module source
// from the host and execute it in a real browser to assert that contract directly.
//
// We fetch the source text and import it via a Blob URL rather than importing the served path
// directly: ASP.NET's MapStaticAssets pipeline serves `_content` assets with environment-dependent
// (sometimes empty) MIME types, which the browser's strict ES-module loader rejects. Sourcing the
// exact served bytes and importing them under a guaranteed `text/javascript` Blob keeps the spec
// deterministic across the Development (`dotnet run`) and published-Release hosts.
const MODULE_PATH = '/_content/Honua.Console.Shell/map-preview.js';

test('map-preview.js is served by the host and is the MapPreview ES module', async ({ request }) => {
  const response = await request.get(MODULE_PATH);
  expect(response.status()).toBe(200);
  const body = await response.text();
  // The served bytes are the real interop module (exported entry points present).
  expect(body).toContain('export async function init');
  expect(body).toContain('export function setBasemap');
  expect(body).toContain('export function dispose');
});

test('map-preview.js degrades gracefully (schematic stays) when no style is bound', async ({ page }) => {
  // Load a host page so fetch + import resolve same-origin under the host's CSP.
  await page.goto('/studio', { waitUntil: 'domcontentloaded' });

  const result = await page.evaluate(async (modulePath) => {
    // Pull the exact served source, then import it under a guaranteed JS MIME (Blob URL).
    const src = await (await fetch(modulePath)).text();
    const blobUrl = URL.createObjectURL(new Blob([src], { type: 'text/javascript' }));
    const mod = await import(/* @vite-ignore */ blobUrl);
    URL.revokeObjectURL(blobUrl);

    const container = document.createElement('div');
    document.body.appendChild(container);
    // No style URL bound: the module must NOT mount a live map; it returns false so the
    // .NET component keeps its SVG schematic placeholder.
    const noStyle = await mod.init(container, { center: [0, 0], zoom: 1 });
    // Null/empty args also degrade rather than throw.
    const noArgs = await mod.init(null, null);
    const emptyOptions = await mod.init(container, {});
    container.remove();
    return {
      exportsInit: typeof mod.init === 'function',
      exportsSetBasemap: typeof mod.setBasemap === 'function',
      exportsDispose: typeof mod.dispose === 'function',
      noStyle,
      noArgs,
      emptyOptions,
    };
  }, MODULE_PATH);

  // The interop module exposes the expected surface.
  expect(result.exportsInit).toBe(true);
  expect(result.exportsSetBasemap).toBe(true);
  expect(result.exportsDispose).toBe(true);

  // No live map is bound when no style is supplied — the schematic placeholder stays.
  expect(result.noStyle).toBe(false);
  expect(result.noArgs).toBe(false);
  expect(result.emptyOptions).toBe(false);
});
