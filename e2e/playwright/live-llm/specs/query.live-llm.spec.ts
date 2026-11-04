import { test, expect, classifyGeneration, GATE_REASON, LIVE_LLM_ENABLED } from '../support/live-llm';

// QUERY generate-from-prompt against a REAL provider (honua-console#283).
//
// The gap this closes: the deterministic live lane can only prove the console's
// baseline binding (QueryGenerationService returns "unsupported" with no provider,
// and the console seeds an honest baseline). Here a real provider is configured, so
// the service actually runs prompt -> LLM -> a structured saved query. Assertions
// are tolerant: we prove a coherent generated query came back, not its exact shape.

const QUERY_GENERATE = '/api/v1/analysis/content/queries/generate';
const PROMPT =
  'Return the id and name fields for all parcel features where the zoning value is R1.';

test.beforeEach(() => {
  test.skip(!LIVE_LLM_ENABLED, GATE_REASON);
});

test.describe('Live-LLM · QUERY from prompt', () => {
  test('server generates a coherent saved query from the prompt (API)', async ({ server }) => {
    const outcome = await server.generate(QUERY_GENERATE, PROMPT);
    // Returns only on a real `generated`; unsupported/error skip cleanly (recorded).
    classifyGeneration('query', outcome);
    // Tolerant coherence: a real, non-empty saved-query artifact was produced.
    expect(outcome.data, 'a generated query response body').toBeTruthy();
    expect(outcome.data.query ?? outcome.data.package, 'a generated query artifact').toBeTruthy();
  });
});
