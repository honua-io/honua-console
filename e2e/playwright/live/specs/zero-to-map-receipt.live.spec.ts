import { dirname } from 'node:path';
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import { test, expect } from '@playwright/test';
import {
  buildConsoleReceipt,
  correlationChipValue,
  readZeroToMapFacts,
  type ConsoleReceiptInputs,
} from '../zero-to-map-receipt';

const PLAN_PATH = process.env.HONUA_ZERO_TO_MAP_PLAN;
const JOURNEY_RECEIPT_PATH = process.env.HONUA_ZERO_TO_MAP_RECEIPT;

test.describe('2026.1 zero-to-map candidate Console receipt', () => {
  test('inspects exact admin/MCP identities, approves the real proposal, and writes the gate receipt', async ({ page }) => {
    test.skip(
      !PLAN_PATH && !JOURNEY_RECEIPT_PATH,
      'Set HONUA_ZERO_TO_MAP_PLAN and HONUA_ZERO_TO_MAP_RECEIPT to run the candidate-backed gate.',
    );
    test.setTimeout(180_000);

    const planPath = required(PLAN_PATH, 'HONUA_ZERO_TO_MAP_PLAN');
    const journeyReceiptPath = required(JOURNEY_RECEIPT_PATH, 'HONUA_ZERO_TO_MAP_RECEIPT');
    const outputPath = required(process.env.HONUA_ZERO_TO_MAP_CONSOLE_RECEIPT, 'HONUA_ZERO_TO_MAP_CONSOLE_RECEIPT');
    const inputs: ConsoleReceiptInputs = {
      // This is intentionally separate from honua_studio_propose_publication. That Studio action
      // records PublicationIntent only; the Console gate requires a real admin proposal.
      proposalId: required(process.env.HONUA_ZERO_TO_MAP_PROPOSAL_ID, 'HONUA_ZERO_TO_MAP_PROPOSAL_ID'),
      candidateId: required(process.env.HONUA_ZERO_TO_MAP_CANDIDATE_ID, 'HONUA_ZERO_TO_MAP_CANDIDATE_ID'),
      releaseId: required(process.env.HONUA_ZERO_TO_MAP_RELEASE_ID, 'HONUA_ZERO_TO_MAP_RELEASE_ID'),
      shareUrl: required(process.env.HONUA_ZERO_TO_MAP_SHARE_URL, 'HONUA_ZERO_TO_MAP_SHARE_URL'),
    };

    const plan = JSON.parse(await readFile(planPath, 'utf8')) as unknown;
    const journeyReceipt = JSON.parse(await readFile(journeyReceiptPath, 'utf8')) as unknown;
    const facts = readZeroToMapFacts(plan, journeyReceipt);

    // Release/deployment identity: the candidate controller owns candidateId/releaseId; Console
    // independently proves the image is serving build metadata and live server health.
    const versionResponse = await page.request.get('/version.json');
    expect(versionResponse.ok(), `Console version endpoint -> ${versionResponse.status()}`).toBeTruthy();
    const version = (await versionResponse.json()) as { version?: unknown };
    expect(typeof version.version === 'string' && version.version.length > 0, 'Console build version').toBeTruthy();

    await page.goto('/operate/health');
    await expect(page.locator('#health-checks')).toBeVisible();
    await expect(page.locator('.approval-inbox-toolbar .console-status')).toHaveClass(/console-state-success/);

    // Resource ids must be the same values emitted by honua admin/MCP—not names guessed from UI copy.
    await page.goto(`/operate/connections/${encodeURIComponent(facts.connectionId)}`);
    await expect(page.locator(`[data-connection-id="${attributeValue(facts.connectionId)}"]`)).toHaveAttribute(
      'data-connection-loaded',
      'True',
    );

    await page.goto(`/operate/services/${encodeURIComponent(facts.serviceId)}/settings`);
    const service = page.locator(`[data-service-name="${attributeValue(facts.serviceId)}"]`);
    await expect(service).toHaveAttribute('data-service-loaded', 'True');
    await expect(service.locator(`[data-layer-id="${facts.layerIds.parcels}"]`)).toBeVisible();
    await expect(service.locator(`[data-layer-id="${facts.layerIds.zoning}"]`)).toBeVisible();

    await page.goto('/operate/layers');
    await expect(page.locator(`[data-layer-id="${facts.layerIds.parcels}"]`)).toBeVisible();
    await expect(page.locator(`[data-layer-id="${facts.layerIds.zoning}"]`)).toBeVisible();

    // Approve the separately created server proposal through the governed Console endpoint.
    await page.goto(`/inbox?proposalId=${encodeURIComponent(inputs.proposalId)}`);
    const proposal = page.locator(`[data-proposal-id="${attributeValue(inputs.proposalId)}"]`);
    await expect(proposal).toBeVisible();
    const approve = proposal.locator('[data-proposal-approve]');
    if ((await approve.count()) > 0) {
      await approve.click();
      await expect(proposal.locator('[data-proposal-action-message]')).toContainText('Approved');
    }
    const executionOperationId = await correlationChipValue(page, 'OperationId');

    // The AI-facing Esri MCP job is the identity join: Esri alias + canonical process + durable
    // result package/artifact + audit correlation all belong to the same server job.
    await openExactJob(page, facts.jobs.esriMcp);
    await expect(page.locator('[data-gpserver-service-id]')).toHaveText(facts.gp.serviceId);
    await expect(page.locator('[data-gpserver-task-name]')).toHaveText(facts.gp.taskName);
    await expect(page.locator('[data-canonical-process-id]')).toHaveText(facts.gp.processId);
    await expect(page.locator('[data-result-package-id]')).toHaveText(facts.gp.resultPackageId);
    await expect(page.locator(`[data-evidence-id="${attributeValue(facts.gp.artifactId)}"]`)).toBeVisible();
    const correlationId = await correlationChipValue(page, 'CorrelationId');

    // The SDK GPServer adapter and direct analysis action remain distinct observed jobs over one
    // canonical runtime. Console has no second runner and no authoring/run UI in this gate.
    await openExactJob(page, facts.jobs.gpServer);
    await expect(page.locator('[data-gpserver-task-name]')).toHaveText(facts.gp.taskName);
    await expect(page.locator('[data-canonical-process-id]')).toHaveText(facts.gp.processId);

    await openExactJob(page, facts.jobs.directAnalysis);
    await expect(page.locator(`[data-evidence-id="${attributeValue(facts.artifactId)}"]`)).toBeVisible();

    // Recovery witness: an honest missing job must render a denied state, then an exact known job
    // must be recoverable by its stable deep link. This is read-only and cannot mutate candidate state.
    await page.goto(`/operate/geoprocessing/console-recovery-missing-${Date.now()}`);
    await expect(page.locator('.operate-status-denied')).toBeVisible();
    await openExactJob(page, facts.jobs.esriMcp);

    const shareResponse = await page.request.get(inputs.shareUrl);
    expect(shareResponse.status(), `approved share URL ${inputs.shareUrl}`).toBe(200);

    const receipt = buildConsoleReceipt(facts, inputs, { executionOperationId, correlationId });
    await mkdir(dirname(outputPath), { recursive: true });
    await writeFile(outputPath, `${JSON.stringify(receipt, null, 2)}\n`, 'utf8');
  });
});

async function openExactJob(page: import('@playwright/test').Page, jobId: string): Promise<void> {
  await page.goto(`/operate/geoprocessing/${encodeURIComponent(jobId)}`);
  await expect(page.locator('#job-detail-heading')).toHaveText(jobId);
}

function required(value: string | undefined, name: string): string {
  const trimmed = value?.trim();
  if (!trimmed) throw new Error(`${name} is required for the candidate-backed Console gate`);
  return trimmed;
}

function attributeValue(value: string): string {
  return value.replaceAll('\\', '\\\\').replaceAll('"', '\\"');
}
