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

// Nav↔route integrity: every non-capability-gated operate section in the nav resolves to
// a real page (not 404, not /not-found). Capability-gated sections still render a real page
// (the ConsoleCapabilityGate "unsupported" surface) rather than a 404 — they are included
// here to confirm routing works even when the capability is not advertised.
//
// This list mirrors OperateSections in ConsoleLayout.razor. When a new section is added to
// the nav, add a matching entry here so the test catches any path/page mismatch at CI time.
const operateSections = [
  // Data
  { path: '/operate/data', label: 'Data & Layers' },
  { path: '/operate/connections', label: 'Connections' },
  { path: '/operate/import/esri', label: 'Import from Esri' },
  { path: '/operate/publishing', label: 'Publishing' },
  { path: '/operate/versions', label: 'Versions' },
  { path: '/operate/sync', label: 'Sync', capabilityGated: true },
  // Services
  { path: '/operate/sensors', label: 'SensorThings' },
  { path: '/operate/scenes', label: '3D Scenes' },
  { path: '/operate/geoprocessing', label: 'Geoprocessing' },
  // Monitor
  { path: '/operate/observability', label: 'Observability' },
  { path: '/operate/metrics', label: 'Metrics' },
  { path: '/operate/alerts/rules', label: 'Alert Rules', capabilityGated: true },
  { path: '/operate/temporal', label: 'Temporal', capabilityGated: true },
  // Access & Admin
  { path: '/operate/access', label: 'Access' },
  { path: '/operate/catalogs', label: 'Catalogs' },
  { path: '/operate/settings', label: 'Settings' },
  // Deploy
  { path: '/operate/deploy', label: 'Deploy' },
  { path: '/operate/releases', label: 'Releases', capabilityGated: true },
  // AI accelerator
  { path: '/operate/ai', label: 'Ask Honua (AI)' },
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

test('operate sections sidebar: AI entry is last and labeled as AI', async ({ page }) => {
  await page.goto('/operate', { waitUntil: 'domcontentloaded' });

  const nav = page.locator('nav[aria-label="Operate sections"]');
  await expect(nav).toBeVisible();

  // "Ask Honua (AI)" must appear in the operate sections nav.
  await expect(nav.getByText('Ask Honua (AI)')).toBeVisible();

  // AI must be the LAST item — not the first or a hero entry.
  const navLinks = nav.locator('a[href]');
  const count = await navLinks.count();
  expect(count, 'operate sections nav should have entries').toBeGreaterThan(0);
  const lastHref = await navLinks.nth(count - 1).getAttribute('href');
  expect(lastHref, 'last operate section link must be /operate/ai').toBe('/operate/ai');
});
