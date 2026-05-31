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
