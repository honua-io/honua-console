import { strict as assert } from "node:assert";
import { readFileSync, readdirSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { test } from "node:test";
import { fileURLToPath } from "node:url";

const here = dirname(fileURLToPath(import.meta.url));
const repoRoot = resolve(here, "../..");

const read = (path) => readFileSync(resolve(repoRoot, path), "utf8");

test("Console restore has one anonymous public package source", () => {
  const config = read("NuGet.config");
  const sources = [...config.matchAll(/<add\s+key="([^"]+)"\s+value="([^"]+)"/g)];

  assert.deepEqual(
    sources.map((match) => [match[1], match[2]]),
    [["nuget.org", "https://api.nuget.org/v3/index.json"]],
  );
  assert.doesNotMatch(config, /github-honua|nuget\.pkg\.github\.com/i);
});

test("Console pins the 2026.1 public SDK train", () => {
  const sourceRoot = resolve(repoRoot, "src");
  const sdkReferences = readdirSync(sourceRoot, { recursive: true })
    .filter((name) => name.endsWith(".csproj"))
    .flatMap((name) => {
      const project = readFileSync(resolve(sourceRoot, name), "utf8");
      return [...project.matchAll(
        /<PackageReference\s+Include="Honua\.Sdk\.Studio"\s+Version="([^"]+)"\s*\/>/g,
      )].map((match) => [name, match[1]]);
    });

  assert.ok(sdkReferences.length > 0, "at least one Console project must consume Studio SDK");
  for (const [name, version] of sdkReferences) {
    assert.equal(version, "1.6.0", `${name} must pin the 2026.1 public SDK train`);
  }
});

test("blocking CI proves a clean credential-free restore", () => {
  const workflow = read(".github/workflows/ci.yml");

  assert.match(workflow, /Restore \.NET projects anonymously from public sources/);
  assert.match(workflow, /dotnet restore Honua\.Console\.slnx --configfile NuGet\.config --no-cache/);
  assert.match(workflow, /NUGET_PACKAGES: \$\{\{ runner\.temp \}\}\/honua-console-public-packages/);
  assert.match(workflow, /Verify public package locks are current/);
  assert.match(workflow, /git diff --exit-code -- ':\(glob\)\*\*\/packages\.lock\.json'/);
  assert.doesNotMatch(workflow, /Authenticate GitHub Packages|nuget update source github-honua/);
  assert.doesNotMatch(workflow, /^\s*packages:\s*read\s*$/m);
});

test("every Console workflow restores without private package credentials", () => {
  const workflowDir = resolve(repoRoot, ".github/workflows");
  const workflows = readdirSync(workflowDir)
    .filter((name) => /\.ya?ml$/.test(name))
    .map((name) => [name, readFileSync(resolve(workflowDir, name), "utf8")]);

  for (const [name, workflow] of workflows) {
    assert.doesNotMatch(
      workflow,
      /github-honua|nuget\.pkg\.github\.com|Authenticate GitHub Packages|packages:\s*read/i,
      `${name} must not depend on private package credentials`,
    );
    for (const restore of workflow.split(/\r?\n/).filter((line) => line.includes("dotnet restore"))) {
      assert.match(restore, /--configfile .*NuGet\.config/);
      assert.match(restore, /--no-cache/);
    }
  }
});

test("onboarding does not ask public contributors for package credentials", () => {
  const readme = read("README.md");

  assert.match(readme, /pins `Honua\.Sdk\.Studio` 1\.6\.0/);
  assert.match(readme, /intentionally fails restore until that exact version is available/);
  assert.match(readme, /commit the regenerated `packages\.lock\.json` files/);
  assert.match(readme, /dotnet restore Honua\.Console\.slnx --configfile NuGet\.config/);
  assert.doesNotMatch(readme, /read:packages|nuget\.pkg\.github\.com|github-honua/i);
});
