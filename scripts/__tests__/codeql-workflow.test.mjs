import { strict as assert } from "node:assert";
import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import { test } from "node:test";
import { fileURLToPath } from "node:url";

// honua-server#4428 — the Console CodeQL gate has to live in the repo, pinned
// the way honua-server's codeql.yml is pinned. Default setup is not a reviewed
// workflow and cannot run a security-extended pass beside an advanced workflow.
// This test is the pin: a floating tag, a missing javascript-typescript lane,
// or a pull request that opts into the weekly suite fails `npm test`.

const repoRoot = resolve(fileURLToPath(new URL("../..", import.meta.url)));
const workflowPath = resolve(repoRoot, ".github/workflows/codeql.yml");
const PINNED_USE = /^[A-Za-z0-9_.-]+(?:\/[A-Za-z0-9_.-]+)+@[0-9a-f]{40} # \S.*$/u;

test("CodeQL workflow pins javascript-typescript to full commit SHAs", () => {
  const workflow = readFileSync(workflowPath, "utf8");
  const uses = [...workflow.matchAll(/^\s*uses:\s*(\S(?:.*\S)?)\s*$/gmu)].map((match) => match[1]);

  assert.ok(uses.length >= 3, "workflow must checkout, initialize, and analyze");
  for (const use of uses) {
    assert.match(use, PINNED_USE, `unpinned action: ${use}`);
  }

  assert.match(workflow, /branches:\s*\[trunk\]/u);
  assert.match(workflow, /languages:\s*\$\{\{\s*matrix\.language\s*\}\}/u);
  assert.match(workflow, /- javascript-typescript\n/u);
  assert.match(workflow, /build-mode:\s*none/u);
  assert.doesNotMatch(workflow, /dependabot\[bot\]/u);

  const pullRequestInit = workflow
    .split("Initialize CodeQL (pull request)")[1]
    ?.split("Initialize CodeQL (full scan)")[0];
  const fullScanInit = workflow.split("Initialize CodeQL (full scan)")[1]?.split("Perform CodeQL Analysis")[0];
  assert.ok(pullRequestInit, "pull-request init step missing");
  assert.ok(fullScanInit, "full-scan init step missing");
  assert.doesNotMatch(pullRequestInit, /queries:/u);
  assert.match(fullScanInit, /queries:\s*security-extended/u);
});
