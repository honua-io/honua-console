// Evidence emitter for the Console parity smoke.
//
// The acceptance criteria on honua-console#9 require the evidence to
// include URLs, item/package IDs, contract versions, and a clear pointer
// at the owning layer of any failure. The emitter renders both a
// machine-readable JSON file (for CI / release-promotion attachment) and
// a short text summary suitable for the workflow log.

import { mkdir, writeFile } from "node:fs/promises";
import { dirname, resolve } from "node:path";

import { OWNING_LAYERS, resolveOwningLayer } from "./owning-layers.mjs";

export function buildEvidenceReport({ scenarioId, ranAt, ctx, steps, result, failure }) {
  const contracts = new Map();
  for (const step of steps) {
    for (const c of step.contracts ?? []) {
      contracts.set(c.name, c);
    }
  }
  const report = {
    scenario: scenarioId,
    ranAt,
    originUrl: ctx.originUrl,
    repoRoot: ctx.repoRoot,
    buildArtifact: ctx.buildArtifact
      ? {
          source: ctx.buildArtifact.source,
          path: ctx.buildArtifact.path,
          name: ctx.buildArtifact.metadata.name,
          version: ctx.buildArtifact.metadata.version,
          commit: ctx.buildArtifact.metadata.commit,
          shortCommit: ctx.buildArtifact.metadata.shortCommit,
          ref: ctx.buildArtifact.metadata.ref,
          builtAt: ctx.buildArtifact.metadata.builtAt,
          legacy: ctx.buildArtifact.metadata.legacy,
          areas: ctx.buildArtifact.metadata.areas,
        }
      : null,
    contractVersions: [...contracts.values()].map((c) => ({
      name: c.name,
      version: c.version,
      owningLayer: c.owningLayer,
      sourceRepo: c.sourceRepo,
      note: c.note,
    })),
    items: { ...ctx.itemIds },
    urls: ctx.urls ?? null,
    steps: steps.map((step) => ({
      id: step.id,
      owningLayer: step.owningLayer,
      owningLayerLabel: OWNING_LAYERS[step.owningLayer].label,
      status: step.status,
      durationMs: step.durationMs,
      description: step.description,
      evidence: step.evidence ?? null,
      error: step.error ?? null,
    })),
    result,
    failure: failure
      ? {
          stepId: failure.stepId,
          owningLayer: failure.owningLayer,
          owningLayerLabel: resolveOwningLayer(failure.owningLayer).label,
          owningRepo: resolveOwningLayer(failure.owningLayer).repo,
          message: failure.message,
        }
      : null,
  };
  return report;
}

export async function writeEvidence({ outputPath, report }) {
  await mkdir(dirname(outputPath), { recursive: true });
  await writeFile(outputPath, `${JSON.stringify(report, null, 2)}\n`);
}

export function formatTextSummary(report) {
  const lines = [];
  lines.push(`# Console parity smoke — ${report.scenario}`);
  lines.push(`ranAt: ${report.ranAt}`);
  lines.push(`originUrl: ${report.originUrl}`);
  if (report.buildArtifact) {
    lines.push(
      `buildArtifact: ${report.buildArtifact.name}@${report.buildArtifact.version} (${report.buildArtifact.shortCommit}, ${report.buildArtifact.ref}) [source=${report.buildArtifact.source}]`,
    );
    lines.push(
      `  legacy: portal=${report.buildArtifact.legacy.portal}, admin=${report.buildArtifact.legacy.admin}`,
    );
  }
  if (report.contractVersions.length > 0) {
    lines.push("contractVersions:");
    for (const c of report.contractVersions) {
      lines.push(`  - ${c.name} ${c.version} (${c.owningLayer} / ${c.sourceRepo})`);
    }
  }
  if (Object.keys(report.items).length > 0) {
    lines.push("items:");
    for (const [k, v] of Object.entries(report.items)) {
      lines.push(`  - ${k}: ${v}`);
    }
  }
  if (report.urls) {
    lines.push("urls:");
    for (const [k, v] of Object.entries(report.urls)) {
      lines.push(`  - ${k}: ${v}`);
    }
  }
  lines.push("steps:");
  for (const step of report.steps) {
    const status = step.status === "ok" ? "✓" : step.status === "failed" ? "✗" : step.status;
    lines.push(`  ${status} ${step.id} [${step.owningLayer}] ${step.durationMs}ms`);
    if (step.status === "failed") {
      lines.push(`     error: ${step.error}`);
    }
  }
  lines.push(`result: ${report.result}`);
  if (report.failure) {
    lines.push(`failure step: ${report.failure.stepId}`);
    lines.push(`failure owner: ${report.failure.owningLayerLabel} (${report.failure.owningRepo})`);
    lines.push(`failure message: ${report.failure.message}`);
  }
  return lines.join("\n");
}

/**
 * Resolve the default output path used by `npm run smoke:parity`.
 * Committed sample output lives under `smoke/parity/sample-evidence/` so
 * reviewers can read the format without running the command.
 */
export function defaultEvidencePath(repoRoot) {
  return resolve(repoRoot, "smoke-evidence/console-parity.json");
}
