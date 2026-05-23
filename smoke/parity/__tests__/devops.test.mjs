import { strict as assert } from "node:assert";
import { test } from "node:test";
import { fileURLToPath } from "node:url";

import { isLocalOrigin, loadBuildArtifact } from "../adapters/devops.mjs";

const REPO_ROOT = fileURLToPath(new URL("../../../", import.meta.url));

test("IPv6 loopback origins stay on the local/offline build-artifact path", async () => {
  assert.equal(isLocalOrigin("http://[::1]:4174"), true);

  let fetchCalled = false;
  const artifact = await loadBuildArtifact({
    repoRoot: REPO_ROOT,
    originUrl: "http://[::1]:4174",
    fetchImpl: async () => {
      fetchCalled = true;
      throw new Error("IPv6 loopback origin must not be fetched");
    },
  });

  assert.equal(fetchCalled, false);
  assert.notEqual(artifact.source, "origin");
  assert.equal(artifact.metadata.name, "honua-console");
});
