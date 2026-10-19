import { expect, test } from '@playwright/test';

// Exercise the mounted scheduler and actual context revision/queue logic with a
// controlled API boundary. This is not a live backend or authority qualification.
test('context polling resumes after navigation supersedes an observation', async ({ page }) => {
  await page.route(url => url.pathname === '/operate', async route => {
    const response = await route.fetch();
    const html = await response.text();
    const manualBoot = html.replace(/<script src="([^"]*_framework\/blazor\.web\.[^"]+)"/,
      '<script autostart="false" src="$1"');
    expect(manualBoot).not.toBe(html);
    await route.fulfill({ response, body: manualBoot });
  });
  await page.goto('/operate');
  const prerendered = await page.locator('[data-agent-workspace]').elementHandle();
  await page.evaluate(() => (window as any).Blazor.start());
  await expect.poll(() => prerendered!.evaluate(element => element.isConnected)).toBe(false);
  await page.evaluate(() => {
    const workspace = (window as any).honuaAgentWorkspace;
    const state = (window as any).pollingRace = { reads: 0, renewals: 0, followed: 0, skipped: false };
    let release!: () => void;
    const firstRead = new Promise<void>(resolve => { release = resolve; });
    state.release = release;
    workspace.session = { environmentId: 'environment', principalId: 'human', environmentName: 'Polling test',
      expiresAt: new Date(Date.now() + 600_000).toISOString() };
    workspace.workspaceId = 'workspace';
    workspace.context = { targetId: 'target', revision: 1, selectedResources: [], task: null };
    workspace.api = async (path: string, method = 'GET', body?: any) => {
      if (path === 'session') {
        state.renewals++;
        return { ...workspace.session, expiresAt: new Date(Date.now() + 600_000).toISOString() };
      }
      if (path.endsWith('/conversation')) return { messages: [] };
      if (path.endsWith('/context') && method === 'PUT')
        return { ...workspace.context, ...body, revision: workspace.context.revision + 1 };
      if (path.startsWith('workspace?viewId=')) {
        if (++state.reads === 1) {
          await firstRead;
          return { targetId: 'target', revision: 1, selectedResources: [], task: null };
        }
        return { ...workspace.context, revision: workspace.context.revision + 1,
          task: { kind: 'workflow-package', id: 'next-process', revision: 1, contentHash: 'a'.repeat(64) } };
      }
      throw new Error(`Unexpected controlled API read: ${path}`);
    };
    const observe = workspace.observeContext.bind(workspace);
    workspace.observeContext = async () => {
      const snapshot = await observe();
      if (!snapshot) state.skipped = true;
      return snapshot;
    };
    window.addEventListener('honua-workspace-process-task', () => { state.followed++; });
  });
  await page.waitForFunction(() => (window as any).pollingRace.reads === 1);
  await page.evaluate(async () => {
    const workspace = (window as any).honuaAgentWorkspace;
    await workspace.select([{ kind: 'layer', id: '23' }]);
    workspace.session.expiresAt = new Date(Date.now() - 1000).toISOString();
    (window as any).pollingRace.release();
  });
  await page.waitForFunction(() => (window as any).pollingRace.skipped);
  await expect.poll(() => page.evaluate(() => (window as any).pollingRace.reads)).toBeGreaterThan(1);
  await expect.poll(() => page.evaluate(() => (window as any).pollingRace.renewals)).toBe(1);
  await expect.poll(() => page.evaluate(() => (window as any).pollingRace.followed)).toBeGreaterThan(0);
});
