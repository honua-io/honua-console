// Devops adapter: loads the build-artifact metadata that honua-devops
// promotes. By default the smoke prefers `dist/version.json` (produced by
// honua-console#8's vite plugin / write-build-metadata script). When that
// file is absent (e.g., trunk before the scaffold lands, or a Console
// repo checkout without `npm run build`), the smoke falls back to the
// committed fixture under `smoke/parity/fixtures/dist-version.json`. The
// fallback is reported in evidence as `source: "fixture"` so a CI
// reviewer can tell the difference between a real artifact verification
// and a placeholder run.

import { readFile, stat } from "node:fs/promises";
import { resolve } from "node:path";

import { findContract } from "../contracts.mjs";

const REQUIRED_BUILD_FIELDS = ["name", "version", "commit", "shortCommit", "ref", "builtAt", "legacy", "areas"];
const REQUIRED_LEGACY_FIELDS = ["portal", "admin"];
const REQUIRED_AREAS = ["studio", "catalog", "share", "operate"];

export class BuildArtifactError extends Error {
  constructor(message, { reason }) {
    super(message);
    this.name = "BuildArtifactError";
    this.reason = reason;
  }
}

async function exists(path) {
  try {
    await stat(path);
    return true;
  } catch {
    return false;
  }
}

export async function loadBuildArtifact({ repoRoot, distPath, fixturePath } = {}) {
  if (!repoRoot) throw new Error("loadBuildArtifact requires repoRoot");
  const dist = distPath ?? resolve(repoRoot, "dist/version.json");
  const fixture = fixturePath ?? resolve(repoRoot, "smoke/parity/fixtures/dist-version.json");

  let source = "dist";
  let path = dist;
  if (!(await exists(dist))) {
    source = "fixture";
    path = fixture;
  }
  const raw = await readFile(path, "utf8");
  let parsed;
  try {
    parsed = JSON.parse(raw);
  } catch (cause) {
    throw new BuildArtifactError(`build-artifact metadata at ${path} is not valid JSON`, {
      reason: cause instanceof Error ? cause.message : String(cause),
    });
  }
  validateBuildArtifact(parsed, path);
  return { metadata: parsed, source, path, contract: findContract("build-artifact") };
}

export function validateBuildArtifact(metadata, path) {
  const missing = REQUIRED_BUILD_FIELDS.filter((f) => !(f in metadata));
  if (missing.length > 0) {
    throw new BuildArtifactError(`build-artifact at ${path} missing fields: ${missing.join(", ")}`, {
      reason: "missing-fields",
    });
  }
  if (metadata.name !== "honua-console") {
    throw new BuildArtifactError(`build-artifact at ${path} declares name="${metadata.name}", expected "honua-console"`, {
      reason: "wrong-artifact",
    });
  }
  const missingLegacy = REQUIRED_LEGACY_FIELDS.filter((f) => !(f in (metadata.legacy ?? {})));
  if (missingLegacy.length > 0) {
    throw new BuildArtifactError(`build-artifact at ${path} legacy block missing: ${missingLegacy.join(", ")}`, {
      reason: "missing-legacy",
    });
  }
  if (!Array.isArray(metadata.areas)) {
    throw new BuildArtifactError(`build-artifact at ${path} areas must be an array`, { reason: "areas-not-array" });
  }
  const missingAreas = REQUIRED_AREAS.filter((a) => !metadata.areas.includes(a));
  if (missingAreas.length > 0) {
    throw new BuildArtifactError(
      `build-artifact at ${path} areas missing: ${missingAreas.join(", ")} — single deployable artifact must serve all four areas from one origin`,
      { reason: "areas-incomplete" },
    );
  }
}
