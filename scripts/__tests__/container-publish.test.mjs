import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import { dirname, resolve } from "node:path";
import { describe, test } from "node:test";
import { fileURLToPath } from "node:url";

const root = resolve(dirname(fileURLToPath(import.meta.url)), "../..");

describe("Console container publication contract", () => {
  test("packages the published artifact into a non-root ASP.NET runtime", async () => {
    const dockerfile = await readFile(resolve(root, "Dockerfile"), "utf8");

    assert.match(
      dockerfile,
      /^FROM mcr\.microsoft\.com\/dotnet\/aspnet:10\.0@sha256:[0-9a-f]{64}$/m,
    );
    assert.match(dockerfile, /COPY --chown=app:app artifacts\/honua-console-web\/ \.\//);
    assert.match(dockerfile, /^USER app$/m);
    assert.match(dockerfile, /^EXPOSE 8080$/m);
    assert.match(dockerfile, /ENTRYPOINT \["dotnet", "Honua\.Console\.Web\.dll"\]/);
  });

  test("keeps pull requests read-only and publishes immutable multi-architecture images", async () => {
    const workflow = await readFile(resolve(root, ".github/workflows/container-publish.yml"), "utf8");

    assert.match(workflow, /permissions:\n  contents: read\n  packages: read/);
    assert.match(workflow, /validate:[\s\S]*if: github\.event_name == 'pull_request'[\s\S]*packages: read/);
    assert.match(workflow, /workflow_run:[\s\S]*workflows:[\s\S]*- CI[\s\S]*- completed/);
    assert.doesNotMatch(workflow, /workflow_dispatch:/);
    assert.match(
      workflow,
      /publish:[\s\S]*github\.event\.workflow_run\.conclusion == 'success'[\s\S]*github\.event\.workflow_run\.event == 'push'[\s\S]*packages: write/,
    );
    assert.match(workflow, /with:\n\s+ref: trunk/);
    assert.match(
      workflow,
      /Verify successful CI belongs to checked-out trunk head[\s\S]*git rev-parse HEAD[\s\S]*SOURCE_SHA/,
    );
    assert.match(workflow, /packages: write/);
    assert.match(workflow, /platforms: linux\/amd64,linux\/arm64/);
    assert.match(workflow, /IMAGE_REF: \$\{\{ env\.IMAGE \}\}@\$\{\{ steps\.image\.outputs\.digest \}\}/);
    assert.match(workflow, /127\.0\.0\.1:4174\/version\.json/);
    assert.match(workflow, /payload\["commit"\] == os\.environ\["SOURCE_SHA"\]/);
    assert.match(workflow, /actions\/attest-build-provenance@[0-9a-f]{40} # v3/);

    const smoke = workflow.indexOf("Smoke-test the published Console");
    const promote = workflow.indexOf("Promote verified digest to release tags");
    assert.ok(smoke >= 0 && promote > smoke, "release tags must be promoted after runtime smoke");
    assert.match(workflow, /imagetools create --prefer-index=false/);
    assert.match(workflow, /released_digest[\s\S]*steps\.image\.outputs\.digest/);
    assert.deepEqual(
      [...workflow.matchAll(/uses:\s+\S+@(v\d+)\s*$/gm)].map((match) => match[0]),
      [],
      "all workflow actions must be pinned by commit SHA",
    );
  });
});
