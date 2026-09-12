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
// Version/archive identity and the exact extracted runtime tree are locked by
// scripts/lib/cesium-extracted-tree.mjs and scripts/cesium-extracted-tree.lock.json.
// If the assets are absent at runtime, SceneViewer degrades to its inline SVG placeholder.
//
//   node scripts/fetch-cesium.mjs           # verify if present, otherwise fetch
//   node scripts/fetch-cesium.mjs --force   # re-fetch, replacing what is there
//   node scripts/fetch-cesium.mjs --verify  # exit non-zero unless present and complete
//   node scripts/fetch-cesium.mjs --force --update-lock # reviewed version bump only

import { createHash } from "node:crypto";
import { existsSync } from "node:fs";
import { copyFile, mkdir, mkdtemp, readFile, rm, readdir, rename, writeFile } from "node:fs/promises";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { spawnSync } from "node:child_process";

import {
  CESIUM_ARCHIVE_SHA256,
  CESIUM_PUBLISHED_MANIFEST,
  CESIUM_VERSION,
  buildManifest,
  canonicalJson,
  inventoryTree,
  verifyTree,
} from "./lib/cesium-extracted-tree.mjs";

const repoRoot = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const destination = resolve(repoRoot, "src/Honua.Console.Shell/wwwroot/vendor/cesium");
const lockPath = resolve(repoRoot, "scripts/cesium-extracted-tree.lock.json");
const registryBase = process.env.HONUA_NPM_REGISTRY ?? "https://registry.npmjs.org";

// Entry assets scene-viewer.js loads directly, plus the directories CESIUM_BASE_URL resolves.
const REQUIRED = ["Cesium.js", "Widgets/widgets.css", "Workers", "Assets", "ThirdParty"];

const args = new Set(process.argv.slice(2));
const force = args.has("--force");
const verifyOnly = args.has("--verify");
const updateLock = args.has("--update-lock");

function present() {
  return REQUIRED.every((entry) => existsSync(join(destination, entry)));
}

if (verifyOnly) {
  try {
    const lock = JSON.parse(await readFile(lockPath, "utf8"));
    await verifyTree(destination, lock, { requirePublishedManifest: true });
    console.log(`cesium ${CESIUM_VERSION}: exact pinned tree verified at ${destination}`);
    process.exit(0);
  } catch (error) {
    console.error(`cesium ${CESIUM_VERSION}: verification failed: ${error.message}`);
    process.exit(1);
  }
}

if (present() && !force) {
  try {
    const lock = JSON.parse(await readFile(lockPath, "utf8"));
    await verifyTree(destination, lock, { requirePublishedManifest: true });
    console.log(`cesium ${CESIUM_VERSION}: exact pinned tree already present (--force to replace)`);
    process.exit(0);
  } catch (error) {
    console.error(`existing Cesium tree failed verification: ${error.message}; use --force to replace it`);
    process.exit(1);
  }
}

const tarballUrl = `${registryBase}/cesium/-/cesium-${CESIUM_VERSION}.tgz`;
console.log(`fetching cesium@${CESIUM_VERSION} from ${tarballUrl}`);

const response = await fetch(tarballUrl);
if (!response.ok) {
  console.error(`fetch failed: ${response.status} ${response.statusText}`);
  process.exit(1);
}
const bytes = Buffer.from(await response.arrayBuffer());
const archiveSha256 = createHash("sha256").update(bytes).digest("hex");
if (archiveSha256 !== CESIUM_ARCHIVE_SHA256) {
  console.error(
    `integrity check failed for cesium@${CESIUM_VERSION}: expected sha256 ` +
      `${CESIUM_ARCHIVE_SHA256}, received ${archiveSha256}`,
  );
  process.exit(1);
}
console.log(`  ${(bytes.length / 1_048_576).toFixed(1)} MB, sha256 ${createHash("sha256").update(bytes).digest("hex").slice(0, 16)}…`);

// Stage beside the destination: /tmp is often a different filesystem and rename() would EXDEV.
await mkdir(dirname(destination), { recursive: true });
const staging = await mkdtemp(join(dirname(destination), ".cesium-staging-"));
const tarball = join(staging, "cesium.tgz");
await writeFile(tarball, bytes);

// npm ships the runtime under package/Build/Cesium. Retain the package license in
// the extracted tree so the published artifact carries independently verifiable
// version/license evidence rather than executable bytes alone.
const extract = spawnSync(
  "tar",
  ["xzf", tarball, "-C", staging, "package/Build/Cesium", "package/LICENSE.md"],
  { stdio: "inherit" },
);
if (extract.status !== 0) {
  console.error("tar extraction failed");
  await rm(staging, { recursive: true, force: true });
  process.exit(1);
}

const extracted = join(staging, "package", "Build", "Cesium");
await copyFile(join(staging, "package", "LICENSE.md"), join(extracted, "LICENSE.md"));
const actualManifest = buildManifest(await inventoryTree(extracted));
if (updateLock) {
  await writeFile(lockPath, `${JSON.stringify(actualManifest, null, 2)}\n`, "utf8");
  console.log(`updated ${lockPath} (${actualManifest.files.length} files, ${actualManifest.treeSha256})`);
} else {
  const expectedManifest = JSON.parse(await readFile(lockPath, "utf8"));
  if (canonicalJson(actualManifest) !== canonicalJson(expectedManifest)) {
    console.error(
      `extracted Cesium tree differs from ${lockPath}: expected ${expectedManifest.treeSha256}, ` +
      `received ${actualManifest.treeSha256}`,
    );
    await rm(staging, { recursive: true, force: true });
    process.exit(1);
  }
}
const pinnedManifest = JSON.parse(await readFile(lockPath, "utf8"));
await writeFile(join(extracted, CESIUM_PUBLISHED_MANIFEST), `${JSON.stringify(pinnedManifest, null, 2)}\n`, "utf8");

await rm(destination, { recursive: true, force: true });
await mkdir(dirname(destination), { recursive: true });
await rename(extracted, destination);
await rm(staging, { recursive: true, force: true });

const top = await readdir(destination);
console.log(`vendored cesium@${CESIUM_VERSION} -> ${destination} (${top.length} top-level entries)`);
if (!present()) {
  console.error(`incomplete: expected ${REQUIRED.join(", ")}`);
  process.exit(1);
}
await verifyTree(destination, pinnedManifest, { requirePublishedManifest: true });
console.log(`ok: ${pinnedManifest.files.length} files, tree sha256 ${pinnedManifest.treeSha256}`);
