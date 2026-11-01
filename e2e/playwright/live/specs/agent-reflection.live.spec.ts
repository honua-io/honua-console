import { test, expect } from '@playwright/test';
import { readFileSync } from 'node:fs';
import { randomBytes } from 'node:crypto';
import { execFileSync } from 'node:child_process';
import { scrollForMilestone } from '../studio-customer';
// @ts-expect-error This shared driver is an ESM JavaScript module used by CI and Playwright.
import { exerciseAgentPublication, exerciseAgentRecovery } from '../../../../scripts/agent-customer-journey.mjs';

// @ts-expect-error Shared ESM evidence helper used by Playwright and CI.
import { captureMilestone } from '../../../../scripts/journey-gallery.mjs';

function required(name: string): string {
  const value = process.env[name];
  if (!value) throw new Error(`${name} is required for the real quickstart journey`);
  return value;
}

test('Console reflects agent changes in the customer quickstart', async ({ page }, info) => {
  const consoleUrl = required('HONUA_REFLECTION_CONSOLE_URL');
  const managerUrl = required('HONUA_REFLECTION_MANAGER_URL');
  const serverUrl = required('HONUA_REFLECTION_SERVER_URL');
  const managerProposer = required('HONUA_REFLECTION_MANAGER_PROPOSER');
  const managerApprover = required('HONUA_REFLECTION_MANAGER_APPROVER');
  const serverProposer = required('HONUA_REFLECTION_SERVER_PROPOSER');
  const serverApprover = required('HONUA_REFLECTION_SERVER_APPROVER');
  const managerActor = required('HONUA_REFLECTION_MANAGER_ACTOR');
  const serverActor = required('HONUA_REFLECTION_SERVER_ACTOR');
  const composeDir = required('HONUA_REFLECTION_COMPOSE_DIR');
  const connectionName = required('HONUA_REFLECTION_CONNECTION_NAME');
  const sshHost = process.env.HONUA_REFLECTION_SSH_HOST;
  if (!/^\/[A-Za-z0-9_./-]+$/.test(composeDir) || (sshHost && !/^[A-Za-z0-9_.-]+$/.test(sshHost)))
    throw new Error('The isolated Compose path or SSH host is invalid');
  const completed: string[] = [];
  const errors: string[] = [];
  const failures: string[] = [];
  const failureDetails: Array<{ stage: string; message: string }> = [];
  const origins = new Set([consoleUrl, managerUrl, serverUrl].map(value => new URL(value).origin));
  page.on('pageerror', error => errors.push(`pageerror: ${error.name}`));
  page.on('console', message => {
    if (message.type() === 'error') errors.push(`console error at ${message.location().url.split('?')[0]}`);
  });
  page.on('response', response => {
    const url = new URL(response.url());
    if (origins.has(url.origin) && response.status() >= 400)
      errors.push(`${response.status()} ${response.request().method()} ${url.origin}${url.pathname}`);
  });
  page.on('requestfailed', request => {
    const url = new URL(request.url());
    if (origins.has(url.origin)) errors.push(`request failed ${url.origin}${url.pathname}: ${request.failure()?.errorText}`);
  });
  const check = async (stage: string, action: () => Promise<void>) => {
    try { await action(); completed.push(stage); }
    catch (error) {
      failures.push(stage);
      const message = (error instanceof Error ? error.message : String(error)).split('\n').slice(0, 8).join('\n');
      failureDetails.push({ stage, message: message.replace(/\?[^\s)]+/g, '?[redacted]') });
    }
  };
  const go = async (path: string) => { await page.goto(new URL(path, consoleUrl).href, { waitUntil: 'domcontentloaded' }); };
  try {
    await go('/auth/login?returnTo=/version.json');
    await go('/auth/server/login?profileId=local-dev&returnTo=%2Foperate%2Fdeploy');
    await page.locator('#username').fill(required('HONUA_REFLECTION_CUSTOMER_USER'));
    await page.locator('#password').fill(required('HONUA_REFLECTION_CUSTOMER_PASSWORD'));
    await page.locator('#kc-login').click();
    await page.waitForURL('**/operate/deploy');
    let proposalId = '';
    await exerciseAgentRecovery({
      managerUrl, proposerKey: managerProposer, approverKey: managerApprover,
      stopRedis: async () => {
        const args = ['docker', 'compose', '--project-directory', composeDir, 'stop', 'redis'];
        if (sshHost) execFileSync('ssh', [sshHost, ...args], { stdio: 'ignore', timeout: 60_000 });
        else execFileSync(args[0], args.slice(1), { stdio: 'ignore', timeout: 60_000 });
      },
      observe: async (stage: string, value: any) => {
        if (stage === 'revision') await check('agent-live-revision', async () => {
          await go('/operate/deploy');
          const observed = page.locator('[data-observed-serving]');
          await expect(observed).toContainText(value.observedImageId, { timeout: 30_000 });
          await expect(observed).toContainText(value.serverVersion);
          await scrollForMilestone(observed);
          await captureMilestone(page, info, '01-live-serving-revision');
        });
        if (stage === 'finding') await check('agent-degradation-finding', async () => {
          await go('/operate/health');
          const redis = page.locator('[data-managed-target="compose-local"] [data-managed-service="redis"]');
          await expect(redis).toContainText(/unhealthy/i, { timeout: 45_000 });
          await scrollForMilestone(redis);
          await captureMilestone(page, info, '02-health-degraded');
          await go('/operate/copilot');
          await expect(page.locator('body')).toContainText('managed-compose-service-unhealthy', { timeout: 45_000 });
          await scrollForMilestone(page.getByText('managed-compose-service-unhealthy', { exact: true }).first());
          await captureMilestone(page, info, '03-finding-shown');
        });
        if (stage === 'pending') {
          proposalId = value.proposalId;
          await check('agent-audit-ownership', async () => {
            expect(proposalId).toBeTruthy(); expect(value.requestedBy).toBe(managerActor);
            await go('/inbox');
            const row = page.locator(`[data-proposal-id="manager:${proposalId}"]`).first();
            await expect(row).toContainText(/awaiting approval/i, { timeout: 45_000 });
            await expect(row).toContainText(value.requestedBy);
            const panel = page.locator('section[data-proposal-id]').filter({ hasText: `manager:${proposalId}` });
            await expect(async () => {
              if (!(await panel.isVisible())) await row.click();
              await expect(panel).toBeVisible({ timeout: 3_000 });
            }).toPass({ timeout: 30_000 });
            await scrollForMilestone(panel.locator('[data-proposal-status]'));
            await captureMilestone(page, info, '04-restart-awaiting-approval');
          });
        }
        if (stage === 'recovered') await check('agent-approval-recovery', async () => {
          const panel = page.locator('section[data-proposal-id]').filter({ hasText: `manager:${proposalId}` });
          const serverOutcome = String(value.proposal.status);
          expect(['Completed', 'Succeeded']).toContain(serverOutcome);
          await expect(panel.locator('[data-proposal-status]')).toHaveAttribute('data-proposal-status', serverOutcome, { timeout: 45_000 });
          await scrollForMilestone(panel.locator('[data-proposal-status]'));
          await captureMilestone(page, info, '05-restart-completed');
          await go('/operate/health');
          const redis = page.locator('[data-managed-target="compose-local"] [data-managed-service="redis"]');
          await expect(redis).toContainText(/\bhealthy\b/i, { timeout: 45_000 });
          await expect(redis).not.toContainText(/unhealthy/i);
          await scrollForMilestone(redis);
          await captureMilestone(page, info, '06-health-recovered');
        });
      },
    });
    const suffix = randomBytes(5).toString('hex');
    const fixture = new URL('../fixtures/', import.meta.url);
    const fields = { name: 'esriFieldTypeString', population: 'esriFieldTypeBigInteger', rating: 'esriFieldTypeDouble' };
    const seen: Array<{ dataset: string; format: string; layerId: number; geometryType: string }> = [];
    await exerciseAgentPublication({
      serverUrl, proposerKey: serverProposer, verifierKey: serverApprover, expectedActor: serverActor,
      datasets: [
        { format: 'geojson', name: `agent_regions_${suffix}`, data: readFileSync(new URL('customer-regions.geojson', fixture), 'utf8'), geometryType: 'Polygon', fields },
        { format: 'csv', name: `agent_sites_${suffix}`, data: readFileSync(new URL('customer-sites.csv', fixture), 'utf8'), geometryType: 'Point', fields },
      ],
      observe: async (_stage: string, record: any) => { seen.push(record); },
    });
    expect(seen, 'both actual agent publications must be observed').toHaveLength(2);
    expect(seen.map(record => record.format).sort()).toEqual(['csv', 'geojson']);
    await check('agent-published-services', async () => {
      for (const record of seen) {
        await go('/operate/services');
        await expect(page.locator('body')).toContainText(record.dataset, { timeout: 30_000 });
        await go('/operate/layers');
        const row = page.locator(`tr[data-service-id="${record.dataset}"][data-layer-id="${record.layerId}"]`);
        await expect(row).toContainText(record.dataset, { timeout: 30_000 });
        await expect(row).toContainText(record.format === 'csv' ? /\bPoint\b/i : /\b(?:Multi)?Polygon\b/i);
        if (record.format === 'csv') await expect(row).not.toContainText(/multipoint/i);
        await scrollForMilestone(row);
        await captureMilestone(page, info, `07-published-${record.format}-layers-geometry`);
        const detailPath = await row.locator('a').first().getAttribute('href');
        expect(detailPath, 'the published layer must link to its detail').toBeTruthy();
        await go(detailPath!);
        for (const [name, type] of Object.entries({ name: 'String', population: 'BigInteger', rating: 'Double' }))
          await expect(page.locator(`[data-field-row][data-field-name="${name}"]`).getByRole('cell', { name: type, exact: true })).toBeVisible({ timeout: 30_000 });
        await scrollForMilestone(page.locator('[data-field-row][data-field-name="rating"]'));
        await captureMilestone(page, info, `08-published-${record.format}-field-types`);
      }
      await go('/operate/connections');
      await expect(page.locator('body')).toContainText(connectionName, { timeout: 30_000 });
    });
  } finally {
    const stages = ['agent-live-revision', 'agent-degradation-finding', 'agent-approval-recovery', 'agent-published-services', 'agent-audit-ownership'];
    const qualificationComplete = stages.every(stage => completed.includes(stage)) && failures.length === 0 && errors.length === 0;
    await info.attach('agent-reflection-progress.json', { contentType: 'application/json',
      body: Buffer.from(JSON.stringify({ completed, failureDetails, qualificationComplete })) });
    expect.soft(qualificationComplete, 'every required agent journey stage must qualify').toBe(true);
    expect.soft(errors, 'no failed Console or server requests or browser errors').toEqual([]);
    expect.soft(failures, 'all agent changes must be visible in Console').toEqual([]);
  }
});
