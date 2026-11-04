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
});
