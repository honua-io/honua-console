import { strict as assert } from "node:assert";
import { existsSync, readFileSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { test } from "node:test";
import { fileURLToPath } from "node:url";

// AUD-104 (honua-console#239) — release-readiness licensing compliance gate.
//
// honua-console declares a license in package.json but historically shipped no LICENSE/NOTICE text,
// and its declaration diverged from the rest of the Honua platform (honua-server and the other
// source-available components ship under the Elastic License 2.0). This test makes the license a
// CI-enforced invariant so the declaration, the license text, and the notice can never drift again:
//   - a LICENSE file exists and is the Elastic License 2.0 text,
//   - a NOTICE file exists,
//   - package.json "license" is the SPDX identifier "Elastic-2.0",
//   - the AGENTS.md/CLAUDE.md contributor declaration matches.
// If the platform later chooses a different license for the Console, update EXPECTED_* here and the
// LICENSE/NOTICE/package.json/AGENTS.md together — the gate forces all four to move in lockstep.

const here = dirname(fileURLToPath(import.meta.url));
const repoRoot = resolve(here, "../..");

const EXPECTED_SPDX = "Elastic-2.0";
const EXPECTED_LICENSE_TITLE = "Elastic License 2.0 (ELv2)";

test("LICENSE file exists and is the Elastic License 2.0", () => {
  const licensePath = resolve(repoRoot, "LICENSE");
  assert.ok(existsSync(licensePath), "a LICENSE file must exist at the repo root");
  const text = readFileSync(licensePath, "utf8");
  assert.ok(
    text.startsWith(EXPECTED_LICENSE_TITLE),
    `LICENSE must be the ${EXPECTED_LICENSE_TITLE} text`,
  );
  // Spot-check a clause unique to ELv2 so an empty/placeholder file cannot pass.
  assert.match(text, /hosted or managed\s+service/i);
});

test("NOTICE file exists", () => {
  const noticePath = resolve(repoRoot, "NOTICE");
  assert.ok(existsSync(noticePath), "a NOTICE file must exist at the repo root");
  const text = readFileSync(noticePath, "utf8");
  assert.match(text, /Elastic License 2\.0/);
});

test("package.json declares the Elastic-2.0 SPDX identifier", () => {
  const pkg = JSON.parse(readFileSync(resolve(repoRoot, "package.json"), "utf8"));
  assert.equal(
    pkg.license,
    EXPECTED_SPDX,
    `package.json "license" must be the SPDX id "${EXPECTED_SPDX}"`,
  );
});

test("AGENTS.md declares the Elastic License 2.0", () => {
  const agents = readFileSync(resolve(repoRoot, "AGENTS.md"), "utf8");
  assert.match(
    agents,
    /License:\s*Elastic License 2\.0/,
    "AGENTS.md must declare the Elastic License 2.0",
  );
});
