import { test, expect } from '@playwright/test';

// Spec 1: the Blazor Web app shell boots in a real browser, the four-area chrome prerenders,
// and the interactive-server framework script is wired. This is the browser boot bUnit never
// exercises (bUnit renders components server-side in isolation and never builds the live circuit).
//
// This smoke exists to catch real boot failures the unit suites cannot see: failed asset loads,
// 404s, transport teardown, and a re-introduced ambiguous route. It therefore asserts that booting
// /studio and letting the interactive circuit start produces NO console or page errors.
//
// History: this assertion was previously gated by a KNOWN_CIRCUIT_ERROR whitelist that tolerated an
// ambiguous `/operate/jobs/{...}` route which crashed the interactive circuit (two components
// registered the same template). That defect was fixed in #140 — OperateJobPage was removed,
// OperateObservabilityPage is the sole owner of `/operate/jobs/{SelectedJobRunId}`, and
// ShellRouteUniquenessTests now guards route uniqueness. With the defect gone, the whitelist only
// blinded this smoke to exactly the failures it exists to catch, so it has been removed. Observed
// errors are still recorded as a test annotation (visible in the HTML report) for diagnosis.

test('app shell prerenders the four-area chrome and wires the Blazor framework', async ({ page }, testInfo) => {
  const observed: string[] = [];
  page.on('console', (msg) => {
    if (msg.type() === 'error') {
      observed.push(`console: ${msg.text()}`);
    }
  });
  page.on('pageerror', (err) => observed.push(`pageerror: ${err.message}`));

  const response = await page.goto('/studio', { waitUntil: 'domcontentloaded' });
  expect(response?.status()).toBe(200);

  // The shell prerendered (server-rendered HTML is present and visible).
  await expect(page.locator('body')).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Studio', exact: true }).first()).toBeVisible();

  // The Blazor Web framework script is wired — the browser-side runtime bUnit cannot cover.
  await expect(page.locator('script[src*="_framework/blazor.web"]')).toHaveCount(1);

  // Let the interactive circuit boot so any startup console output is captured.
  await page.waitForTimeout(1500);

  if (observed.length > 0) {
    testInfo.annotations.push({
      type: 'console-errors',
      description: observed.join('\n'),
    });
  }

  // A healthy boot emits no console or page errors. Fail on any — a real asset 404, transport
  // teardown, or a re-introduced ambiguous route must surface here, not be silently tolerated.
  expect(observed, `console/page errors on load:\n${observed.join('\n')}`).toEqual([]);
});
