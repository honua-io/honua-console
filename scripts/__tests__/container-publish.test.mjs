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
    const workflow = (
      await readFile(resolve(root, ".github/workflows/container-publish.yml"), "utf8")
    ).replace(/\r\n/g, "\n");

    assert.match(workflow, /permissions:\r?\n  contents: read\r?\n\r?\nenv:/);
    assert.doesNotMatch(workflow, /^\s*packages:\s*read\s*$/m);
    assert.match(
      workflow,
      /validate:[\s\S]*if: github\.event_name == 'pull_request'[\s\S]*permissions:\r?\n      contents: read/,
    );
    assert.match(
      workflow,
      /Restore Console anonymously from public sources[\s\S]*--configfile \.\.\/\.\.\/NuGet\.config --no-cache/,
    );
    assert.match(workflow, /workflow_run:[\s\S]*workflows:[\s\S]*- CI[\s\S]*- completed/);
    assert.doesNotMatch(workflow, /workflow_dispatch:/);
    assert.match(
      workflow,
      /validate:[\s\S]*group: console-container-pr-\$\{\{ github\.event\.pull_request\.number \}\}[\s\S]*cancel-in-progress: true/,
    );
    assert.match(
      workflow,
      /publish:[\s\S]*group: console-container-publish-trunk[\s\S]*cancel-in-progress: false/,
    );
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
    const attest = workflow.indexOf("Attest verified candidate image provenance");
    const publicPackage = workflow.indexOf("Verify GHCR candidate is publicly pullable");
    const promote = workflow.indexOf("Promote verified digest to release tags");
    assert.ok(smoke >= 0 && promote > smoke, "release tags must be promoted after runtime smoke");
    assert.ok(
      attest > smoke && promote > attest,
      "release tags must be promoted only after candidate provenance is attached",
    );
    assert.ok(
      publicPackage > attest && promote > publicPackage,
      "release tags must be promoted only after public package visibility is verified",
    );
    assert.match(
      workflow,
      /Verify GHCR candidate is publicly pullable[\s\S]*ghcr\.io\/token\?service=ghcr\.io&scope=repository:[\s\S]*manifests\/\$\{candidate_digest\}[\s\S]*\^docker-content-digest:[\s\S]*public_digest\}" == "\$\{candidate_digest\}/,
    );
    assert.match(workflow, /imagetools create --prefer-index=false/);
    assert.match(
      workflow,
      /existing_sha_digest="\$\(docker buildx imagetools inspect "\$\{release_ref\}"[\s\S]*?--format '\{\{\.Manifest\.Digest\}\}' 2>\/dev\/null \|\| true\)"/,
    );
    assert.match(
      workflow,
      /Log in to GHCR[\s\S]*password: \$\{\{ secrets\.GITHUB_TOKEN \}\}[\s\S]*Promote verified digest to release tags/,
    );
    assert.match(
      workflow,
      /existing_sha_digest[\s\S]*Refusing to move immutable tag[\s\S]*exit 1[\s\S]*imagetools create/,
    );
    assert.match(workflow, /released_digest[\s\S]*steps\.image\.outputs\.digest/);
    assert.match(workflow, /nightly_digest[\s\S]*steps\.image\.outputs\.digest/);
    assert.deepEqual(
      [...workflow.matchAll(/uses:\s+\S+@(v\d+)\s*$/gm)].map((match) => match[0]),
      [],
      "all workflow actions must be pinned by commit SHA",
    );
  });
});
