import type { Page } from '@playwright/test';

type ViolationWindow = { cspViolations: string[] };

/** Records every securitypolicyviolation in the page; call the returned reader after the journey. */
export async function recordCspViolations(page: Page): Promise<() => Promise<string[]>> {
  await page.addInitScript(() => {
    const violations: string[] = [];
    (window as unknown as ViolationWindow).cspViolations = violations;
    document.addEventListener('securitypolicyviolation', event =>
      violations.push(`${event.effectiveDirective} ${event.blockedURI}`));
  });
  return () => page.evaluate(() => (window as unknown as ViolationWindow).cspViolations);
}
