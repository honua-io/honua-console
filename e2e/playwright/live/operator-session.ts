import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { expect, type Page } from '@playwright/test';

// Governed operator sign-in for the live suite.
//
// The Console host strips X-API-Key and will not forward a shared admin key for a browser
// operator (ConsoleOperatorCredentialHandler). Privileged UI calls succeed only after the
// server-session BFF exchanges a real IdP authorization code for a server-bound operator
// bearer. This is the same shared-origin flow the live-auth proof drives:
//   /auth/login (Development console operator) ->
//   /auth/server/login -> IdP -> /admin/auth/callback -> return path.
//
// The IdP, the server OIDC registration, and PUBLIC_BASE_URL = this Console origin are
// provided by the harness. Defaults match e2e/playwright/live-auth's local realm so a
// harness that stands that topology up does not need extra wiring. Nothing here falls
// back to the admin key.

const liveDir = path.dirname(fileURLToPath(import.meta.url));

/** Cookie that marks a context whose Console operator already completed the IdP exchange. */
export const operatorMarkerName = 'honua.e2e.operator';

/** storageState captured once per run and reused by every live spec. */
export const operatorStoragePath = path.join(liveDir, '.auth', 'operator.json');

export interface OperatorCallbackEvidence {
  status: number;
  location: string | null;
  url: string;
}

export function idpHost(): string {
  return process.env.HONUA_CONSOLE_E2E_IDP_HOST ?? 'host.docker.internal:8443';
}

export function idpHostname(): string {
  return idpHost().replace(/:\d+$/, '');
}

export async function hasGovernedOperatorSession(page: Page): Promise<boolean> {
  const cookies = await page.context().cookies();
  return cookies.some((cookie) => cookie.name === operatorMarkerName && cookie.value === '1');
}

/**
 * Completes the governed server/Console session on this page when the context does not
 * already carry one. Returns the Console-origin callback evidence for a fresh exchange,
 * or null when the shared storage state was already applied.
 */
export async function establishGovernedOperatorSession(page: Page): Promise<OperatorCallbackEvidence | null> {
  if (await hasGovernedOperatorSession(page)) {
    return null;
  }

  const user = process.env.HONUA_CONSOLE_E2E_OPERATOR_USER ?? 'alice';
  const password = process.env.HONUA_CONSOLE_E2E_OPERATOR_PASSWORD ?? 'alice-live-proof-pw';
  const profileId = process.env.HONUA_CONSOLE_E2E_OPERATOR_PROFILE ?? 'local-dev';
  const hostPattern = new RegExp(idpHost().replace(/[.*+?^${}()|[\]\\]/g, '\\$&'));
  const returnTo = '/operate/connections';
  const callbacks: OperatorCallbackEvidence[] = [];

  page.on('response', (response) => {
    if (new URL(response.url()).pathname === '/admin/auth/callback') {
      callbacks.push({
        status: response.status(),
        location: response.headers()['location'] ?? null,
        url: response.url(),
      });
    }
  });

  // Development console identity. This does not grant a server bearer; the handler still
  // rejects privileged calls until the exchange below finishes.
  await page.goto('/auth/login');
  await page.waitForURL(/\/$/);

  await page.goto(
    `/auth/server/login?profileId=${encodeURIComponent(profileId)}&returnTo=${encodeURIComponent(returnTo)}`,
  );
  await page.waitForURL(hostPattern);
  await page.locator('#username').fill(user);
  await page.locator('#password').fill(password);
  await page.locator('#kc-login').click();
  await page.waitForURL(`**${returnTo}`);

  expect(callbacks, 'the IdP must return to the Console-origin callback').toHaveLength(1);
  expect(callbacks[0].status, 'the BFF finishes the code exchange with a redirect').toBe(302);
  expect(callbacks[0].location).toBe(returnTo);
  // The callback URL carries the one-time code. Keep the evidence file free of it.
  const evidence: OperatorCallbackEvidence = {
    status: callbacks[0].status,
    location: callbacks[0].location,
    url: new URL(callbacks[0].url).pathname,
  };

  // A live read. Fail-closed surfaces render "Operate binding" / the reauth copy. A
  // server-bound bearer renders the inventory (empty or not). No admin key is configured
  // on the Console host, so this state is reachable only through the exchanged bearer.
  await expect(page.getByRole('heading', { name: 'Data Connections' })).toBeVisible({ timeout: 20_000 });
  await expect(page.getByText('Sign in to honua-server again')).toHaveCount(0);
  await expect(page.getByText('Operate binding')).toHaveCount(0);
  await expect(
    page.getByText('No connections are available in this Operate environment.')
      .or(page.locator('table.console-table')),
  ).toBeVisible();

  const origin = new URL(page.url()).origin;
  await page.context().addCookies([
    {
      name: operatorMarkerName,
      value: '1',
      url: origin,
      httpOnly: false,
      sameSite: 'Lax',
    },
  ]);
  return evidence;
}

export function writeOperatorCallbackEvidence(evidence: OperatorCallbackEvidence): void {
  const dir = path.join(liveDir, '..', 'test-results');
  fs.mkdirSync(dir, { recursive: true });
  fs.writeFileSync(path.join(dir, 'operator-session-callback.json'), JSON.stringify(evidence, null, 2));
}
