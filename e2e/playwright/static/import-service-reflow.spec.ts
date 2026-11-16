import { test, expect, type Page } from '@playwright/test';
import { signInWithFixturePassword } from './fixture-sign-in';
import { applyTextMode, expectDocumentFits, installTextSpacing, startReflowFixture, textModes } from './reflow-fixture';

const authModes = ['anonymous', 'arcgis-token', 'token', 'basic', 'oauth'];
const scenarios = textModes.flatMap(mode => authModes.map(authMode => ({ mode, authMode })));

async function prepareImport(page: Page, origin: string) {
  await signInWithFixturePassword(page, origin, '/console/operate/import/service');
  await expect(page.getByRole('combobox', { name: 'Authentication mode' })).toBeVisible();
  await installTextSpacing(page);
}

async function measureImport(page: Page) {
  return page.locator('.operate-import-page').evaluate(section => ({
    viewport: innerWidth, rootWidth: document.documentElement.scrollWidth,
    fields: [...section.querySelectorAll('.studio-field-grid label, .console-input, .studio-field-grid small')]
      .map(element => { const rect = element.getBoundingClientRect();
        const panel = element.closest('.console-panel')!.getBoundingClientRect(); return {
        tag: element.tagName, left: rect.left, right: rect.right, width: rect.width,
        panelLeft: panel.left, panelRight: panel.right,
      }; }),
  }));
}

function expectImportFits(bounds: Awaited<ReturnType<typeof measureImport>>, context: string) {
  expectDocumentFits(bounds, `${context}: document`);
  for (const field of bounds.fields) {
    expect.soft(field.left, `${context}: ${field.tag} left`).toBeGreaterThanOrEqual(0);
    expect.soft(field.right, `${context}: ${field.tag} right`).toBeLessThanOrEqual(bounds.viewport);
    expect.soft(field.width, `${context}: ${field.tag} width`).toBeGreaterThan(0);
    expect.soft(field.left, `${context}: ${field.tag} panel left`).toBeGreaterThanOrEqual(field.panelLeft);
    expect.soft(field.right, `${context}: ${field.tag} panel right`).toBeLessThanOrEqual(field.panelRight);
  }
}

async function checkImportScenario(page: Page, scenario: typeof scenarios[number]) {
  await applyTextMode(page, scenario.mode);
  await page.getByRole('combobox', { name: 'Authentication mode' }).selectOption(scenario.authMode);
  await expect(page.locator('[data-auth-fields]')).toHaveCount(scenario.authMode === 'anonymous' ? 0 : 1);
  const bounds = await measureImport(page);
  expectImportFits(bounds, `${scenario.mode.name}/${scenario.authMode}`);
  return { textMode: scenario.mode.name, authMode: scenario.authMode, ...bounds };
}

for (const v4Enabled of [false, true]) {
  test(`service import fields reflow with enlarged text, v4=${v4Enabled}`, async ({ page }, testInfo) => {
    const fixture = await startReflowFixture(page, { v4Enabled });
    const measurements: unknown[] = [];
    try {
      await prepareImport(page, fixture.origin);
      for (const scenario of scenarios) measurements.push(await checkImportScenario(page, scenario));
      await testInfo.attach('import-service-reflow', { body: await page.screenshot({ fullPage: true }), contentType: 'image/png' });
      expect(fixture.errors).toEqual([]);
    } finally {
      await testInfo.attach('field-bounds', { body: JSON.stringify(measurements, null, 2), contentType: 'application/json' });
      await page.close();
      await fixture.close();
    }
  });
}
