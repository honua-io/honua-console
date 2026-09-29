import { test, expect } from '@playwright/test';

// Spec 2: the four product-area routes all resolve from the same Blazor Web host
// and render their area heading. These are the IA anchors from docs/console-route-map.md.
const areas = [
  { path: '/studio', heading: 'Studio' },
  { path: '/catalog', heading: 'Catalog' },
  { path: '/operate', heading: 'Operate' },
  { path: '/share', heading: 'Share' },
] as const;

for (const area of areas) {
  test(`${area.path} renders the ${area.heading} area`, async ({ page }) => {
    const response = await page.goto(area.path, { waitUntil: 'domcontentloaded' });
    expect(response?.status(), `${area.path} should return 200`).toBe(200);

    // The area heading renders (not a 404 / crash page).
    await expect(
      page.getByRole('heading', { name: area.heading, exact: true }).first(),
    ).toBeVisible();

    // Not the status-code re-execution / not-found surface.
    await expect(page).not.toHaveURL(/\/not-found/);
  });
}

// Nav↔route integrity for the 2026.1 focused human Console. Authoring, broad admin parity,
// 3D, and the Console AI accelerator remain deep-linkable preview routes but are not advertised
// as part of the normal-image inspect/approve/operate/recover boundary.
//
// This list mirrors OperateSections in ConsoleLayout.razor. When a new section is added to
// the nav, add a matching entry here so the test catches any path/page mismatch at CI time.
const operateSections = [
  // Data
  { path: '/operate/health', label: 'Health' },
  { path: '/operate/data', label: 'Data & Layers' },
  { path: '/operate/connections', label: 'Connections' },
  { path: '/operate/services', label: 'Services' },
  // Services
  { path: '/operate/geoprocessing', label: 'Geoprocessing' },
  // Monitor
  { path: '/operate/observability', label: 'Observability' },
  { path: '/operate/metrics', label: 'Metrics' },
  // Deploy
  { path: '/operate/deploy', label: 'Deploy' },
  { path: '/operate/releases', label: 'Releases' },
] as const;

for (const section of operateSections) {
  test(`${section.path} resolves to a real page (not 404)`, async ({ page }) => {
    const response = await page.goto(section.path, { waitUntil: 'domcontentloaded' });
    expect(response?.status(), `${section.path} should return 200`).toBe(200);

    // Route must not redirect to the 404 surface.
    await expect(page).not.toHaveURL(/\/not-found/);

    // The operate sidebar secondary nav appears on any /operate/* route.
    await expect(
      page.locator('nav[aria-label="Operate sections"]'),
    ).toBeVisible();
  });
}

test('operate sections sidebar advertises only the focused human-client boundary', async ({ page }) => {
  await page.goto('/operate', { waitUntil: 'domcontentloaded' });

  const nav = page.locator('nav[aria-label="Operate sections"]');
  await expect(nav).toBeVisible();

  await expect(nav.locator('a[href]')).toHaveCount(operateSections.length);
  for (const section of operateSections) {
    await expect(nav.locator(`a[href="${section.path}"]`)).toBeVisible();
  }
  for (const hidden of ['/operate/scenes', '/operate/publishing', '/operate/ai', '/operate/settings']) {
    await expect(nav.locator(`a[href="${hidden}"]`)).toHaveCount(0);
  }
});
