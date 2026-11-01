import AxeBuilder from '@axe-core/playwright';
import { expect, request, type Locator, type Page, type TestInfo } from '@playwright/test';

export const serverUrl = process.env.HONUA_CONSOLE_E2E_SERVER_URL ?? 'http://127.0.0.1:8088';
export type Session = { authenticated: boolean; principalId: string; environmentId: string; csrfToken: string; expiresAt: string };
export type Draft = { draftId: string; itemId: string; ownerId: string; createdBy: string; envelope: { body: any } };

/** Resolve the same subject again if Blazor replaces prerendered markup while scrolling. */
export async function scrollForMilestone(subject: Locator) {
  await expect(() => subject.scrollIntoViewIfNeeded()).toPass({ timeout: 15_000 });
}

/** Observe the original lifecycle calls without replaying or changing their outcome. */
export async function observeLifecycle(page: Page) {
  await page.locator('honua-studio-app').evaluate(element => {
    const app = element as any;
    const observations = ((window as any).__studioLifecycleObservations ??= []);
    for (const method of ['createRecipeDraft', 'saveWorkspaceVersion', 'reopenWorkspaceVersion']) {
      const original = app[method];
      if (original.__lifecycleObserved) continue;
      app[method] = async function (...args: unknown[]) {
        const auth = app.auth, expiresAt = app.session?.expiresAt;
        try { return await original.apply(app, args); }
        catch (error) {
          const causes: Array<{ name: string; message: string }> = [];
          for (let cause: any = error; cause && causes.length < 4; cause = cause.cause)
            causes.push({ name: String(cause.name), message: String(cause.message).replace(/\?[^\s)]+/g, '?[redacted]').slice(0, 1000) });
          observations.push({ method, causes, expiresAt, currentExpiry: app.session?.expiresAt,
            sameAuth: auth === app.auth, authStatus: auth.getState().status });
          throw error;
        }
      };
      app[method].__lifecycleObserved = true;
    }
  });
}

/** Password entry and authorization codes must never enter uploaded traces or screenshots. */
export async function signIn(page: Page, user = 'customer', returnTo = '/studio') {
  page.setDefaultNavigationTimeout(30_000);
  page.setDefaultTimeout(20_000);
  await page.goto('/auth/login?returnTo=/version.json');
  await page.goto(`/auth/server/login?profileId=local-dev&returnTo=${encodeURIComponent(returnTo)}`);
  await page.waitForURL(/:8443\//);
  await page.locator('#username').fill(user);
  await page.locator('#password').fill(`${user}-live-proof-pw`);
  const callback = page.waitForResponse(response => new URL(response.url()).pathname === '/admin/auth/callback');
  await page.locator('#kc-login').click();
  expect((await callback).status(), 'real OIDC code exchange').toBe(302);
  await page.waitForURL(`**${returnTo}`);
  const response = await page.request.get('/api/agent-workspace/session');
  expect(response.status(), 'customer must receive a real server-bound session').toBe(200);
  const session: Session = await response.json();
  expect(session.authenticated).toBe(true);
  return session;
}

export function headers(session: Session) {
  return { 'X-Honua-Environment': session.environmentId, 'X-Honua-Session-Partition': session.principalId,
    'X-CSRF-Token': session.csrfToken };
}

export async function read(page: Page, session: Session, path: string) {
  const response = await page.request.get(path, { headers: headers(session) });
  expect(response.status(), `real read ${path.split('?')[0]}`).toBe(200);
  const body = await response.json();
  return body.data ?? body;
}

export async function identity(page: Page): Promise<{ draftId: string; packageKey: string } | null> {
  return page.locator('honua-studio-app').evaluate(element =>
    (element as HTMLElement & { workspaceDraftIdentity?: { draftId: string; packageKey: string } }).workspaceDraftIdentity ?? null);
}

/** No broad exception list: even failed optional widgets are customer-visible failures. */
export function observeHealth(page: Page) {
  const origins = new Set([new URL(serverUrl).origin, `http://127.0.0.1:${process.env.HONUA_CONSOLE_E2E_LIVE_PORT ?? '5274'}`]);
  const failures: string[] = [];
  const successfulReads: string[] = [];
  const failedWidgetReads = new Set<string>();
  const cancelledReads: string[] = [];
  const consoleDetails: Array<{ location: string; errors: unknown[] }> = [];
  const pendingConsoleDetails: Promise<void>[] = [];
  page.on('response', response => {
    const url = new URL(response.url());
    if (!origins.has(url.origin)) return;
    // Deliberately omit queries: authorization codes and embed tokens are not evidence.
    const observation = `${response.status()} ${response.request().method()} ${url.origin}${url.pathname}`;
    if (response.status() >= 400) failures.push(observation);
    if (response.ok() && /\/api\/studio\/data\//.test(url.pathname)) successfulReads.push(observation);
  });
  page.on('requestfailed', request => {
    const url = new URL(request.url());
    if (request.failure()?.errorText === 'net::ERR_ABORTED') {
      cancelledReads.push(`${url.origin}${url.pathname}`);
      return;
    }
    if (request.method() === 'GET' && origins.has(url.origin) && /^\/rest\/services\/.+\/FeatureServer\/\d+\/query$/.test(url.pathname)) failedWidgetReads.add(request.url());
    if (origins.has(url.origin))
      failures.push(`request failed ${url.origin}${url.pathname}: ${request.failure()?.errorText}`);
  });
  page.on('pageerror', error => {
    failures.push(`pageerror: ${error.message}`);
    // Keep the stack for diagnosis without retaining URL queries, which may carry credentials.
    consoleDetails.push({ location: 'pageerror', errors: [{ name: error.name, message: error.message,
      stack: error.stack?.replace(/\?[^\s)]+/g, '?[redacted]') }] });
  });
  page.on('console', message => {
    if (message.type() !== 'error') return;
    failures.push(`console: ${message.text()}`);
    const location = message.location();
    const source = location.url ? `${location.url.split('?')[0]}:${location.lineNumber}` : '';
    pendingConsoleDetails.push(Promise.all(message.args().map(argument => argument.evaluate(value =>
      value instanceof Error ? { name: value.name, message: value.message } : null).catch(() => null)))
      .then(errors => { consoleDetails.push({ location: source, errors: errors.filter(Boolean) }); }));
  });
  return {
    successfulReads,
    async verify(info: TestInfo) {
      await Promise.all(pendingConsoleDetails);
      const independentStatus = [];
      for (const url of failedWidgetReads) {
        const response = await page.request.get(url, { maxRedirects: 0 });
        independentStatus.push({ path: new URL(url).pathname, status: response.status() });
      }
      await info.attach('studio-network-health.json', { contentType: 'application/json',
        body: Buffer.from(JSON.stringify({ failures, successfulReads, independentStatus, cancelledReads, consoleDetails }, null, 2)) });
      expect.soft(failures, 'no Console, Studio or server HTTP failures or browser errors').toEqual([]);
    },
  };
}

export async function accessibility(page: Page, info: TestInfo, name: string) {
  const result = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa']).analyze();
  await info.attach(`axe-${name}.json`, { contentType: 'application/json', body: Buffer.from(JSON.stringify(result)) });
  expect(result.violations, `axe on ${name}, including open shadow roots and frames`).toEqual([]);
}

export async function createRecipe(page: Page, sourceId: string, packageKey: string) {
  await page.locator('honua-studio-app').evaluate(element => {
    const app = element as any;
    const tracker = { states: [String(app.auth.getState().status)], panel: [] as string[], removals: [] as string[] };
    (window as any).__recipeDiagnostic = tracker;
    app.auth.subscribe((state: { status: string }) => { if (tracker.states.length < 30) tracker.states.push(state.status); });
    let previous = '';
    const observer = new MutationObserver(records => {
      for (const record of records) for (const node of record.removedNodes) {
        if ((node === app || (node instanceof Element && node.contains(app))) && tracker.removals.length < 10) {
          const parent = record.target instanceof Element ? record.target : null;
          tracker.removals.push(`${node.nodeName}#${node instanceof Element ? node.id : ''}.${node instanceof Element ? node.className : ''} from ${parent?.tagName ?? ''}#${parent?.id ?? ''}[studio-mount=${parent?.hasAttribute('data-studio-mount') ?? false}]`);
        }
      }
      const value = `${app.isConnected}:${Boolean(app.shadowRoot?.querySelector('[data-testid="recipe-panel"]'))}`;
      if (value !== previous && tracker.panel.length < 30) { tracker.panel.push(value); previous = value; }
    });
    observer.observe(document.body, { childList: true, subtree: true });
    if (app.shadowRoot) observer.observe(app.shadowRoot, { childList: true, subtree: true });
  });
  if (!(await page.getByTestId('recipe-panel').isVisible())) {
    await page.getByTestId('recipe-toggle').focus();
    await page.keyboard.press('Enter');
  }
  await expect(page.getByTestId('recipe-panel')).toBeVisible();
  await page.getByTestId('recipe-package-key').fill(packageKey);
  try {
    await page.getByTestId('recipe-source').selectOption(sourceId);
  } catch (error) {
    const options = await page.getByTestId('recipe-source').locator('option').evaluateAll(nodes => nodes.map(node => ({ value: (node as HTMLOptionElement).value, text: node.textContent })) ).catch(() => []);
    const status = await page.getByTestId('recipe-status').textContent().catch(() => null);
    const diagnostic = await page.evaluate(() => (window as any).__recipeDiagnostic);
    throw new Error(`Recipe source ${sourceId} unavailable; choices=${JSON.stringify(options)}, status=${status}, diagnostic=${JSON.stringify(diagnostic)}`, { cause: error });
  }
  try { await expect(page.getByTestId('recipe-status')).toContainText('Verified'); }
  catch (error) { throw new Error(`Recipe verification lost: ${JSON.stringify(await page.evaluate(() => (window as any).__recipeDiagnostic))}`, { cause: error }); }
  await page.getByTestId('recipe-category').selectOption('category');
  await page.getByTestId('recipe-columns').getByLabel('name', { exact: true }).check();
  await page.getByTestId('recipe-columns').getByLabel('category', { exact: true }).check();
  await page.getByTestId('recipe-create').focus();
  await expect(page.getByTestId('recipe-create')).toBeFocused();
  await page.keyboard.press('Enter');
  for (let attempt = 0; attempt < 4; attempt++) {
    await expect(page.getByTestId('recipe-status')).toHaveText(/Created draft|The action is pending/, { timeout: 30_000 });
    if ((await page.getByTestId('recipe-status').textContent())?.includes('Created draft')) break;
    expect(attempt, 'bounded explicit recovery must complete the same recipe submission').toBeLessThan(3);
    await page.getByTestId('recipe-create').focus();
    await page.keyboard.press('Enter');
  }
  await expect(page.getByTestId('recipe-status')).toContainText('Created draft');
  await page.getByTestId('recipe-close').click();
  await expect.poll(() => identity(page).then(value => value?.packageKey)).toBe(packageKey);
}

/** Independent IdP witness; its admin token stays in memory outside the browser context. */
export async function customerIdentity() {
  const idp = await request.newContext({ baseURL: 'https://127.0.0.1:8443', ignoreHTTPSErrors: true });
  try {
    const tokenResponse = await idp.post('/realms/master/protocol/openid-connect/token', { form: {
      client_id: 'admin-cli', grant_type: 'password', username: 'admin', password: 'admin-live-proof-pw',
    } });
    expect(tokenResponse.status()).toBe(200);
    const token = (await tokenResponse.json()).access_token;
    const headers = { Authorization: `Bearer ${token}` };
    const userResponse = await idp.get('/admin/realms/honua/users?username=customer&exact=true', { headers });
    expect(userResponse.status()).toBe(200);
    const users = await userResponse.json();
    expect(users).toHaveLength(1);
    const user = users[0];
    const rolesResponse = await idp.get(`/admin/realms/honua/users/${user.id}/role-mappings/realm/composite`, { headers });
    expect(rolesResponse.status()).toBe(200);
    const roles = (await rolesResponse.json()).map((role: { name: string }) => role.name);
    expect(roles).toContain('user');
    expect(roles).toContain('admin');
    expect(roles).not.toContain('administrator');
    return { id: user.id as string, username: user.username as string };
  } finally { await idp.dispose(); }
}

export async function sourceFor(page: Page, service: string, layerId: number) {
  await expect(async () => {
    if (!(await page.getByTestId('recipe-panel').isVisible()))
      await page.getByTestId('recipe-toggle').click();
    await expect(page.getByTestId('recipe-panel')).toBeVisible({ timeout: 3000 });
  }).toPass({ timeout: 30_000 });
  await expect(page.getByTestId('recipe-source')).toBeEnabled();
  const matches = () => page.locator('honua-studio-app').evaluate((element, selected) =>
    (((element as any).sourceCatalog ?? []) as Array<{ id: string; locator?: { url?: string; layerId?: number } }>)
      .filter(entry => entry.locator?.url?.includes(selected.service) && entry.locator?.layerId === selected.layerId),
    { service, layerId });
  await expect.poll(() => matches().then(sources => sources.length), { timeout: 30_000 }).toBe(1);
  return (await matches())[0];
}

export async function paintedFeatures(page: Page, layerId: string): Promise<number> {
  return page.locator('honua-studio-canvas').evaluate((element, id) => {
    const map = (element as any).mapView?.map;
    if (!map?.getLayer(id)) return 0;
    return map.queryRenderedFeatures({ layers: [id] }).length;
  }, layerId);
}
