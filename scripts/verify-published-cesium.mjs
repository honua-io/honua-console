#!/usr/bin/env node

import { access, readFile } from "node:fs/promises";
import { resolve } from "node:path";
import { verifyTree } from "./lib/cesium-extracted-tree.mjs";

const artifactArgument = process.argv[2];
if (!artifactArgument) {
  console.error("usage: node scripts/verify-published-cesium.mjs <published-artifact-directory>");
  process.exit(2);
}

const artifactRoot = resolve(process.cwd(), artifactArgument);
let staticRoot = resolve(artifactRoot, "wwwroot", "console");
try {
  await access(resolve(staticRoot, "index.html"));
} catch {
  staticRoot = resolve(artifactRoot, "wwwroot");
}
const cesiumRoot = resolve(
  staticRoot,
  "_content",
  "Honua.Console.Shell",
  "vendor",
  "cesium",
);
try {
  const lock = JSON.parse(await readFile(resolve(import.meta.dirname, "cesium-extracted-tree.lock.json"), "utf8"));
  const manifest = await verifyTree(cesiumRoot, lock, { requirePublishedManifest: true });
  console.log(
    `published Console contains exact cesium@${manifest.version} tree ` +
    `${manifest.treeSha256} with ${manifest.files.length} files and ${manifest.license.spdx} license evidence`,
  );
} catch (error) {
  console.error(`published Console Cesium verification failed under ${cesiumRoot}: ${error.message}`);
  process.exit(1);
}
