import { appendFileSync, mkdirSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

// Shared harness for the live-LLM smoke lane: the credential gate and the surface ledger the CI job summary reads.

// run-live-llm.mjs and the nightly workflow set this to '1' only when a real
// provider was resolved. Absent => the lane skips (never fails).
export const LIVE_LLM_ENABLED = process.env.HONUA_LIVE_LLM_ENABLED === '1';
export const PROVIDER_MODE = process.env.HONUA_LIVE_LLM_MODE ?? 'unknown';

export const GATE_REASON =
  'live-LLM lane requires a configured AI provider. Set HONUA_LIVE_LLM_ENABLED=1 with ' +
  'the HONUA_LIVE_LLM_* credentials (Bedrock via LiteLLM, or a direct OpenAI-compatible key), ' +
  'or configure the nightly repo secret. Skipping cleanly.';

// Specs run serially in a single worker, so plain appends are safe.
const RESULTS_DIR = fileURLToPath(new URL('../results/', import.meta.url));
const SURFACES_FILE = join(RESULTS_DIR, 'surfaces.jsonl');

export type SurfaceStatus = 'answered' | 'error';

export function recordSurface(surface: string, status: SurfaceStatus, detail?: string): void {
  try {
    mkdirSync(dirname(SURFACES_FILE), { recursive: true });
    appendFileSync(
      SURFACES_FILE,
      JSON.stringify({ surface, status, mode: PROVIDER_MODE, detail: detail ?? '', at: new Date().toISOString() }) + '\n',
    );
  } catch {
    // A recording failure must never fail the lane.
  }
}
