import { strict as assert } from "node:assert";
import { createHash } from "node:crypto";
import { existsSync } from "node:fs";
import { mkdtemp, readdir, readFile, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { after, test } from "node:test";
import { gzipSync } from "node:zlib";

import {
  fetchStudioRelease,
  parseStudioReleaseLock,
  readStudioReleaseLock,
  releaseAssetUrl,
  verifyStudioAssets,
} from "../lib/studio-release.mjs";
import { extractTarGz } from "../lib/tar-archive.mjs";

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

function tarEntry(name, { type = "0", body = Buffer.alloc(0), link = "" } = {}) {
  const header = Buffer.alloc(512);
  header.write(name, 0, 100);
  header.write("0000644\0", 100);
  header.write(`${body.length.toString(8).padStart(11, "0")}\0`, 124);
  header.write(type, 156);
  header.write(link, 157, 100);
  header.write("ustar\u000000", 257);
  const padding = Buffer.alloc((512 - (body.length % 512)) % 512);
  return Buffer.concat([header, body, padding]);
}

function tarGz(...entries) {
  return gzipSync(Buffer.concat([...entries, Buffer.alloc(1024)]));
}

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
  assert.throws(
    () => lockFor(archive, { signerWorkflow: "someone/fork/.github/workflows/release.yml" }),
    /signer workflow must belong to the release repository/,
  );
});

test("extraction refuses links and paths outside the extraction directory", async () => {
  const refusals = [
    [tarEntry("./console.js", { type: "2", link: "/etc/passwd" }), /not a regular file or directory/],
    [tarEntry("./maputnik", { type: "1", link: "./console.js" }), /not a regular file or directory/],
    [tarEntry("../escape.js", { body: Buffer.from("x") }), /escapes the extraction directory/],
    [tarEntry("/tmp/escape.js", { body: Buffer.from("x") }), /escapes the extraction directory/],
    [tarEntry("./assets/../../escape.js", { body: Buffer.from("x") }), /escapes the extraction directory/],
  ];
  for (const [entry, reason] of refusals) {
    const destination = await mkdtemp(join(scratch, "extract-"));
    await assert.rejects(extractTarGz(tarGz(entry), destination), reason);
    assert.deepEqual(await readdir(destination), []);
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
