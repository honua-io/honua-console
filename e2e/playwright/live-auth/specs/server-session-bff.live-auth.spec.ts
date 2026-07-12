import { test, expect, request as pwRequest, type Page, type BrowserContext } from '@playwright/test';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const specDir = path.dirname(fileURLToPath(import.meta.url));

// Deploy proof for the operator-partitioned server session BFF (#303 / PR #305).
//
// Topology under test (all REAL, no simulated upstream):
//   Console (stock Honua.Console.Web, http://127.0.0.1:5274)
//     -> honua-server (pinned nightly image, http://127.0.0.1:5281, NO dev-auth bypass)
//     -> Keycloak 26 (real OIDC IdP, https://host.docker.internal:8443, PKCE + client secret)
//   with honua-server PUBLIC_BASE_URL = the CONSOLE origin, so the OIDC redirect_uri the
//   server registers/builds is the Console's /admin/auth/callback (shared-origin routing).
//
// What is proven live:
//   1. /auth/server/login redirects the authenticated Console operator to the real IdP;
//      the IdP redirects back to the CONSOLE-origin callback; the BFF finishes the server
//      token exchange + bearer mint; the operator lands on the requested admin page and it
//      renders a LIVE admin read (impossible without the bearer — no admin key is
//      configured and /api/v1/admin/connections 401s anonymous callers).
//   2. The one-time callback state is bound to the initiating operator: a second operator
//      (trusted-edge identity) and an anonymous caller cannot consume a REAL IdP-issued
//      code+state; the owner still can, exactly once.
//   3. A second operator does NOT inherit the first operator's server session, completes
//      their OWN flow as a DIFFERENT IdP user, and the first operator stays untouched.
//   4. Console sign-out revokes the server-bound session (the admin surface fails closed
//      again on the next entry).

// NOTE: both IdP users carry the realm role "admin" so their bearers can read the
// admin connections surface. When bob had only the "user" role, his completed flow
// yielded a bearer the server DENIED with "Ops-reader authorization denied ...
// principal lacks an admin or ops:read grant" — live confirmation that the exchanged
// bearer carries per-user claims and honua-server enforces per-principal RBAC on it.
const KC_HOST = 'host.docker.internal:8443';
const ALICE = { user: 'alice', password: 'alice-live-proof-pw' };
const BOB = { user: 'bob', password: 'bob-live-proof-pw' };
const EDGE_HEADERS = {
  'X-Honua-Edge-Auth': 'live-proof-edge-secret',
  'X-Forwarded-User': 'bob-operator',
  'X-Forwarded-Email': 'bob@live-proof.honua.test',
};

const EVIDENCE_DIR = path.join(specDir, '..', 'evidence');
fs.mkdirSync(EVIDENCE_DIR, { recursive: true });
const flowLog: Array<Record<string, unknown>> = [];
function log(entry: Record<string, unknown>): void {
  flowLog.push({ at: new Date().toISOString(), ...entry });
}

test.afterAll(async () => {
  fs.writeFileSync(
    path.join(EVIDENCE_DIR, 'flow-log.json'),
    JSON.stringify(flowLog, null, 2),
  );
});

async function devLogin(page: Page): Promise<void> {
  await page.goto('/auth/login');
  await page.waitForURL('**/');
}

async function submitKeycloakLogin(page: Page, creds: { user: string; password: string }): Promise<void> {
  await page.waitForURL(new RegExp(KC_HOST.replace('.', '\\.')));
  await page.locator('#username').fill(creds.user);
  await page.locator('#password').fill(creds.password);
  await page.locator('#kc-login').click();
}

// Operator B's HTTP-only observable: the PRERENDERED HTML of an admin surface fetched
// with B's trusted-edge identity. It reflects B's OWN partition (live data vs the
// fail-closed Operate binding state) without needing an interactive circuit.
async function fetchConnectionsHtmlAsBob(baseURL: string): Promise<string> {
  const ctx = await pwRequest.newContext({ extraHTTPHeaders: EDGE_HEADERS });
  const response = await ctx.get(`${baseURL}/operate/connections`);
  const html = await response.text();
  await ctx.dispose();
  return html;
}

test.describe.serial('server session BFF · live shared-origin OIDC deploy proof', () => {
  test('operator A completes the real IdP flow and the admin surface renders live server data over the bearer', async ({ page, baseURL }) => {
    await devLogin(page);

    // BEFORE server sign-in: the connections read must fail closed (no admin key is
    // configured, the account session only holds the non-forwardable sentinel, and the
    // server 401s the anonymous /api/v1/admin/connections read), so the surface renders
    // the Operate binding capability state instead of data.
    await page.goto('/operate/connections');
    await expect(page.getByRole('heading', { name: 'Data Connections' })).toBeVisible();
    await expect(page.getByText('Operate binding')).toBeVisible();
    await expect(page.getByText('No connections are available')).toHaveCount(0);
    await page.screenshot({ path: path.join(EVIDENCE_DIR, '01-before-server-signin-fail-closed.png'), fullPage: true });
    log({ step: 'before-server-signin', operator: 'dev-operator', surface: 'fail-closed (no bearer, no admin key)' });

    // Record the shared-origin callback exchange as it happens.
    const callbackResponses: Array<{ url: string; status: number; location: string | null }> = [];
    page.on('response', (response) => {
      if (response.url().startsWith(`${baseURL}/admin/auth/callback`)) {
        callbackResponses.push({
          url: response.url(),
          status: response.status(),
          location: response.headers()['location'] ?? null,
        });
      }
    });

    // Begin the server sign-in: single provider -> straight to the real IdP.
    await page.goto('/auth/server/login?profileId=local-dev&returnTo=%2Foperate%2Fconnections');
    await submitKeycloakLogin(page, ALICE);

    // The IdP must land on the CONSOLE-origin callback, which finishes the server-side
    // code exchange + bearer mint and redirects to the requested page.
    await page.waitForURL('**/operate/connections');
    expect(callbackResponses.length).toBeGreaterThan(0);
    expect(callbackResponses[0].status).toBe(302);
    expect(callbackResponses[0].location).toBe('/operate/connections');
    log({
      step: 'shared-origin-callback',
      operator: 'dev-operator',
      idpUser: ALICE.user,
      callback: callbackResponses[0],
    });

    // AFTER: the live connections read succeeds. /api/v1/admin/connections 401s anonymous
    // callers and no admin key is configured, so this state is only reachable over the
    // exchanged operator bearer (a clean empty inventory on a fresh server).
    await expect(page.getByText('No connections are available')).toBeVisible();
    await expect(page.getByText('Operate binding')).toHaveCount(0);
    await page.screenshot({ path: path.join(EVIDENCE_DIR, '02-after-server-signin-live-connections.png'), fullPage: true });
    log({ step: 'after-server-signin', operator: 'dev-operator', surface: 'live admin connections read over bearer' });
  });

  test('a REAL IdP-issued callback is operator-bound and one-time', async ({ page }) => {
    await devLogin(page);

    // Capture — WITHOUT delivering — a genuine IdP-issued code+state for operator A by
    // driving the begin + IdP login over plain HTTP (server redirects cannot be
    // intercepted mid-chain in the browser). page.request shares operator A's Console
    // cookies. Node does not get the browser's host-resolver pin, so the IdP host is
    // reached via its localhost-published port (same Keycloak instance and TLS cert).
    const begin = await page.request.get(
      '/auth/server/login?profileId=local-dev&returnTo=%2Foperate%2Fconnections',
      { maxRedirects: 0 },
    );
    expect(begin.status()).toBe(302);
    const authorizeUrl = begin.headers()['location']!.replace(KC_HOST, 'localhost:8443');
    expect(authorizeUrl).toContain('localhost:8443/realms/honua');

    const loginPage = await page.request.get(authorizeUrl, { ignoreHTTPSErrors: true });
    const formAction = /action="([^"]+)"/.exec(await loginPage.text())![1]
      .replace(/&amp;/g, '&')
      .replace(KC_HOST, 'localhost:8443');
    const submitted = await page.request.post(formAction, {
      form: { username: ALICE.user, password: ALICE.password, credentialId: '' },
      maxRedirects: 0,
      ignoreHTTPSErrors: true,
    });
    expect(submitted.status()).toBe(302);
    const callbackUrl = submitted.headers()['location']!;
    expect(callbackUrl).toContain('/admin/auth/callback');
    expect(callbackUrl).toContain('code=');
    log({ step: 'captured-real-callback', callbackUrl });

    // (a) Anonymous caller: fail-closed before any BFF logic (auth challenge).
    const anonymous = await pwRequest.newContext();
    const anonymousResponse = await anonymous.get(callbackUrl, { maxRedirects: 0 });
    expect(anonymousResponse.status()).toBe(302);
    expect(anonymousResponse.headers()['location']).toContain('/auth/login');
    log({ step: 'anonymous-replay', status: anonymousResponse.status(), location: anonymousResponse.headers()['location'] });
    await anonymous.dispose();

    // (b) A DIFFERENT authenticated operator cannot consume (or invalidate) the owner's
    // pending flow, even with the genuine code+state.
    const bobEdge = await pwRequest.newContext({ extraHTTPHeaders: EDGE_HEADERS });
    const crossOperator = await bobEdge.get(callbackUrl, { maxRedirects: 0 });
    expect(crossOperator.status()).toBe(302);
    expect(crossOperator.headers()['location']).toContain('/auth/signin');
    expect(crossOperator.headers()['location']).toContain('serverAuth=denied');
    log({ step: 'cross-operator-replay', operator: 'bob-operator', status: crossOperator.status(), location: crossOperator.headers()['location'] });
    await bobEdge.dispose();

    // (c) The owner consumes it successfully — the cross-operator attempt did not burn it.
    const ownerFirst = await page.request.get(callbackUrl, { maxRedirects: 0 });
    expect(ownerFirst.status()).toBe(302);
    expect(ownerFirst.headers()['location']).toBe('/operate/connections');
    log({ step: 'owner-consume', status: ownerFirst.status(), location: ownerFirst.headers()['location'] });

    // (d) ... exactly once: an owner replay of the same real code+state is denied.
    const ownerReplay = await page.request.get(callbackUrl, { maxRedirects: 0 });
    expect(ownerReplay.status()).toBe(302);
    expect(ownerReplay.headers()['location']).toContain('serverAuth=denied');
    log({ step: 'owner-replay-denied', status: ownerReplay.status(), location: ownerReplay.headers()['location'] });
  });

  test('a second operator does NOT inherit the first operator session and completes their OWN real IdP flow', async ({ browser, baseURL }) => {
    // Operator A: sign in and complete the live flow (each Console dev sign-in re-binds
    // the account session to a fresh sentinel, so A re-runs the IdP round-trip here).
    const contextA = await browser.newContext();
    const pageA = await contextA.newPage();
    await devLogin(pageA);
    await pageA.goto('/auth/server/login?profileId=local-dev&returnTo=%2Foperate%2Fconnections');
    await submitKeycloakLogin(pageA, ALICE);
    await pageA.waitForURL('**/operate/connections');
    await expect(pageA.getByText('No connections are available')).toBeVisible();
    await expect(pageA.getByText('Operate binding')).toHaveCount(0);

    // BEFORE operator B signs in upstream: B's partition must NOT see A's session —
    // B's prerendered admin surface still renders the fail-closed Operate binding state.
    const beforeHtml = await fetchConnectionsHtmlAsBob(baseURL!);
    expect(beforeHtml).toContain('Operate binding');
    expect(beforeHtml).not.toContain('No connections are available');
    log({ step: 'operator-b-before-own-flow', operator: 'bob-operator', surface: 'fail-closed (does not inherit operator A bearer)' });

    // Operator B: trusted-edge identity (distinct partition), signs in at the IdP as BOB.
    const contextB: BrowserContext = await browser.newContext({ extraHTTPHeaders: EDGE_HEADERS });
    const pageB = await contextB.newPage();
    const callbackResponsesB: Array<{ status: number; location: string | null }> = [];
    pageB.on('response', (response) => {
      if (response.url().startsWith(`${baseURL}/admin/auth/callback`)) {
        callbackResponsesB.push({ status: response.status(), location: response.headers()['location'] ?? null });
      }
    });
    await pageB.goto('/auth/server/login?profileId=local-dev&returnTo=%2F');
    await submitKeycloakLogin(pageB, BOB);
    await pageB.waitForURL('**/');
    expect(callbackResponsesB.length).toBeGreaterThan(0);
    expect(callbackResponsesB[0].status).toBe(302);
    expect(callbackResponsesB[0].location).toBe('/');
    log({ step: 'operator-b-own-flow', operator: 'bob-operator', idpUser: BOB.user, callback: callbackResponsesB[0] });

    // KNOWN LIMITATION (reported on the PR, not asserted as success here): operator B's
    // exchanged bearer is stored in B's partition, but B is a trusted-EDGE operator and
    // ConsoleEdgeIdentityMiddleware re-runs ConsoleOperatorSessionBridge.SyncAsync on every
    // edge request; with no X-Forwarded-Access-Token that re-sync overwrites the stored
    // session with the sentinel, so an edge operator cannot RETAIN a BFF-exchanged bearer
    // across requests. The wire-level proof above (IdP login as bob -> shared-origin
    // callback -> 302) plus the server-side token/bearer log lines are B's flow evidence.
    // Cookie-authenticated operators (the stock browser sign-in) retain the bearer — that
    // path is proven live by operator A throughout this suite.

    // Operator A's live session is untouched by B's sign-in.
    await pageA.reload();
    await expect(pageA.getByText('No connections are available')).toBeVisible();
    await expect(pageA.getByText('Operate binding')).toHaveCount(0);
    await pageA.screenshot({ path: path.join(EVIDENCE_DIR, '03-operator-a-unaffected-by-b.png'), fullPage: true });
    log({ step: 'operator-a-unaffected', surface: 'still live' });

    await contextB.close();
    await contextA.close();
  });

  test('sign-out revokes the operator server-bound session and the next entry fails closed', async ({ page }) => {
    // Operator A: fresh sign-in + live IdP round-trip (dev sign-in re-binds the account
    // session, so the bearer observed below is freshly minted in THIS test).
    await devLogin(page);
    await page.goto('/auth/server/login?profileId=local-dev&returnTo=%2Foperate%2Fconnections');
    await submitKeycloakLogin(page, ALICE);
    await page.waitForURL('**/operate/connections');
    await expect(page.getByText('No connections are available')).toBeVisible();

    // Sign out: the endpoint runs the BFF SignOutAsync (revokes the upstream server
    // session, erases the operator's bearers and cookie jars) then clears the cookie.
    const logout = await page.request.post('/auth/logout', { maxRedirects: 0 });
    expect(logout.status()).toBe(302);
    expect(logout.headers()['location']).toContain('/auth/login');
    log({ step: 'operator-a-logout', status: logout.status(), location: logout.headers()['location'] });

    // Re-entering finds the server-bound session gone: the admin surface fails closed
    // until a new IdP round-trip. (Per-operator scoping of the erasure is regression-
    // locked by the in-repo SignOut_ClearsOnlyCurrentOperators... integration test; the
    // live observable here is the fail-closed surface after sign-out.)
    await devLogin(page);
    await page.goto('/operate/connections');
    await expect(page.getByText('Operate binding')).toBeVisible();
    await expect(page.getByText('No connections are available')).toHaveCount(0);
    await page.screenshot({ path: path.join(EVIDENCE_DIR, '04-after-signout-fail-closed.png'), fullPage: true });
    log({ step: 'operator-a-after-signout', surface: 'fail-closed (server session erased)' });
  });
});
