import { test, expect, classifyGeneration, GATE_REASON, LIVE_LLM_ENABLED } from '../support/live-llm';

// MAP generate-from-prompt against a REAL provider (honua-console#283).
// Tolerant: prove a coherent generated map package came back, not its exact shape.

const MAP_GENERATE = '/api/v1/studio/map-packages/generate';
const PROMPT = 'A simple map showing a single parcels layer over a light basemap.';

test.beforeEach(() => {
  test.skip(!LIVE_LLM_ENABLED, GATE_REASON);
});

test.describe('Live-LLM · MAP from prompt', () => {
  test('server generates a coherent map package from the prompt (API)', async ({ server }) => {
    const outcome = await server.generate(MAP_GENERATE, PROMPT);
    classifyGeneration('map', outcome);
    expect(outcome.data, 'a generated map response body').toBeTruthy();
    expect(outcome.data.package ?? outcome.data.map, 'a generated map artifact').toBeTruthy();
  });
});
