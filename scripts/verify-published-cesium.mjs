#!/usr/bin/env node

import { existsSync } from "node:fs";
import { resolve } from "node:path";

const artifactArgument = process.argv[2];
if (!artifactArgument) {
  console.error("usage: node scripts/verify-published-cesium.mjs <published-artifact-directory>");
  process.exit(2);
}

const artifactRoot = resolve(process.cwd(), artifactArgument);
const cesiumRoot = resolve(
  artifactRoot,
  "wwwroot",
  "_content",
  "Honua.Console.Shell",
  "vendor",
  "cesium",
);
const required = ["Cesium.js", "Widgets/widgets.css", "Workers", "Assets", "ThirdParty"];
const missing = required.filter((entry) => !existsSync(resolve(cesiumRoot, entry)));
if (missing.length > 0) {
  console.error(`published Console is missing Cesium assets under ${cesiumRoot}: ${missing.join(", ")}`);
  process.exit(1);
}

console.log(`published Console contains digest-verified Cesium assets under ${cesiumRoot}`);
