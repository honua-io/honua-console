#!/usr/bin/env node
// honua-console#9 real-server gate checker.
//
// Console Patterns Charter §11 ("Real-server integration — no standing mocks")
// makes the cross-surface smoke a release/portal-retirement gate that may only
// be satisfied by evidence from a chain that ran against a real honua-server.
// This checker reads a parity evidence file and decides whether it satisfies
// that gate.
//
// The rule (honua-console#59 AC2): a run against only the in-memory
// contract-shape adapters MUST NOT satisfy the gate. Such evidence reports
// `sourceHydrated: false` with no `server` provenance; the checker classifies
// it as PENDING (not yet satisfiable, blocked upstream) — never satisfied.
//
// Evidence that claims a real-server run (`sourceHydrated: true`) is validated
// strictly: it must carry the honua-server image + commit and the seed profile,
// and the scenario result must be `ok`.
//
// Usage:
//   node smoke/parity/check-gate.mjs [--pending-ok] [evidence.json]
//
// Exit codes:
//   0 — gate satisfied (real-server evidence is complete and green), OR the
//       gate is PENDING and --pending-ok was passed (prints a warning).
//   1 — gate NOT satisfied: real-server evidence is incomplete/failed, or the
//       evidence is mock-only and --pending-ok was not passed.
//   2 — checker setup error (bad args, unreadable/invalid evidence file).

import { readFile } from "node:fs/promises";
import { fileURLToPath } from "node:url";
import { resolve } from "node:path";

// Upstream work that must land before the gate can be satisfied. Surfaced in
// the PENDING message so a reader knows why no real-server evidence exists yet.
export const GATE_BLOCKERS = Object.freeze([
  "honua-sdk-dotnet#166 (typed .NET client projections: content-item, share-access, embed-token, map-package, publish-handoff)",
  "Console integration wiring: replace the InMemory* shell services with a real client — no HTTP transport to honua-server exists today",
  "honua-server Console-facing chain endpoints (share-access, embed-token, open-data, webmap), coordinated via honua-server#1162",
]);

const REQUIRED_SERVER_FIELDS = Object.freeze(["image", "commit", "seedProfile"]);

/**
 * Decide whether an evidence report satisfies the honua-console#9 real-server
 * gate. Pure over `report`.
 *
 * @returns {{ satisfied: boolean, pending: boolean, reasons: string[] }}
 *   - satisfied: the gate is met (real, complete, green real-server evidence).
 *   - pending:   the gate is not yet satisfiable (mock-only evidence); distinct
 *                from a hard failure so CI can stay green while the gate is
 *                honestly reported as unmet.
 *   - reasons:   human-readable explanation lines.
 */
export function evaluateGate(report) {
  if (!report || typeof report !== "object") {
    return { satisfied: false, pending: false, reasons: ["evidence is not a JSON object"] };
  }

  const hydrated = report.sourceHydrated === true;
  const server = report.server ?? null;

  // Mock-only evidence: never satisfied, classified as pending so the gate is
  // reported as "not yet met" rather than "broken".
  if (!hydrated && !server) {
    return {
      satisfied: false,
      pending: true,
      reasons: [
        "evidence is from in-memory contract-shape adapters (sourceHydrated=false, server=null)",
        "the honua-console#9 gate requires a chain run against a real honua-server (Console Patterns Charter §11)",
      ],
    };
  }

  // The evidence claims a real-server run — validate it strictly.
  const reasons = [];
  if (report.result !== "ok") {
    reasons.push(`scenario result is "${report.result ?? "missing"}", expected "ok"`);
  }
  if (!hydrated) {
    reasons.push("sourceHydrated must be true for a real-server run");
  }
  if (!server) {
    reasons.push("server provenance block is missing (image, commit, seedProfile)");
  } else {
    for (const field of REQUIRED_SERVER_FIELDS) {
      if (typeof server[field] !== "string" || server[field].length === 0) {
        reasons.push(`server.${field} is required (records the honua-server image/commit and seed profile)`);
      }
    }
  }

  return { satisfied: reasons.length === 0, pending: false, reasons };
}

function parseArgs(argv) {
  const args = { allowPending: false, evidencePath: "smoke-evidence/console-parity.json" };
  let sawPath = false;
  for (const a of argv) {
    if (a === "--pending-ok") {
      args.allowPending = true;
    } else if (a === "--help" || a === "-h") {
      args.help = true;
    } else if (a.startsWith("-")) {
      throw new Error(`Unknown argument: ${a}`);
    } else if (!sawPath) {
      args.evidencePath = a;
      sawPath = true;
    } else {
      throw new Error(`Unexpected extra argument: ${a}`);
    }
  }
  return args;
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
      `Usage: node smoke/parity/check-gate.mjs [--pending-ok] [evidence.json]\n\n` +
        `Checks whether parity evidence satisfies the honua-console#9 real-server gate.\n` +
        `See docs/smoke/real-server-gate.md and docs/migration/CONSOLE_PATTERNS_CHARTER.md §11.\n`,
    );
    return;
  }

  const path = resolve(process.cwd(), args.evidencePath);
  let report;
  try {
    report = JSON.parse(await readFile(path, "utf8"));
  } catch (e) {
    process.stderr.write(`::error::could not read evidence at ${path}: ${e instanceof Error ? e.message : String(e)}\n`);
    process.exitCode = 2;
    return;
  }

  const { satisfied, pending, reasons } = evaluateGate(report);

  if (satisfied) {
    process.stdout.write(
      `#9 real-server gate SATISFIED: ${report.scenario} ran against ` +
        `${report.server.image} (${report.server.commit}), seed profile "${report.server.seedProfile}".\n`,
    );
    return;
  }

  if (pending) {
    const message =
      `honua-console#9 real-server gate PENDING — evidence at ${args.evidencePath} is mock-only. ` +
      `Blocked on: ${GATE_BLOCKERS.join("; ")}.`;
    if (args.allowPending) {
      // Loud-but-non-blocking: CI stays green while the gate is honestly unmet.
      process.stdout.write(`::warning::${message}\n`);
      for (const reason of reasons) process.stdout.write(`  - ${reason}\n`);
      return;
    }
    process.stderr.write(`::error::${message}\n`);
    for (const reason of reasons) process.stderr.write(`  - ${reason}\n`);
    process.exitCode = 1;
    return;
  }

  process.stderr.write(`::error::honua-console#9 real-server gate FAILED for ${args.evidencePath}:\n`);
  for (const reason of reasons) process.stderr.write(`  - ${reason}\n`);
  process.exitCode = 1;
}

const isDirectInvocation = fileURLToPath(import.meta.url) === resolve(process.argv[1] ?? "");
if (isDirectInvocation) {
  main(process.argv.slice(2)).catch((err) => {
    process.stderr.write(`gate checker crashed: ${err instanceof Error ? err.stack ?? err.message : String(err)}\n`);
    process.exitCode = 2;
  });
}
