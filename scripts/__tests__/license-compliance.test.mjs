import { strict as assert } from "node:assert";
import { existsSync, readFileSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { test } from "node:test";
import { fileURLToPath } from "node:url";

// AUD-104 (honua-console#239) — release-readiness licensing compliance gate.
//
// honua-console declares a license in package.json but historically shipped no LICENSE/NOTICE text.
// This test makes the license a CI-enforced invariant so the declaration, the license text, and the
// notice can never drift again:
//   - a LICENSE file exists and is the Apache License 2.0 text,
//   - a NOTICE file exists,
//   - package.json "license" is the SPDX identifier "Apache-2.0",
//   - the AGENTS.md/CLAUDE.md contributor declaration matches.
// If the platform later chooses a different license for the Console, update EXPECTED_* here and the
// LICENSE/NOTICE/package.json/AGENTS.md together — the gate forces all four to move in lockstep.

const here = dirname(fileURLToPath(import.meta.url));
const repoRoot = resolve(here, "../..");

const EXPECTED_SPDX = "Apache-2.0";

test("LICENSE file exists and is the Apache License 2.0", () => {
  const licensePath = resolve(repoRoot, "LICENSE");
  assert.ok(existsSync(licensePath), "a LICENSE file must exist at the repo root");
  const text = readFileSync(licensePath, "utf8");
  assert.match(text, /Apache License/, "LICENSE must be the Apache License 2.0 text");
  assert.match(text, /Version 2\.0, January 2004/, "LICENSE must be Apache License version 2.0");
  // Spot-check clauses unique to Apache-2.0 so an empty/placeholder file cannot pass.
  assert.match(text, /Grant of Patent License/);
  assert.match(text, /Licensed under the Apache License, Version 2\.0/);
});

test("NOTICE file exists", () => {
  const noticePath = resolve(repoRoot, "NOTICE");
  assert.ok(existsSync(noticePath), "a NOTICE file must exist at the repo root");
  const text = readFileSync(noticePath, "utf8");
  assert.match(text, /Apache License, Version 2\.0/);
});

test("package.json declares the Apache-2.0 SPDX identifier", () => {
  const pkg = JSON.parse(readFileSync(resolve(repoRoot, "package.json"), "utf8"));
  assert.equal(
    pkg.license,
    EXPECTED_SPDX,
    `package.json "license" must be the SPDX id "${EXPECTED_SPDX}"`,
  );
});

test("AGENTS.md declares the Apache License 2.0", () => {
  const agents = readFileSync(resolve(repoRoot, "AGENTS.md"), "utf8");
  assert.match(
    agents,
    /License:\s*Apache License 2\.0/,
    "AGENTS.md must declare the Apache License 2.0",
  );
});
