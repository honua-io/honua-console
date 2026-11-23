import { test, expect, type Page } from '@playwright/test';
import { signInWithFixturePassword } from './fixture-sign-in';
import { applyTextMode, expectDocumentFits, installTextSpacing, startReflowFixture, textModes } from './reflow-fixture';

const pages = [
  { route: '/console/operate/data/new', ready: '[data-step-rail]', parts: '[data-step-rail] .data-publish-flow__rail-label' },
  { route: '/console/studio/report', ready: '.console-toolbar', parts: '.console-toolbar :is(button, input, label)' },
];

async function measure(page: Page, parts: string) {
  return page.evaluate(selector => ({
    viewport: innerWidth, rootWidth: document.documentElement.scrollWidth,
    pageOverflow: Math.max(0, ...[...document.querySelectorAll('.console-page')].map(page => page.scrollWidth - page.clientWidth)),
    parts: [...document.querySelectorAll(selector)].map(element => {
      const rect = element.getBoundingClientRect();
      return { text: element.textContent?.trim() || element.tagName, left: rect.left, right: rect.right, width: rect.width };
    }),
  }), parts);
}

function expectFits(bounds: Awaited<ReturnType<typeof measure>>, context: string) {
  expectDocumentFits(bounds, `${context}: document`);
  expect.soft(bounds.pageOverflow, `${context}: page content overflow`).toBeLessThanOrEqual(1);
  expect(bounds.parts.length, `${context}: measured parts`).toBeGreaterThan(0);
  for (const part of bounds.parts) {
    expect.soft(part.left, `${context}: ${part.text} left`).toBeGreaterThanOrEqual(0);
    expect.soft(part.right, `${context}: ${part.text} right`).toBeLessThanOrEqual(bounds.viewport);
    expect.soft(part.width, `${context}: ${part.text} width`).toBeGreaterThan(0);
  }
}

for (const v4Enabled of [false, true]) {
  for (const target of pages) {
    test(`${target.route} toolbar and step rail reflow with enlarged text, v4=${v4Enabled}`, async ({ page }, testInfo) => {
      const fixture = await startReflowFixture(page, { v4Enabled });
      const measurements: unknown[] = [];
      try {
        await signInWithFixturePassword(page, fixture.origin, target.route);
        await expect(page.locator(target.ready).first()).toBeVisible();
        await installTextSpacing(page);
        for (const mode of textModes) {
          await applyTextMode(page, mode);
          const bounds = await measure(page, target.parts);
          expectFits(bounds, mode.name);
          measurements.push({ mode: mode.name, ...bounds });
        }
        expect(fixture.errors).toEqual([]);
      } finally {
        await testInfo.attach('reflow-bounds', { body: JSON.stringify(measurements, null, 2), contentType: 'application/json' });
        await page.close();
        await fixture.close();
      }
    });
  }
}
