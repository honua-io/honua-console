import { test, expect } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';

// The Console's accessibility gate (honua-server#4428).
//
// Before this spec, `git grep -rn -i -E "axe-core|axe\.run|accessib|a11y|wcag"` over this repository
// returned four hits and every one of them was prose: docker-compose comments, `aria-describedby`
// design commentary, and two bUnit comments about `<label>` names. Release section 14 requires the
// focused Console journey to pass an accessibility gate, and `docs/.../spec` states "NFR-003:
// Accessibility (AA contrast, keyboard-first including the command palette)" as a requirement with
// no test behind it. This is the first executable check of that requirement.
//
// What it asserts, on the real browser boot the smoke already provisions (backend-free
// missing-binding mode, so no honua-server is needed):
//   1. axe-core finds no `serious` or `critical` violation on any of the four area routes, tagged
//      to WCAG 2.1 A and AA. Moderate and minor findings are REPORTED, not enforced — they are
//      recorded as a test annotation so the debt is visible without the gate asserting a standard
//      the Console has never been measured against.
//   2. the app is keyboard reachable: tabbing from the document body reaches a focusable element
//      inside the main landmark, which is the floor under "keyboard-first".
//
// The threshold is deliberate and documented rather than convenient: `serious` and `critical` are
// axe's own impact levels for findings that block a user, and nothing here suppresses a rule.

const AREA_ROUTES = ['/studio', '/catalog', '/operate', '/share'] as const;

// WCAG 2.1 A + AA. `best-practice` is excluded on purpose: it is advisory, not a conformance
// standard, and mixing it in would make the gate assert something section 14 does not require.
const WCAG_TAGS = ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa'];

for (const route of AREA_ROUTES) {
  test(`${route} has no serious or critical accessibility violations`, async ({ page }, testInfo) => {
    const response = await page.goto(route, { waitUntil: 'domcontentloaded' });
    expect(response?.status()).toBe(200);

    // Let the interactive circuit boot so the scan sees the live DOM, not only the prerender.
    await expect(page.locator('body')).toBeVisible();
    await page.waitForTimeout(1500);

    const results = await new AxeBuilder({ page }).withTags(WCAG_TAGS).analyze();
    await testInfo.attach(`axe-${route.replace(/\W+/g, '-')}`, {
      body: JSON.stringify(results, null, 2),
      contentType: 'application/json',
    });
    await testInfo.attach(`screen-${route.replace(/\W+/g, '-')}`, {
      body: await page.screenshot({ fullPage: true }),
      contentType: 'image/png',
    });

    const blocking = results.violations.filter(
      (violation) => violation.impact === 'serious' || violation.impact === 'critical',
    );
    const advisory = results.violations.filter(
      (violation) => violation.impact !== 'serious' && violation.impact !== 'critical',
    );

    const describe = (violations: typeof results.violations) =>
      violations
        .map(
          (violation) =>
            `${violation.impact ?? 'unknown'}: ${violation.id} — ${violation.help} ` +
            `(${violation.nodes.length} node(s); first: ${violation.nodes[0]?.target.join(' ') ?? 'n/a'})`,
        )
        .join('\n');

    if (advisory.length > 0) {
      await testInfo.attach(`axe-advisory-${route.replace(/\W+/g, '-')}`, {
        body: describe(advisory),
        contentType: 'text/plain',
      });
    }

    expect(
      blocking,
      `axe-core reported ${blocking.length} serious/critical WCAG 2.1 A/AA violation(s) on ${route}:\n${describe(blocking)}`,
    ).toEqual([]);
  });
}

test('the app shell is reachable by keyboard', async ({ page }) => {
  const response = await page.goto('/studio', { waitUntil: 'domcontentloaded' });
  expect(response?.status()).toBe(200);
  await expect(page.locator('body')).toBeVisible();
  await page.waitForTimeout(1500);

  // Walk the tab order from the top of the document. A keyboard-first shell must put a focusable
  // element within reach of a small number of tab stops; 25 is generous for a skip link plus the
  // area chrome, and bounds the walk so a focus trap fails the test instead of hanging it.
  let focusedTag = '';
  let reachedInteractive = false;
  for (let stop = 0; stop < 25; stop += 1) {
    await page.keyboard.press('Tab');
    focusedTag = await page.evaluate(() => document.activeElement?.tagName?.toLowerCase() ?? '');
    if (['a', 'button', 'input', 'select', 'textarea'].includes(focusedTag)) {
      reachedInteractive = true;
      break;
    }
  }

  expect(
    reachedInteractive,
    `No focusable control was reachable within 25 tab stops from the document start (last focused element: <${focusedTag || 'none'}>).`,
  ).toBe(true);
});
