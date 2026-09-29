import { strict as assert } from "node:assert";
import { readFileSync, readdirSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { test } from "node:test";
import { fileURLToPath } from "node:url";

const here = dirname(fileURLToPath(import.meta.url));
const repoRoot = resolve(here, "../..");

const read = (path) => readFileSync(resolve(repoRoot, path), "utf8").replace(/\r\n/g, "\n");

// Honua.Sdk.Studio 1.6.4 is the first version published to nuget.org; anything
// older only ever existed on the private GitHub Packages feed.
const FIRST_PUBLIC_STUDIO_SDK = [1, 6, 4];

const parseVersion = (version) => version.split(".").map((part) => Number.parseInt(part, 10));
const compareVersions = (left, right) => {
  for (let index = 0; index < Math.max(left.length, right.length); index += 1) {
    const delta = (left[index] ?? 0) - (right[index] ?? 0);
    if (delta !== 0) return delta;
  }
  return 0;
};

test("Console restore has one anonymous public package source", () => {
  const config = read("NuGet.config");
  const sources = [...config.matchAll(/<add\s+key="([^"]+)"\s+value="([^"]+)"/g)];

  assert.deepEqual(
    sources.map((match) => [match[1], match[2]]),
    [["nuget.org", "https://api.nuget.org/v3/index.json"]],
  );
  assert.match(config, /<clear\s*\/>/);
  assert.doesNotMatch(config, /github-honua|nuget\.pkg\.github\.com/i);
});

test("Console consumes one publicly published Studio SDK version", () => {
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
  const versions = new Set(sdkReferences.map(([, version]) => version));
  assert.equal(versions.size, 1, `every project must pin the same Studio SDK: ${JSON.stringify(sdkReferences)}`);
  for (const [name, version] of sdkReferences) {
    assert.ok(
      compareVersions(parseVersion(version), FIRST_PUBLIC_STUDIO_SDK) >= 0,
      `${name} pins Honua.Sdk.Studio ${version}, which predates the first public nuget.org release`,
    );
  }
});

test("blocking CI proves a clean credential-free locked restore", () => {
  const workflow = read(".github/workflows/ci.yml");

  assert.match(workflow, /Restore \.NET projects anonymously from public sources/);
  assert.match(
    workflow,
    /dotnet restore Honua\.Console\.slnx --configfile NuGet\.config --no-cache --locked-mode/,
  );
  assert.match(workflow, /NUGET_PACKAGES=\$\{RUNNER_TEMP\}\/honua-console-public-packages[\s\S]*>> "\$GITHUB_ENV"/);
  const dotnetCommands = workflow
    .split("\n")
    .filter((line) => /^\s*(?:run:\s*)?dotnet (?:test|build|publish|format)\b/.test(line));
  assert.ok(dotnetCommands.length > 0, "CI must run dotnet build/test commands");
  for (const command of dotnetCommands) {
    assert.match(command, /--no-restore/, `CI must not implicitly re-restore: ${command.trim()}`);
  }
  assert.match(workflow, /Verify public package locks are current/);
  assert.match(workflow, /git diff --exit-code -- ':\(glob\)\*\*\/packages\.lock\.json'/);
  assert.doesNotMatch(workflow, /Authenticate GitHub Packages|nuget update source github-honua/);
  assert.doesNotMatch(workflow, /^\s*packages:\s*read\s*$/m);
});

test("every Console workflow restores without private package credentials", () => {
  const workflowDir = resolve(repoRoot, ".github/workflows");
  const workflows = readdirSync(workflowDir)
    .filter((name) => /\.ya?ml$/.test(name))
    .map((name) => [name, read(`.github/workflows/${name}`)]);

  for (const [name, workflow] of workflows) {
    assert.doesNotMatch(
      workflow,
      /github-honua|nuget\.pkg\.github\.com|Authenticate GitHub Packages|packages:\s*read/i,
      `${name} must not depend on private package credentials`,
    );
    for (const restore of workflow.split("\n").filter((line) => line.includes("dotnet restore"))) {
      assert.match(restore, /--configfile .*NuGet\.config/, `${name}: ${restore.trim()}`);
      assert.match(restore, /--no-cache/, `${name}: ${restore.trim()}`);
      assert.match(restore, /--locked-mode/, `${name}: ${restore.trim()}`);
    }
  }
});

test("onboarding does not ask public contributors for package credentials", () => {
  const readme = read("README.md");

  assert.match(readme, /Anonymous access to nuget\.org/);
  assert.match(readme, /commit the regenerated `packages\.lock\.json` files/);
  assert.match(readme, /dotnet restore Honua\.Console\.slnx --configfile NuGet\.config --locked-mode/);
  assert.doesNotMatch(readme, /read:packages|nuget\.pkg\.github\.com|github-honua/i);
});
