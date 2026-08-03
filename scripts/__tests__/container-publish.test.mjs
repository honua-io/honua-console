import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import { resolve } from "node:path";
import { describe, test } from "node:test";

const root = resolve(import.meta.dirname, "../..");

describe("Console container publication contract", () => {
  test("packages the published artifact into a non-root ASP.NET runtime", async () => {
    const dockerfile = await readFile(resolve(root, "Dockerfile"), "utf8");

    assert.match(dockerfile, /^FROM mcr\.microsoft\.com\/dotnet\/aspnet:10\.0$/m);
    assert.match(dockerfile, /COPY --chown=app:app artifacts\/honua-console-web\/ \.\//);
    assert.match(dockerfile, /^USER app$/m);
    assert.match(dockerfile, /^EXPOSE 8080$/m);
    assert.match(dockerfile, /ENTRYPOINT \["dotnet", "Honua\.Console\.Web\.dll"\]/);
  });

  test("keeps pull requests read-only and publishes immutable multi-architecture images", async () => {
    const workflow = await readFile(resolve(root, ".github/workflows/container-publish.yml"), "utf8");

    assert.match(workflow, /permissions:\n  contents: read\n  packages: read/);
    assert.match(workflow, /validate:[\s\S]*if: github\.event_name == 'pull_request'[\s\S]*packages: read/);
    assert.match(workflow, /publish:[\s\S]*if: github\.event_name != 'pull_request'[\s\S]*packages: write/);
    assert.match(workflow, /packages: write/);
    assert.match(workflow, /platforms: linux\/amd64,linux\/arm64/);
    assert.match(workflow, /IMAGE_REF: \$\{\{ env\.IMAGE \}\}@\$\{\{ steps\.image\.outputs\.digest \}\}/);
    assert.match(workflow, /127\.0\.0\.1:4174\/version\.json/);
    assert.match(workflow, /payload\["commit"\] == os\.environ\["GITHUB_SHA"\]/);
    assert.match(workflow, /actions\/attest-build-provenance@v3/);
  });
});
