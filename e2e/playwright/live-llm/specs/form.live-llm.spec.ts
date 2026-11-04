import { test, expect, classifyGeneration, GATE_REASON, LIVE_LLM_ENABLED } from '../support/live-llm';

// FORM generate-from-prompt against a REAL provider (honua-console#283).
//
// FORM generation is behind the studio generation feature gate (the shared
// WorkflowGeneration section). The lane's stack enables it; we still probe the
// providers endpoint and skip cleanly if a given server does not expose it.
// Tolerant: prove a coherent generated form package came back, not its exact shape.

const FORM_GENERATE = '/api/v1/admin/forms/packages/generate';
const FORM_PROVIDERS = '/api/v1/console/form-packages/generation/providers';
const PROMPT =
  'An inspection form with a required "Asset name" text field, a "Condition" choice field ' +
  '(good / fair / poor), and a "Notes" multi-line text area.';

test.beforeEach(() => {
  test.skip(!LIVE_LLM_ENABLED, GATE_REASON);
});

test.describe('Live-LLM · FORM from prompt', () => {
  test('server generates a coherent form package from the prompt (API)', async ({ server }) => {
    // The admin form-generation endpoint is the real capability. The console-facing
    // providers probe (FORM_PROVIDERS) can 404 on an image that generates fine, so
    // gating on it would wrongly skip — we gate on the generate call itself, which
    // classifyGeneration turns into a clean skip only when the server truly reports
    // the surface unsupported (no provider).
    const outcome = await server.generate(FORM_GENERATE, PROMPT);
    classifyGeneration('form', outcome);
    expect(outcome.data, 'a generated form response body').toBeTruthy();
    expect(
      outcome.data.package ?? outcome.data.form ?? outcome.data.schema,
      'a generated form artifact',
    ).toBeTruthy();
  });
});
