import { strict as assert } from "node:assert";
import { readFileSync } from "node:fs";
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
  const project = read("src/Honua.Console.Shell/Honua.Console.Shell.csproj");
  assert.match(
    project,
    /<PackageReference\s+Include="Honua\.Sdk\.Studio"\s+Version="1\.6\.0"\s*\/>/,
  );
});

test("blocking CI proves a clean credential-free restore", () => {
  const workflow = read(".github/workflows/ci.yml");

  assert.match(workflow, /Restore \.NET projects anonymously from public sources/);
  assert.match(workflow, /dotnet restore Honua\.Console\.slnx --configfile NuGet\.config --no-cache/);
  assert.match(workflow, /NUGET_PACKAGES: \$\{\{ runner\.temp \}\}\/honua-console-public-packages/);
  assert.doesNotMatch(workflow, /Authenticate GitHub Packages|nuget update source github-honua/);
  assert.doesNotMatch(workflow, /^\s*packages:\s*read\s*$/m);
});

test("onboarding does not ask public contributors for package credentials", () => {
  const readme = read("README.md");

  assert.match(readme, /pins `Honua\.Sdk\.Studio` 1\.6\.0/);
  assert.match(readme, /intentionally fails restore until that exact version is available/);
  assert.match(readme, /dotnet restore Honua\.Console\.slnx --configfile NuGet\.config/);
  assert.doesNotMatch(readme, /read:packages|nuget\.pkg\.github\.com|github-honua/i);
});
