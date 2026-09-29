import { test, expect } from '@playwright/test';

// Spec 3: with no honua-server bound, server-owned surfaces degrade gracefully into an
// explicit missing-binding state rather than crashing (Console Patterns Charter section 11).
// The shared ConsoleStateView renders `.console-state .console-kicker` for these surfaces.

test('share degrades into an explicit missing-binding state, not a crash', async ({ page }) => {
  await page.goto('/share', { waitUntil: 'domcontentloaded' });

  // The shared missing-binding surface is present and readable.
  const state = page.locator('.console-state').first();
  await expect(state).toBeVisible();
  await expect(state.locator('.console-kicker')).toBeVisible();

  // The Share area explicitly reports it is not bound (no mock/seeded data shown).
  await expect(page.getByText('Share is not bound to honua-server')).toBeVisible();
});

test('studio map builder renders the missing-binding surface', async ({ page }) => {
  await page.goto('/studio/map', { waitUntil: 'domcontentloaded' });

  // The map-package lifecycle is not bound — the surface states this rather than crashing.
  await expect(page.getByRole('heading', { name: 'Map package lifecycle is not bound' })).toBeVisible();
  await expect(page.locator('.console-state-error, .console-state-missing').first()).toBeVisible();
});

// Publishing and Esri import were the two surfaces this spec was written for and never covered.
//
// Both used to render design-handoff fixtures — a service tree, a resource table with feature
// counts and PII flags, a review screen quoting a layer slot and a published URL, and an eight-row
// Esri inventory with per-item fidelity verdicts. That content rendered identically whether or not a
// server was bound, which is why the publish wizard passed this backend-free lane for months while
// POSTing to a connection id that existed in no deployment: a route smoke test cannot tell a wired
// page from a mockup, and a mockup is MORE likely to pass one.
//
// Any route that claims a server binding must therefore prove it degrades here, not merely return 200.

test('publishing workspace reports no bound services instead of a seeded service tree', async ({ page }) => {
  await page.goto('/operate/publishing', { waitUntil: 'domcontentloaded' });

  // The service picker states that nothing is bound, and offers no rows to pick.
  await expect(page.locator('[data-publish-tree-empty]')).toBeVisible();
  await expect(page.locator('[data-publish-tree-row]')).toHaveCount(0);

  // The retired fixtures must not reappear on any deployment, bound or not.
  const markup = await page.content();
  for (const fixture of ['prod-postgis', 'parcels_2024', 'public-works-fs', '1,284,021', 'honua.example.gov']) {
    expect(markup, `${fixture} is a retired mockup fixture`).not.toContain(fixture);
  }
});

test('publishing author-first mode declares itself unwired', async ({ page }) => {
  await page.goto('/operate/publishing', { waitUntil: 'domcontentloaded' });

  await page
    .locator('button.publish-mode-option', { hasText: 'Author resource first' })
    .click();

  await expect(page.getByText('This flow is not wired yet')).toBeVisible();
  // The seven fabricated steps (compatibility matrix, slot, fields, projection, access) are gone.
  await expect(page.locator('ol.publish-stepper')).toHaveCount(0);
});

test('esri import wizard reports a missing migration binding instead of an item inventory', async ({ page }) => {
  await page.goto('/operate/import/esri', { waitUntil: 'domcontentloaded' });

  await expect(page.locator('[data-esri-missing-binding]').first()).toBeVisible();

  // The source card used to claim a connected ArcGIS organization with an item count.
  const markup = await page.content();
  expect(markup).not.toContain('org.maps.arcgis.com');
  expect(markup).not.toContain('state-gis');

  // A source that is not connected cannot advance the wizard.
  await expect(page.locator('button.publish-wizard-next')).toBeDisabled();
});
