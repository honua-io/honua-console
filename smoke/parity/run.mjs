#!/usr/bin/env node
// Console parity smoke entry point.
//
// One automated command (honua-console#9 AC1) that drives the cross-surface
// chain: publish -> catalog -> Studio -> share/embed. The command runs
// against the single deployable artifact (AC2) by fetching `/version.json`
// for deployed origins, or by reading local published artifact metadata
// with a fixture fallback for offline runs. Every scenario step is tagged with
// its owning layer; a failure prints a human-readable triage line that
// points directly at the responsible repo (AC3). Evidence is written as
// JSON containing URLs, item/package IDs, and contract versions (AC4).
//
// Usage:
//   node smoke/parity/run.mjs                   # run with defaults
//   node smoke/parity/run.mjs --output path.json
//   node smoke/parity/run.mjs --origin https://console.staging.example
//
// Exit codes:
//   0 — scenario completed (`result: ok`)
//   1 — one step failed (`result: failed`); see triage line / evidence JSON
//   2 — runner setup error (e.g., bad CLI args)

import { fileURLToPath } from "node:url";
import { dirname, resolve } from "node:path";

import { resolveOwningLayer } from "./owning-layers.mjs";
import { SCENARIO_ID, SCENARIO_STEPS } from "./scenario.mjs";
import { buildEvidenceReport, defaultEvidencePath, formatTextSummary, writeEvidence } from "./evidence.mjs";

const HERE = dirname(fileURLToPath(import.meta.url));
const REPO_ROOT = resolve(HERE, "../..");

function parseArgs(argv) {
  const args = { originUrl: "http://127.0.0.1:4174", outputPath: null, quiet: false };
  for (let i = 0; i < argv.length; i += 1) {
    const a = argv[i];
    if (a === "--origin" || a === "-o") {
      args.originUrl = argv[++i];
    } else if (a === "--output") {
      args.outputPath = argv[++i];
    } else if (a === "--quiet") {
      args.quiet = true;
    } else if (a === "--help" || a === "-h") {
      args.help = true;
    } else {
      throw new Error(`Unknown argument: ${a}`);
    }
  }
  return args;
}

export async function runParitySmoke({ originUrl, repoRoot = REPO_ROOT, steps = SCENARIO_STEPS, fetchImpl } = {}) {
  if (!originUrl) throw new Error("runParitySmoke requires originUrl");
  const normalizedOriginUrl = new URL(originUrl).origin;
  const ranAt = new Date().toISOString();
  const ctx = { repoRoot, originUrl: normalizedOriginUrl, itemIds: {}, fetchImpl };
  const executed = [];
  let failure = null;

  for (const step of steps) {
    // Validate the owning layer up-front so a typo in a step definition fails
    // loudly before that step runs.
    resolveOwningLayer(step.owningLayer);

    const startedAt = performance.now();
    let outcome = { evidence: null, contracts: [] };
    let status = "ok";
    let error = null;
    try {
      outcome = (await step.run(ctx)) ?? outcome;
    } catch (e) {
      status = "failed";
      error = e instanceof Error ? e.message : String(e);
      failure = { stepId: step.id, owningLayer: step.owningLayer, message: error };
    }
    const durationMs = Math.round(performance.now() - startedAt);
    executed.push({
      id: step.id,
      owningLayer: step.owningLayer,
      description: step.description,
      status,
      durationMs,
      evidence: outcome.evidence ?? null,
      contracts: outcome.contracts ?? [],
      error,
    });
    if (failure) break;
  }

  // Mark steps after the failure as skipped so evidence is unambiguous about
  // which steps actually ran.
  const ran = executed.map((s) => s.id);
  for (const step of steps) {
    if (!ran.includes(step.id)) {
      executed.push({
        id: step.id,
        owningLayer: step.owningLayer,
        description: step.description,
        status: "skipped",
        durationMs: 0,
        evidence: null,
        contracts: [],
        error: null,
      });
    }
  }

  const report = buildEvidenceReport({
    scenarioId: SCENARIO_ID,
    ranAt,
    ctx,
    steps: executed,
    result: failure ? "failed" : "ok",
    failure,
  });
  return { report, ctx };
}

async function main(argv) {
  let args;
  try {
    args = parseArgs(argv);
  } catch (e) {
    process.stderr.write(`${e.message}\n`);
    process.exitCode = 2;
    return;
  }
  if (args.help) {
    process.stdout.write(
      `Usage: node smoke/parity/run.mjs [--origin URL] [--output path.json] [--quiet]\n` +
        `\n` +
        `Runs the Console parity smoke and writes evidence JSON. See docs/smoke/parity.md.\n`,
    );
    return;
  }
  const { report } = await runParitySmoke({ originUrl: args.originUrl });
  const outputPath = args.outputPath ? resolve(process.cwd(), args.outputPath) : defaultEvidencePath(REPO_ROOT);
  await writeEvidence({ outputPath, report });
  if (!args.quiet) {
    process.stdout.write(`${formatTextSummary(report)}\n`);
    process.stdout.write(`\nevidence written to ${outputPath}\n`);
  }
  if (report.result === "failed") {
    process.exitCode = 1;
  }
}

const isDirectInvocation = fileURLToPath(import.meta.url) === resolve(process.argv[1] ?? "");
if (isDirectInvocation) {
  main(process.argv.slice(2)).catch((err) => {
    process.stderr.write(`parity smoke crashed: ${err instanceof Error ? err.stack ?? err.message : String(err)}\n`);
    process.exitCode = 2;
  });
}
