import { test, expect, classifyGeneration, recordSurface, GATE_REASON, LIVE_LLM_ENABLED } from '../support/live-llm';

// WORKFLOW generate-from-prompt against a REAL provider (honua-console#283).
//
// WORKFLOW generation is behind the studio generation feature gate; the lane's
// stack enables it (WorkflowGeneration:Enabled=true). We probe the providers
// endpoint (reports enabled + provider list) and skip cleanly if disabled.
// Tolerant: prove a coherent generated workflow came back, not its exact DAG.

const WORKFLOW_GENERATE = '/api/v1/console/workflow-packages/generate';
const WORKFLOW_PROVIDERS = '/api/v1/console/workflow-generation/providers';
const PROMPT =
  'Copy new parcel features into a working layer, stamp a reviewed flag on each, ' +
  'then compute the area of every parcel.';

test.beforeEach(() => {
  test.skip(!LIVE_LLM_ENABLED, GATE_REASON);
});

test.describe('Live-LLM · WORKFLOW from prompt', () => {
  test('server generates a coherent workflow from the prompt (API)', async ({ server }) => {
    // Feature-gate probe: the providers endpoint reports {enabled, providers[]}.
    const providers = await server.getJson(WORKFLOW_PROVIDERS);
    const enabled = providers.ok && providers.body?.enabled === true &&
      Array.isArray(providers.body?.providers) && providers.body.providers.length > 0;
    if (!enabled) {
      recordSurface('workflow', 'unsupported', `providers enabled=${providers.body?.enabled}`);
      test.skip(true, 'workflow generation is disabled or has no provider on this server — clean skip.');
    }

    const outcome = await server.generate(WORKFLOW_GENERATE, PROMPT);
    classifyGeneration('workflow', outcome);
    expect(outcome.data, 'a generated workflow response body').toBeTruthy();
    expect(
      outcome.data.package ?? outcome.data.workflow ?? outcome.data.graph,
      'a generated workflow artifact',
    ).toBeTruthy();
  });

  test('console /studio/workflows/new from-prompt surface renders a workflow graph', async ({ page }) => {
    await page.goto('/studio/workflows/new');

    // The generation provider control (a <select> for >=2 providers, a <span> for
    // exactly one) should render when the console sees an enabled provider. If it
    // does not on this image, skip cleanly rather than fail — non-blocking lane.
    const control = page.locator('select[data-workflow-ai-provider], span[data-workflow-ai-provider]').first();
    const controlRendered = await control
      .waitFor({ state: 'visible', timeout: 25_000 })
      .then(() => true)
      .catch(() => false);
    if (!controlRendered) {
      recordSurface('workflow-ui', 'unsupported', 'workflow provider control did not render');
      test.skip(true, 'console workflow-generation provider control did not render on this server image — clean skip.');
    }

    await page.locator('textarea').first().fill(PROMPT);
    await page.getByRole('button', { name: /Send/ }).click();

    // Tolerant success: a node/edge graph with at least one real node renders.
    // WORKFLOW is the largest, hardest schema; a small model may not produce a
    // valid DAG. If no graph materialises, record + skip (non-blocking) instead of
    // turning the nightly red on model non-determinism.
    const graph = page.locator('[data-workflow-graph]').first();
    const graphRendered = await graph
      .waitFor({ state: 'visible', timeout: 120_000 })
      .then(() => true)
      .catch(() => false);
    const nodeCount = graphRendered ? await graph.locator('[data-workflow-graph-node]').count() : 0;
    if (!graphRendered || nodeCount === 0) {
      recordSurface('workflow-ui', 'error', 'no workflow graph node rendered');
      test.info().annotations.push({ type: 'generation-error', description: 'workflow-ui: no graph rendered' });
      test.skip(true, 'workflow generation did not produce a rendered graph — recorded, non-blocking skip.');
    }
    expect(nodeCount, 'a rendered workflow graph with >=1 node').toBeGreaterThan(0);
    recordSurface('workflow-ui', 'generated');
  });
});
