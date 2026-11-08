import { test, expect } from '@playwright/test';


const ungatedSections = [
  { path: '/operate/data', heading: 'Datasets' },
  { path: '/operate/connections', heading: 'Connections' },
  { path: '/operate/import/esri', heading: 'Import source content' },
  { path: '/operate/publishing', heading: 'Publishing' },
  { path: '/operate/versions', heading: /Versions|Branch.version|version manager/i },
  { path: '/operate/sensors', heading: /SensorThings|Sensor/i },
  { path: '/operate/scenes', heading: /3D Scenes|Scenes/i },
  { path: '/operate/geoprocessing', heading: /Geoprocessing/i },
  { path: '/operate/observability', heading: /Observability/i },
  { path: '/operate/metrics', heading: /Metrics|Performance/i },
  { path: '/operate/access', heading: /Access|Roles/i },
  { path: '/operate/catalogs', heading: /Catalogs/i },
  { path: '/operate/settings', heading: /Settings/i },
  { path: '/operate/deploy', heading: /Deploy/i },
] as const;

const gatedSections = [
  { path: '/operate/sync', heading: /sync/i },
  { path: '/operate/alerts/rules', heading: /alert/i },
  { path: '/operate/temporal', heading: /temporal|history/i },
  { path: '/operate/releases', heading: /release/i },
] as const;

async function openOperateMenu(page: import('@playwright/test').Page) {
  const nav = page.locator('[role="group"][aria-label="Operate"]');
  await expect(async () => {
    if (!(await nav.isVisible())) {
      await page.locator('[data-navigation-menu="Operate"] > summary').click();
    }
    await expect(nav).toBeVisible({ timeout: 2_000 });
  }).toPass({ timeout: 15_000 });
  return nav;
}

test.describe('Operate nav · no AI provider required (live)', () => {
  for (const section of ungatedSections) {
    test(`${section.path} is reachable and renders without AI`, async ({ page }) => {
      const response = await page.goto(section.path, { waitUntil: 'domcontentloaded' });
      expect(response?.status(), `${section.path} should return 200`).toBe(200);
      await expect(page).not.toHaveURL(/\/not-found/);

      await openOperateMenu(page);

      await expect(
        page.getByRole('heading', { name: section.heading }).first(),
      ).toBeVisible();

      const main = page.locator('main.console-main');
      await expect(main).toBeVisible();
      const hasNonAiContent = await main.evaluate((el) => {
        const hasOmniOnly = el.querySelectorAll('[data-omni-prompt]').length > 0 &&
          el.querySelectorAll('.console-page, .console-panel, .console-state').length === 0;
        return !hasOmniOnly;
      });
      expect(
        hasNonAiContent,
        `${section.path} must render a manual operate surface, not an AI-only wall`,
      ).toBeTruthy();
    });
  }

  for (const section of gatedSections) {
    test(`${section.path} resolves to its capability-gate surface (not 404)`, async ({ page }) => {
      const response = await page.goto(section.path, { waitUntil: 'domcontentloaded' });
      expect(response?.status(), `${section.path} should return 200`).toBe(200);
      await expect(page).not.toHaveURL(/\/not-found/);

      await expect(
        page.getByRole('heading', { name: section.heading }).first(),
      ).toBeVisible();
    });
  }

  test('focused operate navigation advertises recovery and inspection, not authoring or AI', async ({ page }) => {
    await page.goto('/operate', { waitUntil: 'domcontentloaded' });
    const nav = await openOperateMenu(page);
    await expect(nav.locator('a[href]')).toHaveCount(9);
    expect(await nav.locator('a[href]').evaluateAll(links => links.map(link => link.getAttribute('href'))))
      .toEqual([
        '/operate', '/inbox', '/operate/health', '/operate/copilot',
        '/operate/geoprocessing', '/operate/observability', '/operate/metrics',
        '/operate/deploy', '/operate/releases',
      ]);
    for (const path of ['/operate/ai', '/operate/publishing', '/operate/access', '/operate/settings']) {
      await expect(nav.locator(`a[href="${path}"]`)).toHaveCount(0);
    }
  });

  test('operate menu shows non-AI sections when AI provider is absent', async ({ page }) => {
    await page.goto('/operate/data', { waitUntil: 'domcontentloaded' });

    const nav = await openOperateMenu(page);

    const manualSurfaces = ['Dashboard', 'Approvals', 'Health', 'Findings', 'Geoprocessing', 'Observability', 'Metrics', 'Deploy', 'Releases'];
    for (const label of manualSurfaces) {
      await expect(nav.getByText(label), `menu must show "${label}"`).toBeVisible();
    }

    await expect(page.getByRole('heading', { name: 'Datasets' }).first()).toBeVisible();
  });
});
