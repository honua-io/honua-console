import { expect, test, type Page, type TestInfo } from '@playwright/test';
import { pendingRoleFixture } from './pending-role-fixture';
import { applyTextMode, textModes } from './reflow-typography';

async function checkAuditReflow(page: Page, context: { v4Enabled: boolean; testInfo: TestInfo }) {
  const region = page.getByRole('region', { name: 'Role change audit', exact: true });
  for (const mode of textModes) {
    const style = await applyTextMode(page, mode);
    await region.evaluate(element => { element.scrollLeft = 0; });
    await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth))
      .toBeLessThanOrEqual(page.viewportSize()!.width);
    await region.focus();
    await expect(region).toBeFocused();
    if (await region.evaluate(element => element.scrollWidth > element.clientWidth)) {
      await page.keyboard.press('ArrowRight');
      await expect.poll(() => region.evaluate(element => element.scrollLeft), 'keyboard scroll exposes audit columns')
        .toBeGreaterThan(0);
      await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth))
        .toBeLessThanOrEqual(page.viewportSize()!.width);
    }
    await context.testInfo.attach(`role-audit-${mode}-v4-${context.v4Enabled}`, {
      body: await page.screenshot({ fullPage: true }), contentType: 'image/png',
    });
    await style.evaluate(element => element.remove());
  }
}

for (const v4Enabled of [false, true]) {
  test(`Role audit scrolls inside the page with enlarged text (v4=${v4Enabled})`, async ({ page }, testInfo) => {
    const fixture = await pendingRoleFixture(v4Enabled);
    const errors: string[] = [];
    page.on('pageerror', error => errors.push(error.message));
    try {
      const address = fixture.server.address();
      if (!address || typeof address === 'string') throw new Error('Fixture did not bind TCP');
      await page.goto(`http://127.0.0.1:${address.port}/console/operate/access?workspace=first`);
      await page.locator('[data-rbac-audit]').click();
      const region = page.getByRole('region', { name: 'Role change audit', exact: true });
      await expect(region.locator('tbody')).toContainText('fixture-operator');
      await checkAuditReflow(page, { v4Enabled, testInfo });
      expect(fixture.requests, 'audit read does not mutate roles').toEqual([]);
      expect(errors).toEqual([]);
    } finally {
      fixture.releaseCreate();
      await new Promise<void>((resolve, reject) => fixture.server.close(error => error ? reject(error) : resolve()));
    }
  });
}
