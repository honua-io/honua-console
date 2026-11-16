import type { Page } from '@playwright/test';

export async function signInWithFixturePassword(page: Page, origin: string, returnTo: string) {
  await page.goto(`${origin}/console/auth/signin?returnTo=${encodeURIComponent(returnTo)}`);
  await page.getByLabel('Password', { exact: true }).fill('fixture-password');
  await page.getByRole('button', { name: 'Continue', exact: true }).click();
}
