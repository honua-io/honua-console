import { expect, type Page } from '@playwright/test';
import { readFileSync } from 'node:fs';

export function shippingPassword(): string {
  const passwordFile = process.env.HONUA_SHIPPING_PASSWORD_FILE;
  if (!passwordFile) throw new Error('HONUA_SHIPPING_PASSWORD_FILE is required');
  const password = readFileSync(passwordFile, 'utf8').trim();
  if (!password) throw new Error('The quickstart password file is empty');
  return password;
}

export async function signIn(page: Page, password: string) {
  await page.goto('/console/auth/signin');
  const passwordInput = page.locator('input[type="password"]');
  await expect(passwordInput).toBeVisible();
  const signInResponse = page.waitForResponse(response =>
    new URL(response.url()).pathname === '/api/v1/admin/auth/session'
    && response.request().method() === 'POST');
  await passwordInput.fill(password);
  await page.getByRole('button', { name: 'Continue', exact: true }).click();
  expect((await signInResponse).status()).toBe(204);
  await expect(page).not.toHaveURL(/\/auth\/signin/);
  await expect(passwordInput).toHaveCount(0);
}
