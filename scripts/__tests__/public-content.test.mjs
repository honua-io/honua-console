import { strict as assert } from "node:assert";
import { resolve } from "node:path";
import { test } from "node:test";
import { fileURLToPath } from "node:url";

import {
  findPublicContentViolations,
  isPublicTextPath,
  scanPublicRepository,
} from "../check-public-content.mjs";

const repoRoot = resolve(fileURLToPath(new URL("../..", import.meta.url)));

test("public repository contains no desktop-client certification detail", () => {
  assert.deepEqual(scanPublicRepository(repoRoot), []);
});

test("maintained documentation formats are scanned", () => {
  for (const path of ["docs/page.md", "docs/page.mdx", "docs/page.rst", "docs/page.txt"]) {
    assert.equal(isPublicTextPath(path), true, path);
  }
  assert.equal(isPublicTextPath("docs/figure.png"), false);
});

test("nominative protocol and product compatibility statements remain allowed", () => {
  const text = [
    "The service implements the Esri REST query operation.",
    "ArcGIS Pro can consume the response through the compatible interface.",
    "The importer recognizes hosted arcpy notebook definitions.",
  ].join("\n");

  assert.deepEqual(findPublicContentViolations("docs/compatibility.md", text), []);
  assert.deepEqual(
    findPublicContentViolations(
      "docs/onboarding.md",
      "Install Honua Console. ArcGIS Pro can consume the response",
    ),
    [],
  );
  assert.deepEqual(
    findPublicContentViolations(
      "docs/onboarding.md",
      "Click the ribbon. ArcGIS Pro can consume the response",
    ),
    [],
  );
});

test("desktop-client procedures, artifacts, and result tallies are rejected", () => {
  const prohibited = [
    ["docs/install.md", "Install ArcGIS Pro, activate its license, and click the ribbon.", "desktop-client-procedure"],
    ["docs/screenshot.md", "Screenshot of ArcGIS Pro with the toolbox open.", "desktop-client-procedure"],
    ["docs/license.md", "ArcGIS Pro license manager checkout is private.", "desktop-client-procedure"],
    ["evidence/run.md", "ArcGIS Pro desktop replay passed on the runner.", "desktop-client-result"],
    ["evidence/tally.md", "ArcGIS Pro tests: 12 passed, 1 failed", "desktop-client-result"],
    ["evidence/plural.md", "ArcGIS Pro tests passed", "desktop-client-result"],
    ["evidence/count.md", "12 passed, 1 failed against ArcGIS Pro", "desktop-client-result"],
    ["fixtures/client.aprx", "binary placeholder", "desktop-project-artifact"],
  ];

  for (const [path, text, rule] of prohibited) {
    const violations = findPublicContentViolations(path, text);
    assert.ok(
      violations.some((violation) => violation.rule === rule),
      `${path} should violate ${rule}, got ${violations.map((violation) => violation.rule).join(",") || "no violations"}`,
    );
  }
});
