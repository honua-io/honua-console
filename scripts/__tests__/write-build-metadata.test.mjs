import { execFileSync } from "node:child_process";
import { strict as assert } from "node:assert";
import { mkdtempSync, readFileSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { dirname, resolve } from "node:path";
import { test } from "node:test";
import { fileURLToPath } from "node:url";

const here = dirname(fileURLToPath(import.meta.url));
const repoRoot = resolve(here, "../..");
const script = resolve(repoRoot, "scripts/write-build-metadata.mjs");

test("write-build-metadata stamps version, commit, ref, legacy status, and areas", () => {
  const artifactDir = mkdtempSync(resolve(tmpdir(), "honua-console-artifact-"));

  try {
    execFileSync(process.execPath, [script], {
      cwd: repoRoot,
      env: {
        ...process.env,
        HONUA_CONSOLE_ARTIFACT_DIR: artifactDir,
        HONUA_CONSOLE_COMMIT_SHA: "0123456789abcdef0123456789abcdef01234567",
        HONUA_CONSOLE_REF: "release/2026.06",
        HONUA_CONSOLE_LEGACY_PORTAL_STATUS: "retiring",
        HONUA_CONSOLE_LEGACY_ADMIN_STATUS: "active",
      },
      stdio: ["ignore", "pipe", "pipe"],
    });

    const metadata = JSON.parse(readFileSync(resolve(artifactDir, "version.json"), "utf8"));
    const areas = JSON.parse(readFileSync(resolve(repoRoot, "config/console-areas.json"), "utf8"));

    assert.equal(metadata.name, "honua-console");
    assert.equal(metadata.version, "0.1.0");
    assert.equal(metadata.commit, "0123456789abcdef0123456789abcdef01234567");
    assert.equal(metadata.shortCommit, "0123456789ab");
    assert.equal(metadata.ref, "release/2026.06");
    assert.deepEqual(metadata.legacy, { portal: "retiring", admin: "active" });
    assert.deepEqual(metadata.areas, areas);
    assert.equal(Number.isNaN(Date.parse(metadata.builtAt)), false);
  } finally {
    rmSync(artifactDir, { recursive: true, force: true });
  }
});
