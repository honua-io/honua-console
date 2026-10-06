import { strict as assert } from "node:assert";
import { resolve } from "node:path";
import { test } from "node:test";
import { fileURLToPath } from "node:url";

import {
  findPublicContentViolations,
  scanPublicRepository,
} from "../check-public-content.mjs";

const repoRoot = resolve(fileURLToPath(new URL("../..", import.meta.url)));

test("public repository contains no desktop-client certification detail", () => {
  assert.deepEqual(scanPublicRepository(repoRoot), []);
});

test("nominative protocol and product compatibility statements remain allowed", () => {
  const text = [
    "The service implements the Esri REST query operation.",
    "ArcGIS Pro can consume the response through the compatible interface.",
    "The importer recognizes hosted arcpy notebook definitions.",
  ].join("\n");

  assert.deepEqual(findPublicContentViolations("docs/compatibility.md", text), []);
});

test("desktop-client procedures, artifacts, and result tallies are rejected", () => {
  const prohibited = [
    ["docs/install.md", "Install ArcGIS Pro, activate its license, and click the ribbon."],
    ["evidence/run.md", "ArcGIS Pro desktop replay passed on the runner."],
    ["fixtures/client.aprx", "binary placeholder"],
  ];

  for (const [path, text] of prohibited) {
    const violations = findPublicContentViolations(path, text);
    assert.ok(violations.length > 0, `${path} should violate the public-content policy`);
  }
});
