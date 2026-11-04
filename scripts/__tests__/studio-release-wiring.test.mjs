import { strict as assert } from "node:assert";
import { readdirSync, readFileSync } from "node:fs";
import { resolve } from "node:path";
import { test } from "node:test";

import { STUDIO_REPOSITORY, STUDIO_SIGNER_WORKFLOW } from "../lib/studio-release.mjs";

const repoRoot = resolve(import.meta.dirname, "../..");
const workflowDirectory = resolve(repoRoot, ".github/workflows");
const workflows = readdirSync(workflowDirectory)
  .filter((name) => name.endsWith(".yml"))
  .map((name) => ({ name, lines: readFileSync(resolve(workflowDirectory, name), "utf8").split("\n") }));

const FETCH_ACTION = "uses: ./.github/actions/fetch-studio-release";
const PUBLISHED_STUDIO_VERIFICATION = "node scripts/fetch-studio-release.mjs --verify artifacts/honua-console-web/wwwroot/_studio";
const BUILDS_WEB_HOST = /\bdotnet (?:build|publish|test|run)\b|\bplaywright test\b|e2e\/run-live/;
const JOB_HEADER = /^ {2}([A-Za-z0-9_-]+):\s*$/;

function* jobCommands(lines) {
  let job;
  let fetched = false;
  const jobsStart = lines.indexOf("jobs:");
  for (const [index, line] of lines.entries()) {
    if (index <= jobsStart) continue;
    const header = line.match(JOB_HEADER);
    if (header) [job, fetched] = [header[1], false];
    if (line.includes(FETCH_ACTION)) fetched = true;
    if (!line.trim().startsWith("#")) yield { job, fetched, index, line };
  }
}

test("every job that builds, tests, runs or publishes the Web host fetches the locked Studio release first", () => {
  const unfetched = workflows.flatMap(({ name, lines }) => [...jobCommands(lines)]
    .filter(({ fetched, line }) => BUILDS_WEB_HOST.test(line) && !fetched)
    .map(({ job, index }) => `${name}:${index + 1} (${job})`));
  assert.deepEqual(unfetched, []);
});

test("every published Console artifact is verified against the Studio release lock", () => {
  let publishes = 0;
  for (const { name, lines } of workflows) {
    const sequence = lines
      .filter((line) => line.includes("dotnet publish") || line.includes(PUBLISHED_STUDIO_VERIFICATION))
      .map((line) => (line.includes("dotnet publish") ? "publish" : "verify"));
    publishes += sequence.filter((step) => step === "publish").length;
    const alternates = sequence.every((step, index) => step === (index % 2 === 0 ? "publish" : "verify"));
    assert.ok(alternates && sequence.length % 2 === 0, `${name} must verify each publish before the next: ${sequence}`);
  }
  assert.ok(publishes >= 5, "the deployment, e2e and live workflows publish the Console");
});

test("CI verifies the release attestation against the signer, tag and commit in the lock", () => {
  const ci = readFileSync(resolve(workflowDirectory, "ci.yml"), "utf8");
  const job = ci.slice(ci.indexOf("  studio-release-attestation:"), ci.indexOf("  validate:"));
  assert.match(job, /permissions:\n {6}attestations: read\n {6}contents: read\n/);
  assert.match(job, /GH_TOKEN: \$\{\{ github\.token \}\}/);
  assert.match(job, /gh attestation verify "\$ARCHIVE"/);
  assert.ok(job.includes(`--repo ${STUDIO_REPOSITORY} \\\n`), "the attested repository is fixed in CI");
  assert.ok(job.includes(`--signer-workflow ${STUDIO_SIGNER_WORKFLOW} \\\n`), "the signer workflow is fixed in CI");
  assert.doesNotMatch(job, /jq -r \.(?:repository|signerWorkflow)/);
  assert.ok(job.includes('--source-digest "$(jq -r .sourceCommit "$LOCK")"'));
  assert.ok(job.includes('--source-ref "refs/tags/$(jq -r .tag "$LOCK")"'));
  assert.match(job, /--deny-self-hosted-runners/);
});

test("the Web project vendors the Studio release before Razor inventories static assets", () => {
  const project = readFileSync(resolve(repoRoot, "src/Honua.Console.Web/Honua.Console.Web.csproj"), "utf8");
  assert.match(project, /<Target Name="FetchStudioRelease" BeforeTargets="ResolveProjectStaticWebAssets"/);
  assert.match(project, /<Exec Command="node &quot;scripts\/fetch-studio-release\.mjs&quot;"/);
  assert.match(project, /<Content Include="wwwroot\/_studio\/\*\*\/\*" Exclude="@\(Content\)" \/>/);
});
