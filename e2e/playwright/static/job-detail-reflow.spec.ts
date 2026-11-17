import { expect, test, type Page } from '@playwright/test';
import { signInWithFixturePassword } from './fixture-sign-in';
import { jobDetailFixture, jobId } from './job-detail-fixture';
import { applyTextMode, textModes } from './reflow-typography';

// Every text block of the diagnostics and detail panels must sit inside its panel.
async function measureJobPanels(page: Page) {
  return page.evaluate(() => {
    const panels = [...document.querySelectorAll('[data-job-timeline], .operate-detail-grid > *, .operate-job-steps')];
    const escaped = panels.flatMap(panel => {
      const bounds = panel.getBoundingClientRect();
      return [panel, ...panel.querySelectorAll('*')]
        .filter(element => {
          const box = element.getBoundingClientRect();
          // Scrollable regions (log lines) may scroll; clipped text is lost.
          const clipped = ['hidden', 'clip'].includes(getComputedStyle(element).overflowX);
          const spills = clipped && element.scrollWidth > element.clientWidth + 1;
          return box.width > 0 && (box.right > bounds.right + 1 || box.right > innerWidth + 1 || spills);
        })
        .map(element => `${element.tagName}.${element.className}`);
    });
    return { panels: panels.length, rootWidth: document.documentElement.scrollWidth, viewport: innerWidth, escaped };
  });
}

for (const v4Enabled of [false, true]) {
  test(`job detail fits a phone viewport and offers no empty event paging, v4=${v4Enabled}`, async ({ page }, testInfo) => {
    const { server, diagnosticCursors } = await jobDetailFixture({ v4Enabled });
    const errors: string[] = [];
    page.on('pageerror', error => errors.push(error.message));
    const origin = `http://127.0.0.1:${(server.address() as { port: number }).port}`;
    try {
      await page.setViewportSize({ width: 390, height: 900 });
      await signInWithFixturePassword(page, origin, `/console/operate/geoprocessing/${jobId}`);
      await expect(page.locator('[data-job-timeline]')).toBeVisible();
      await expect(page.locator('[data-job-diagnostics-artifact-items]')).toBeVisible();
      await expect(page.locator('.operate-job-steps')).toBeVisible();
      await expect(page.getByRole('button', { name: 'Read earlier events' })).toHaveCount(0);
      for (const mode of textModes) {
        const style = await applyTextMode(page, mode);
        const bounds = await measureJobPanels(page);
        await testInfo.attach(`job-detail-390-${mode}-v4-${v4Enabled}`, {
          body: await page.screenshot({ fullPage: true }), contentType: 'image/png' });
        expect(bounds.panels, `${mode}: panels rendered`).toBeGreaterThanOrEqual(3);
        expect.soft(bounds.rootWidth, `${mode}: document width`).toBeLessThanOrEqual(bounds.viewport);
        expect.soft(bounds.escaped, `${mode}: content stays inside its panel`).toEqual([]);
        await style.evaluate(element => element.remove());
      }
      expect(diagnosticCursors.every(cursor => cursor === null), 'no earlier-event page was requested').toBe(true);
      expect(errors).toEqual([]);
    } finally {
      await page.close();
      await new Promise<void>(resolve => server.close(() => resolve()));
    }
  });
}
