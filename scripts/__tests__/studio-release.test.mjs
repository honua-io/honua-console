import { strict as assert } from "node:assert";
import { createHash } from "node:crypto";
import { existsSync } from "node:fs";
import { mkdtemp, readFile, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { after, test } from "node:test";
import { gzipSync } from "node:zlib";

import {
  RELEASE_LIMITS,
  fetchStudioRelease,
  parseStudioReleaseLock,
  readStudioReleaseLock,
  releaseAssetUrl,
  verifyStudioAssets,
} from "../lib/studio-release.mjs";
import { tarEntry, tarGz } from "./helpers/tar-fixtures.mjs";

const scratch = await mkdtemp(join(tmpdir(), "honua-console-studio-release-"));
after(() => rm(scratch, { recursive: true, force: true }));

const SDK_REVISION = "b".repeat(40);
const RELEASE_FILES = {
  "console.js": "export const createWorkspaceAgentSession = () => {};\n",
  "style.css": "honua-studio-app { display: block; }\n",
  LICENSE: "Apache License\n",
  "THIRD-PARTY-NOTICES.txt": "notices\n",
  "SDK-PROVENANCE.json": `${JSON.stringify({ sourceCommit: SDK_REVISION })}\n`,
  "maputnik/index.html": "<!doctype html>\n",
};

function releaseArchive(files = RELEASE_FILES) {
  return tarGz(
    tarEntry("./", { type: "5" }),
    ...Object.entries(files).map(([name, text]) => tarEntry(`./${name}`, { body: Buffer.from(text) })),
  );
}

function lockFor(archive, overrides = {}) {
  return parseStudioReleaseLock(JSON.stringify({
    schemaVersion: "honua.console.studio-release-lock/v1",
    repository: "honua-io/honua-studio",
    tag: "v9.9.9",
    asset: "honua-studio-console-assets-v9.9.9.tar.gz",
    sha256: createHash("sha256").update(archive).digest("hex"),
    sourceCommit: "a".repeat(40),
    signerWorkflow: "honua-io/honua-studio/.github/workflows/release.yml",
    sdkRevision: SDK_REVISION,
    ...overrides,
  }));
}

const serving = (archive) => async () => new Response(archive);
const unreachable = async () => assert.fail("the release must not be downloaded");

test("the repository lock pins an attested honua-studio release asset", async () => {
  const lock = await readStudioReleaseLock();
  assert.equal(lock.repository, "honua-io/honua-studio");
  assert.equal(lock.signerWorkflow, "honua-io/honua-studio/.github/workflows/release.yml");
  assert.equal(lock.asset, `honua-studio-console-assets-${lock.tag}.tar.gz`);
  assert.equal(
    releaseAssetUrl(lock),
    `https://github.com/honua-io/honua-studio/releases/download/${lock.tag}/${lock.asset}`,
  );
});

test("a malformed lock is refused", () => {
  const archive = releaseArchive();
  assert.throws(() => lockFor(archive, { sha256: "e".repeat(63) }), /invalid sha256/);
  assert.throws(() => lockFor(archive, { tag: "latest" }), /invalid tag/);
  assert.throws(() => lockFor(archive, { mirror: "https://example.test" }), /must contain exactly/);
});

test("a lock cannot redirect trust to another repository or signer workflow", () => {
  const archive = releaseArchive();
  const redirections = [
    { repository: "attacker/honua-studio", signerWorkflow: "attacker/honua-studio/.github/workflows/release.yml" },
    { repository: "attacker/honua-studio" },
    { signerWorkflow: "honua-io/honua-studio/.github/workflows/unreviewed.yml" },
  ];
  for (const redirection of redirections) {
    assert.throws(() => lockFor(archive, redirection), /invalid (?:repository|signerWorkflow)/, JSON.stringify(redirection));
  }
});

test("a release whose digest differs from the lock is refused before extraction", async () => {
  const destination = join(scratch, "digest-mismatch");
  const lock = lockFor(releaseArchive());
  const tampered = releaseArchive({ ...RELEASE_FILES, "console.js": "export const createWorkspaceAgentSession = 1;\n" });
  await assert.rejects(
    fetchStudioRelease(lock, destination, { fetchImpl: serving(tampered) }),
    new RegExp(`the Studio release lock requires ${lock.sha256}`),
  );
  assert.equal(existsSync(destination), false);
});

test("a release archive above the download limit is refused while it streams or from its declared length", async () => {
  const archive = releaseArchive();
  const lock = lockFor(archive);
  const limits = { ...RELEASE_LIMITS, maxArchiveBytes: archive.length - 1 };
  const declared = async () => new Response(archive, { headers: { "content-length": String(archive.length) } });
  for (const fetchImpl of [serving(archive), declared]) {
    await assert.rejects(
      fetchStudioRelease(lock, join(scratch, "oversize"), { fetchImpl, limits }),
      new RegExp(`exceeds the ${limits.maxArchiveBytes}-byte Studio release archive limit`),
    );
  }
  assert.equal(existsSync(join(scratch, "oversize")), false);
  assert.ok(RELEASE_LIMITS.maxArchiveBytes >= 10 * 2_064_927, "the limit leaves headroom above the v0.1.1 archive");
});

test("a cached archive above the download limit is not reused", async () => {
  const archive = releaseArchive();
  const lock = lockFor(archive);
  const cacheDirectory = join(scratch, "oversize-cache");
  await fetchStudioRelease(lock, join(scratch, "cached-once"), { cacheDirectory, fetchImpl: serving(archive) });
  const limits = { ...RELEASE_LIMITS, maxArchiveBytes: archive.length - 1 };
  await assert.rejects(
    fetchStudioRelease(lock, join(scratch, "cached-twice"), { cacheDirectory, fetchImpl: serving(archive), limits }),
    /exceeds the .*-byte Studio release archive limit/,
  );
});

test("a fetched release is vendored with provenance and reused from the archive cache", async () => {
  const archive = releaseArchive();
  const lock = lockFor(archive);
  const destination = join(scratch, "vendored");
  const cacheDirectory = join(scratch, "cache");
  await fetchStudioRelease(lock, destination, { cacheDirectory, fetchImpl: serving(archive) });
  await rm(destination, { recursive: true });
  await fetchStudioRelease(lock, destination, { cacheDirectory, fetchImpl: unreachable });

  await verifyStudioAssets(destination, lock);
  const provenance = JSON.parse(await readFile(join(destination, "STUDIO-PROVENANCE.json"), "utf8"));
  assert.equal(provenance.sourceCommit, lock.sourceCommit);
  assert.deepEqual(provenance.release, {
    repository: lock.repository, tag: lock.tag, asset: lock.asset, sha256: lock.sha256, signerWorkflow: lock.signerWorkflow,
  });
  assert.equal(provenance.files["maputnik/index.html"].bytes, RELEASE_FILES["maputnik/index.html"].length);
  const manifest = JSON.parse(await readFile(join(destination, "honua-studio-artifact.json"), "utf8"));
  assert.deepEqual(
    manifest.files.map((file) => file.path),
    [...Object.keys(RELEASE_FILES), "STUDIO-PROVENANCE.json"].sort((left, right) => left.localeCompare(right)),
  );
});

test("a release built against another SDK revision is refused", async () => {
  const archive = releaseArchive();
  const lock = lockFor(archive, { sdkRevision: "c".repeat(40) });
  await assert.rejects(
    fetchStudioRelease(lock, join(scratch, "sdk-mismatch"), { fetchImpl: serving(archive) }),
    /SDK provenance does not match the locked SDK revision/,
  );
});

test("verification rejects changed, unlisted or mis-compressed assets and accepts publish compression", async () => {
  const archive = releaseArchive();
  const lock = lockFor(archive);
  const destination = join(scratch, "verified");
  await fetchStudioRelease(lock, destination, { fetchImpl: serving(archive) });

  await writeFile(join(destination, "console.js.gz"), gzipSync(RELEASE_FILES["console.js"]));
  await verifyStudioAssets(destination, lock);
  await writeFile(join(destination, "console.js.gz"), gzipSync("different"));
  await assert.rejects(verifyStudioAssets(destination, lock), /does not reproduce pinned bytes: console\.js\.gz/);
  await rm(join(destination, "console.js.gz"));

  await writeFile(join(destination, "injected.js"), "alert(1)");
  await assert.rejects(verifyStudioAssets(destination, lock), /unpinned file injected\.js/);
  await rm(join(destination, "injected.js"));

  await writeFile(join(destination, "style.css"), "body { display: none; }");
  await assert.rejects(verifyStudioAssets(destination, lock), /style\.css does not match its recorded digest/);
  await assert.rejects(verifyStudioAssets(destination, lockFor(archive, { tag: "v9.9.10" })), /provenance release/);
});
