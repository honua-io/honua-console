#!/usr/bin/env node
// Fetch CesiumJS into wwwroot/vendor/cesium at deploy/build time (honua-console#334).
//
// Cesium is NOT committed. Its Build/Cesium tree is ~69 MB across Workers/, Assets/,
// ThirdParty/, and Widgets/, resolved dynamically at runtime through
// window.CESIUM_BASE_URL — committing it would put that weight in every clone and CI
// checkout permanently. The other vendored assets (MapLibre, Vega) are a few hundred
// kilobytes of single files and are committed with an integrity lock; Cesium is a
// different shape of problem and gets a different answer.
//
// The version here is the single source of truth and must match what the Console was
// built against. If the assets are absent at runtime, SceneViewer degrades to its inline
// SVG placeholder — 3D is a capability that lights up when its assets are present.
//
//   node scripts/fetch-cesium.mjs           # fetch if missing
//   node scripts/fetch-cesium.mjs --force   # re-fetch, replacing what is there
//   node scripts/fetch-cesium.mjs --verify  # exit non-zero unless present and complete

import { createHash } from "node:crypto";
import { existsSync } from "node:fs";
import { mkdir, mkdtemp, rm, readdir, rename, writeFile } from "node:fs/promises";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { spawnSync } from "node:child_process";

const CESIUM_VERSION = "1.119.0";
const repoRoot = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const destination = resolve(repoRoot, "src/Honua.Console.Shell/wwwroot/vendor/cesium");
const registryBase = process.env.HONUA_NPM_REGISTRY ?? "https://registry.npmjs.org";

// Entry assets scene-viewer.js loads directly, plus the directories CESIUM_BASE_URL resolves.
const REQUIRED = ["Cesium.js", "Widgets/widgets.css", "Workers", "Assets", "ThirdParty"];

const args = new Set(process.argv.slice(2));
const force = args.has("--force");
const verifyOnly = args.has("--verify");

function present() {
  return REQUIRED.every((entry) => existsSync(join(destination, entry)));
}

if (verifyOnly) {
  if (present()) {
    console.log(`cesium ${CESIUM_VERSION}: present at ${destination}`);
    process.exit(0);
  }
  console.error(
    `cesium ${CESIUM_VERSION}: MISSING or incomplete at ${destination}\n` +
      `SceneViewer will fall back to its placeholder. Run: node scripts/fetch-cesium.mjs`,
  );
  process.exit(1);
}

if (present() && !force) {
  console.log(`cesium ${CESIUM_VERSION}: already present — nothing to do (--force to replace)`);
  process.exit(0);
}

const tarballUrl = `${registryBase}/cesium/-/cesium-${CESIUM_VERSION}.tgz`;
console.log(`fetching cesium@${CESIUM_VERSION} from ${tarballUrl}`);

const response = await fetch(tarballUrl);
if (!response.ok) {
  console.error(`fetch failed: ${response.status} ${response.statusText}`);
  process.exit(1);
}
const bytes = Buffer.from(await response.arrayBuffer());
console.log(`  ${(bytes.length / 1_048_576).toFixed(1)} MB, sha256 ${createHash("sha256").update(bytes).digest("hex").slice(0, 16)}…`);

// Stage beside the destination: /tmp is often a different filesystem and rename() would EXDEV.
await mkdir(dirname(destination), { recursive: true });
const staging = await mkdtemp(join(dirname(destination), ".cesium-staging-"));
const tarball = join(staging, "cesium.tgz");
await writeFile(tarball, bytes);

// npm ships the build under package/Build/Cesium.
const extract = spawnSync("tar", ["xzf", tarball, "-C", staging, "package/Build/Cesium"], { stdio: "inherit" });
if (extract.status !== 0) {
  console.error("tar extraction failed");
  await rm(staging, { recursive: true, force: true });
  process.exit(1);
}

await rm(destination, { recursive: true, force: true });
await mkdir(dirname(destination), { recursive: true });
await rename(join(staging, "package", "Build", "Cesium"), destination);
await rm(staging, { recursive: true, force: true });

const top = await readdir(destination);
console.log(`vendored cesium@${CESIUM_VERSION} -> ${destination} (${top.length} top-level entries)`);
if (!present()) {
  console.error(`incomplete: expected ${REQUIRED.join(", ")}`);
  process.exit(1);
}
console.log("ok");
