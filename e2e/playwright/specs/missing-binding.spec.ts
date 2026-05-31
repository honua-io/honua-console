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
