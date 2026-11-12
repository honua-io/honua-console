import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { createHash } from "node:crypto";
import { mkdtemp, mkdir, readFile, rm, symlink, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { dirname, join, resolve } from "node:path";
import { test } from "node:test";
import { fileURLToPath } from "node:url";

const root = resolve(dirname(fileURLToPath(import.meta.url)), "../..");

test("static bundle supersedes stale pull-request heads but never cancels release runs", async () => {
  const workflow = await readFile(join(root, ".github/workflows/static-bundle.yml"), "utf8");
  assert.match(
    workflow,
    /concurrency:\n(?:  #.*\n)*  group: \$\{\{ github\.workflow \}\}-\$\{\{ github\.event_name == 'pull_request' && github\.event\.pull_request\.number \|\| github\.run_id \}\}\n  cancel-in-progress: \$\{\{ github\.event_name == 'pull_request' \}\}\n/,
  );
});

test("static release validates locked assets before packaging and limits release writes", async () => {
  const workflow = await readFile(join(root, ".github/workflows/static-bundle.yml"), "utf8");
  assert.match(workflow, /permissions:\n  contents: read/);
  const release = workflow.indexOf("  release:\n    name:");
  assert.ok(release > 0);
  assert.doesNotMatch(workflow.slice(0, release), /contents: write|packages: write|pull_request_target:/);
  assert.match(workflow.slice(release), /if: github.event_name == 'release'/);
  assert.match(workflow.slice(release), /contents: write/);
  assert.match(workflow, /--configfile NuGet.config --no-cache --locked-mode/);
  assert.match(workflow, /cp src\/Honua.Console.Web\/global.json global.json/);
  assert.match(workflow, /--repo honua-io\/honua-studio/);
  assert.match(workflow, /--signer-workflow honua-io\/honua-studio\/\.github\/workflows\/release.yml/);
  assert.match(workflow, /--source-ref/);
  assert.match(workflow, /--source-digest/);
  assert.match(workflow, /--deny-self-hosted-runners/);
  const publish = workflow.indexOf("dotnet publish");
  const verifyCesium = workflow.indexOf("node scripts/verify-published-cesium.mjs");
  const verifyStudio = workflow.indexOf("--verify artifacts/static-publish/wwwroot/console/_studio");
  const browser = workflow.indexOf("playwright test --config playwright.static.config.ts");
  const pack = workflow.indexOf("python3 scripts/package-console-static.py");
  assert.ok(workflow.indexOf("gh attestation verify") < publish);
  assert.ok(publish < verifyCesium && verifyCesium < pack);
  assert.ok(publish < verifyStudio && verifyStudio < pack);
  assert.ok(verifyCesium < browser && verifyStudio < browser && browser < pack);
  assert.match(workflow, /gh release upload "\$RELEASE_TAG" static-release\/\* <\/dev\/null/);
  assert.doesNotMatch(workflow, /--clobber/);
  for (const match of workflow.matchAll(/uses:\s+([^\s]+)/g)) {
    assert.ok(match[1].startsWith("./") || /@[a-f0-9]{40}$/.test(match[1]), match[1]);
  }
});

test("release ZIP contains browser files only and its receipt verifies the actual bytes", async () => {
  const directory = await mkdtemp(join(tmpdir(), "console-static-"));
  const source = join(directory, "publish", "wwwroot", "console");
  const output = join(directory, "release");
  const run = (...extra) => spawnSync("python3", [join(root, "scripts/package-console-static.py"), source,
    "--output", output, "--version", "0.1.0-rc.2", ...extra], { encoding: "utf8" });
  try {
    await mkdir(join(source, "_framework"), { recursive: true });
    await writeFile(join(source, "index.html"), '<base href="/console/">');
    await writeFile(join(source, "config.json"), '{"apiBaseUrl":"/"}');
    await writeFile(join(source, "_framework", "blazor.webassembly.js"), "// runtime");
    await writeFile(join(directory, "publish", "host-secret.json"), "not a browser file");
    const result = run();
    assert.equal(result.status, 0, result.stderr);
    const receipt = JSON.parse(result.stdout);
    const bundle = await readFile(join(output, receipt.asset));
    assert.equal(receipt.files, 3);
    assert.equal(receipt.zipBytes, bundle.length);
    assert.equal(receipt.sha256, createHash("sha256").update(bundle).digest("hex"));
    assert.deepEqual(JSON.parse(await readFile(join(output, `${receipt.asset}.json`))), receipt);
    const inspect = spawnSync("python3", ["-c", "import json,sys,zipfile; print(json.dumps(zipfile.ZipFile(sys.argv[1]).namelist()))", join(output, receipt.asset)], { encoding: "utf8" });
    assert.equal(inspect.status, 0, inspect.stderr);
    assert.deepEqual(JSON.parse(inspect.stdout), ["_framework/blazor.webassembly.js", "config.json", "index.html"]);
    assert.notEqual(run("--version", "../../escape").status, 0);
    assert.notEqual(run("--output", join(source, "release")).status, 0);
    await symlink(join(directory, "publish", "host-secret.json"), join(source, "secret.json"));
    assert.notEqual(run().status, 0, "symlinked files must not enter a release");
    await rm(join(source, "secret.json"));
    await writeFile(join(source, "index.html"), '<base href="/">');
    assert.notEqual(run().status, 0, "the root host base cannot ship under /console/");
    await rm(join(source, "config.json"));
    assert.notEqual(run().status, 0, "missing deployment configuration must fail packaging");
  } finally {
    await rm(directory, { recursive: true, force: true });
  }
});
