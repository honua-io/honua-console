import { expect, test } from '@playwright/test';
import { pendingRoleFixture } from './pending-role-fixture';

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
      for (const mode of ['normal', 'spacing', 'enlarged', 'combined']) {
        const style = await page.addStyleTag({ content: `
          html { font-size: ${mode === 'enlarged' || mode === 'combined' ? 32 : 16}px !important; }
          ${mode === 'spacing' || mode === 'combined' ? '* { line-height: 1.5 !important; letter-spacing: .12em !important; word-spacing: .16em !important; } p { margin-bottom: 2em !important; }' : ''}
        ` });
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
        await testInfo.attach(`role-audit-${mode}-v4-${v4Enabled}`, {
          body: await page.screenshot({ fullPage: true }), contentType: 'image/png',
        });
        await style.evaluate(element => element.remove());
      }
      expect(fixture.requests, 'audit read does not mutate roles').toEqual([]);
      expect(errors).toEqual([]);
    } finally {
      fixture.releaseCreate();
      await new Promise<void>((resolve, reject) => fixture.server.close(error => error ? reject(error) : resolve()));
    }
  });
}
