import assert from "node:assert/strict";
import { mkdtempSync, readFileSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { spawnSync } from "node:child_process";
import test from "node:test";

const workflow = readFileSync(new URL("../../.github/workflows/console-aws-browser.yml", import.meta.url), "utf8");
const [preflight, browser] = workflow.split("\n  live-aws-browser:\n");
const script = preflight.split("        run: |\n")[1]
  .split("\n").map((line) => line.replace(/^          /, "")).join("\n");
const username = "HONUA_CONSOLE_AWS_OPERATOR_USERNAME";
const password = "HONUA_CONSOLE_AWS_OPERATOR_PASSWORD";

for (const [label, user, pass, provisioned] of [
  ["both absent", "", "", false],
  ["username absent", "", "test-password", false],
  ["password absent", "test-operator", "", false],
  ["both present", "test-operator", "test-password", true],
]) {
  test(`operator precondition: ${label}`, () => {
    const directory = mkdtempSync(join(tmpdir(), "console-aws-preflight-"));
    try {
      const output = join(directory, "output");
      const summary = join(directory, "summary");
      const result = spawnSync("bash", ["--noprofile", "--norc", "-e", "-o", "pipefail", "-c", script], {
        encoding: "utf8",
        env: { ...process.env, [username]: user, [password]: pass,
          GITHUB_OUTPUT: output, GITHUB_STEP_SUMMARY: summary },
      });
      assert.equal(result.status, 0, result.stderr);
      assert.equal(readFileSync(output, "utf8"), `provisioned=${provisioned}\n`);
      if (!provisioned) {
        const reason = readFileSync(summary, "utf8");
        assert.equal(reason.trim().split("\n").length, 1);
        assert.match(reason, /SKIPPED: live-aws-browser/);
        assert.ok(reason.includes(username) && reason.includes(password));
        assert.match(reason, /configure both repository secrets.*rerun the workflow/);
        assert.equal(result.stdout, `::notice::${reason}`);
      } else {
        assert.equal(result.stdout, "");
      }
      assert.ok(!result.stdout.includes("test-password"));
      assert.ok(!result.stdout.includes("test-operator"));
    } finally {
      rmSync(directory, { recursive: true, force: true });
    }
  });
}

test("the unchanged browser check is skipped at job level before setup", () => {
  assert.match(preflight, /outputs:\n      provisioned: \$\{\{ steps\.operator-secrets\.outputs\.provisioned \}\}/);
  for (const secret of [username, password]) {
    assert.ok(preflight.includes(`${secret}: \${{ secrets.${secret} }}`));
  }
  assert.doesNotMatch(preflight, /uses:|checkout|dotnet|npm/);
  assert.match(browser, /^    needs: operator-preflight\n    if: needs\.operator-preflight\.outputs\.provisioned == 'true'\n/);
  assert.match(browser, /run: npm run e2e:aws/);
  assert.match(browser, /if: always\(\)/);
  assert.match(browser, /if-no-files-found: error/);
  assert.doesNotMatch(workflow, /continue-on-error|exit 78|\|\| true/);
});
