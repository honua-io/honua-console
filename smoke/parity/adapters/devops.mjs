// Devops adapter: loads the build-artifact metadata that honua-devops
// promotes. Operates in two modes:
//
//   - Deployed-origin mode (originUrl host is NOT loopback): MUST fetch
//     `${originUrl}/version.json` over HTTP and validate it. A failed fetch
//     or schema-invalid response throws so the smoke cannot silently ship a
//     "looks deployed" evidence file against an unreachable artifact. This
//     is the path CI uses when `--origin "$PREVIEW_ORIGIN"` is passed.
//   - Local/offline mode (originUrl host is loopback, or no origin given):
//     prefer `dist/version.json` (honua-console#8 writer); when absent, fall
//     back to the committed fixture under
//     `smoke/parity/fixtures/dist-version.json`. The fallback is reported
//     as `source: "fixture"` in evidence so a reviewer can tell the
//     placeholder run from a real artifact verification.
//
// The evidence `buildArtifact.source` is `"origin"`, `"dist"`, or
// `"fixture"`; downstream tooling can refuse to gate on `"fixture"`.

import { readFile, stat } from "node:fs/promises";
import { resolve } from "node:path";

import { findContract } from "../contracts.mjs";

const REQUIRED_BUILD_FIELDS = ["name", "version", "commit", "shortCommit", "ref", "builtAt", "legacy", "areas"];
const REQUIRED_LEGACY_FIELDS = ["portal", "admin"];
const REQUIRED_AREAS = ["studio", "catalog", "share", "operate"];

// Hosts treated as local/offline. Anything else is a deployed origin and
// MUST be verified via HTTP fetch — we never silently fall back to the
// fixture for a remote origin.
const LOOPBACK_HOSTS = new Set(["localhost", "127.0.0.1", "::1", "0.0.0.0"]);

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

/** True when the origin URL is a loopback address (local dev / smoke harness). */
export function isLocalOrigin(originUrl) {
  if (!originUrl) return true;
  let parsed;
  try {
    parsed = new URL(originUrl);
  } catch {
    return false;
  }
  return LOOPBACK_HOSTS.has(parsed.hostname);
}

async function fetchOriginVersion({ originUrl, fetchImpl }) {
  const target = new URL("/version.json", originUrl).toString();
  let response;
  try {
    response = await fetchImpl(target);
  } catch (cause) {
    throw new BuildArtifactError(
      `deployed-origin smoke could not reach ${target}: ${cause instanceof Error ? cause.message : String(cause)}`,
      { reason: "origin-unreachable" },
    );
  }
  if (!response.ok) {
    throw new BuildArtifactError(
      `deployed-origin smoke got HTTP ${response.status} from ${target}`,
      { reason: "origin-http-error" },
    );
  }
  let parsed;
  try {
    parsed = await response.json();
  } catch (cause) {
    throw new BuildArtifactError(`deployed-origin ${target} did not return valid JSON`, {
      reason: cause instanceof Error ? cause.message : String(cause),
    });
  }
  return { parsed, path: target };
}

export async function loadBuildArtifact({ repoRoot, originUrl, distPath, fixturePath, fetchImpl = globalThis.fetch } = {}) {
  if (!repoRoot) throw new Error("loadBuildArtifact requires repoRoot");

  // Deployed-origin path: the AC2 promise is that the smoke runs against
  // the single deployable artifact. When the caller has supplied a non-
  // loopback origin we honor that — falling back to the local fixture
  // here would ship an evidence file with `source: "fixture"` against a
  // URL that may not even exist, which is exactly the false-green the
  // earlier code review found.
  if (originUrl && !isLocalOrigin(originUrl)) {
    if (typeof fetchImpl !== "function") {
      throw new BuildArtifactError(
        `deployed-origin smoke requires a fetch implementation (Node 18+ global fetch or an injected fetchImpl)`,
        { reason: "no-fetch" },
      );
    }
    const { parsed, path } = await fetchOriginVersion({ originUrl, fetchImpl });
    validateBuildArtifact(parsed, path);
    return { metadata: parsed, source: "origin", path, contract: findContract("build-artifact") };
  }

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
