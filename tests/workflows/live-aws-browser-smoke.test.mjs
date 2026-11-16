import assert from "node:assert/strict";
import { mkdtempSync, readFileSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { spawnSync } from "node:child_process";
import test from "node:test";

const workflow = readFileSync(new URL("../../.github/workflows/console-aws-browser.yml", import.meta.url), "utf8");
const script = workflow.split("        run: |\n")[1]
  .split("\n").map((line) => line.replace(/^          /, "")).join("\n");

test("an uninstalled AWS gate records a skip without claiming browser qualification", () => {
  const directory = mkdtempSync(join(tmpdir(), "console-aws-preflight-"));
  try {
    const summary = join(directory, "summary");
    const result = spawnSync("bash", ["--noprofile", "--norc", "-e", "-o", "pipefail", "-c", script], {
      encoding: "utf8",
      env: { ...process.env, GITHUB_STEP_SUMMARY: summary },
    });
    assert.equal(result.status, 0, result.stderr);
    const reason = readFileSync(summary, "utf8");
    assert.match(reason, /^SKIPPED:.*Launch Stack gate install/);
    assert.match(reason, /candidate server image, published static Console/);
    assert.match(reason, /stack-created Cognito test operator/);
    assert.match(reason, /No browser journey ran; this is not a live qualification pass/);
    assert.equal(result.stdout, `::notice::${reason}`);
  } finally {
    rmSync(directory, { recursive: true, force: true });
  }
});

test("the unavailable gate does not run legacy journeys or publish a passing live browser check", () => {
  assert.match(workflow, /schedule:\n    - cron:/);
  assert.match(workflow, /workflow_dispatch: \{\}/);
  assert.doesNotMatch(workflow, /\n  live-aws-browser:|secrets\.|demo\.honua\.io|LegacyHost|e2e:aws/);
  assert.doesNotMatch(workflow, /uses:|dotnet|npm|continue-on-error|exit 78|\|\| true/);
});
