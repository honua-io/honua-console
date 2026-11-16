import { test, expect, type Page, type TestInfo } from '@playwright/test';
import { signInWithFixturePassword } from './fixture-sign-in';
import { applyTextMode, expectDocumentFits, installTextSpacing, startReflowFixture, textModes } from './reflow-fixture';

const serviceName = 'regional-transportation-network-publication';
const title = 'Regional transportation network and public infrastructure survey';
const scenarios = textModes.flatMap(mode => ['existing', 'new'].map(destination => ({ mode, destination })));

async function preparePublishing(page: Page, origin: string) {
  await page.route('**/api/v1/admin/**', route => {
    const path = new URL(route.request().url()).pathname.replace(/\/$/, '');
    if (path === '/api/v1/admin/connections') return route.fulfill({ json: { success: true, data: [] } });
    if (path === '/api/v1/admin/services') return route.fulfill({ json: { success: true, data: {
      items: [{ serviceName, title, layerCount: 0 }], page: 1, pageSize: 100, total: 1, layerTotal: 0,
    } } });
    if (path === `/api/v1/admin/services/${serviceName}/settings`) {
      return route.fulfill({ json: { success: true, data: { serviceName } } });
    }
    return route.continue();
  });
  await signInWithFixturePassword(page, origin, '/console/operate/publishing');
  const row = page.locator('[data-publish-tree-row]');
  await expect(row).toHaveCount(1);
  await expect(row.locator('.publish-tree-name')).toHaveText(title);
  await expect(row.locator('.publish-tree-display-name')).toContainText(serviceName);
  await installTextSpacing(page);
}

async function selectDestination(page: Page, destination: string) {
  await page.getByRole('button', { name: destination === 'new' ? 'Create new service' : 'Use existing service', exact: true }).click();
  if (destination !== 'new') return;
  const folder = page.getByRole('combobox', { name: 'Publishing Folder', exact: true });
  await expect(folder).toBeEnabled();
  await folder.selectOption('root:');
  const review = page.locator('[data-publish-folder-review]');
  await expect(review).toBeEnabled();
  await page.locator('[data-publish-folder-refresh]').click();
  await expect(review).toBeEnabled();
  await review.click();
  await expect(folder).toHaveValue('root:');
}

async function measurePublishing(page: Page) {
  return page.locator('main').evaluate(section => ({
    viewport: innerWidth, rootWidth: document.documentElement.scrollWidth,
    elements: [...section.querySelectorAll('.publish-segment, .publish-segment-option, .publish-tree-row, .publish-tree-name, .publish-tree-display-name, [data-publish-folders] button, .publish-flow-node, .publish-slot-row, .publish-record-row, .publish-flow-map .console-status')]
      .map(element => { const rect = element.getBoundingClientRect(); return {
        text: element.textContent, left: rect.left, right: rect.right, width: rect.width,
        scrollWidth: element.scrollWidth, clientWidth: element.clientWidth,
      }; }),
  }));
}

function expectPublishingFits(bounds: Awaited<ReturnType<typeof measurePublishing>>) {
  expectDocumentFits(bounds, 'document fits');
  expect(bounds.elements.length).toBeGreaterThan(3);
  for (const element of bounds.elements) {
    expect.soft(element.left, `${element.text}: left`).toBeGreaterThanOrEqual(0);
    expect.soft(element.right, `${element.text}: right`).toBeLessThanOrEqual(bounds.viewport);
    expect.soft(element.width).toBeGreaterThan(0);
    expect.soft(element.scrollWidth, `${element.text}: full text`).toBeLessThanOrEqual(element.clientWidth + 1);
  }
}

async function checkPublishingScenario(page: Page, testInfo: TestInfo, scenario: typeof scenarios[number]) {
  await applyTextMode(page, scenario.mode);
  await selectDestination(page, scenario.destination);
  const bounds = await measurePublishing(page);
  expectPublishingFits(bounds);
  if (scenario.mode.enlarged && scenario.mode.spacing) await testInfo.attach(`publishing-${scenario.destination}`, {
    body: await page.screenshot({ fullPage: true }), contentType: 'image/png',
  });
  return { enlarged: scenario.mode.enlarged, spacing: scenario.mode.spacing, destination: scenario.destination, ...bounds };
}

for (const v4Enabled of [false, true]) {
  test(`publishing destinations reflow with enlarged text, v4=${v4Enabled}`, async ({ page }, testInfo) => {
    const fixture = await startReflowFixture(page, { v4Enabled });
    const measurements: unknown[] = [];
    try {
      await preparePublishing(page, fixture.origin);
      for (const scenario of scenarios) measurements.push(await checkPublishingScenario(page, testInfo, scenario));
      expect(fixture.errors).toEqual([]);
    } finally {
      await testInfo.attach('publishing-bounds', { body: JSON.stringify(measurements, null, 2), contentType: 'application/json' });
      await page.close();
      await fixture.close();
    }
  });
}
