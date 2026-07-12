import { test, expect } from '@playwright/test';

// Spec 5: horizontal-overflow regression guard (#310).
//
// At 1440x900 (mainstream desktop) the page body must never scroll horizontally:
// every wide block (dense tables, stat-tile rows, multi-pane authoring shells, long
// prose/code in cards) has to scroll inside its own container or reflow at a
// breakpoint, never push the document past the viewport. The UCD review captured
// systemic right-edge clipping across the operator surfaces because the page-wrapper
// primitives sized themselves against 100vw (ignoring the 280px nav rail + main
// padding) instead of their content column.
//
// The invariant is width-only, so it holds in the host's backend-free / missing-binding
// mode (Program.cs renders every page's unbound state; the app dev-auto-logins in
// Development). We assert documentElement.scrollWidth never exceeds the viewport by more
// than 1px (sub-pixel rounding) on each affected route.

// 1440x900: the primary width the UCD review flagged. 1366x768 is the narrower mainstream
// laptop width the acceptance criteria also calls out; guarding it too keeps the wide
// operator shells honest as they reflow.
const VIEWPORTS = [
  { width: 1440, height: 900 },
  { width: 1366, height: 768 },
] as const;

// Every surface the review found clipped, plus the sibling operator pages that share the
// same .console-operate-page / .studio-page wrappers (so a regression in the shared
// primitive is caught wherever it surfaces first).
const ROUTES = [
  '/inbox',
  '/operate/health',
  '/operate/observability',
  '/operate/copilot',
  '/operate/deploy',
  '/operate/metrics',
  '/operate/access',
  '/studio',
  '/studio/drafts',
] as const;

for (const viewport of VIEWPORTS) {
  for (const route of ROUTES) {
    test(`no horizontal overflow at ${viewport.width}x${viewport.height} on ${route}`, async ({
      page,
    }) => {
      await page.setViewportSize(viewport);

      const response = await page.goto(route, { waitUntil: 'domcontentloaded' });
      expect(response?.status(), `${route} should return 200`).toBe(200);

      // Let the interactive circuit boot and the unbound states settle before measuring.
      await expect(page.locator('main.console-main')).toBeVisible();
      await page.waitForTimeout(750);

      const metrics = await page.evaluate(() => ({
        scrollWidth: document.documentElement.scrollWidth,
        innerWidth: window.innerWidth,
      }));

      // Allow 1px of sub-pixel rounding slack; anything larger is real clipped content.
      expect(
        metrics.scrollWidth,
        `${route} @ ${viewport.width}px: documentElement.scrollWidth (${metrics.scrollWidth}) ` +
          `must not exceed viewport innerWidth (${metrics.innerWidth}); the page body is ` +
          `scrolling horizontally and right-edge content is clipped.`,
      ).toBeLessThanOrEqual(metrics.innerWidth + 1);
    });
  }
}
