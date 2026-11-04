import { createHash } from "node:crypto";
import { existsSync } from "node:fs";
import { cp, mkdir, mkdtemp, readdir, readFile, rm, stat, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { dirname, join, relative, resolve, sep } from "node:path";
import { fileURLToPath } from "node:url";
import { isDeepStrictEqual } from "node:util";

import { verifyCompressedCopies } from "./compressed-static-assets.mjs";
import { TAR_LIMITS, extractTarGz } from "./tar-archive.mjs";

const MIB = 1024 * 1024;
export const RELEASE_LIMITS = Object.freeze({ maxArchiveBytes: 32 * MIB, ...TAR_LIMITS });

const repositoryRoot = resolve(dirname(fileURLToPath(import.meta.url)), "../..");
export const STUDIO_RELEASE_LOCK_PATH = join(repositoryRoot, "scripts/studio-release.lock.json");
export const VENDORED_STUDIO_ROOT = join(repositoryRoot, "src/Honua.Console.Web/wwwroot/_studio");

const LOCK_SCHEMA = "honua.console.studio-release-lock/v1";
const PROVENANCE_SCHEMA = "honua.studio.console-assets/v1";
const ARTIFACT_SCHEMA = "honua.console.studio-artifact.v1";
const PROVENANCE = "STUDIO-PROVENANCE.json";
const ARTIFACT_MANIFEST = "honua-studio-artifact.json";
const SDK_PROVENANCE = "SDK-PROVENANCE.json";
const REQUIRED_FILES = ["console.js", "style.css", "LICENSE", "THIRD-PARTY-NOTICES.txt", SDK_PROVENANCE, PROVENANCE];
const GITHUB_NAME = "[A-Za-z0-9][A-Za-z0-9._-]*";
export const STUDIO_REPOSITORY = "honua-io/honua-studio";
export const STUDIO_SIGNER_WORKFLOW = `${STUDIO_REPOSITORY}/.github/workflows/release.yml`;
const isCommit = (value) => /^[a-f0-9]{40}$/.test(value);
const LOCK_FIELDS = {
  schemaVersion: (value) => value === LOCK_SCHEMA,
  repository: (value) => value === STUDIO_REPOSITORY,
  tag: (value) => /^v\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$/.test(value),
  asset: (value) => new RegExp(`^${GITHUB_NAME}\\.tar\\.gz$`).test(value),
  sha256: (value) => /^[a-f0-9]{64}$/.test(value),
  sourceCommit: isCommit,
  signerWorkflow: (value) => value === STUDIO_SIGNER_WORKFLOW,
  sdkRevision: isCommit,
};

export function parseStudioReleaseLock(text) {
  const lock = JSON.parse(text);
  const expectedFields = Object.keys(LOCK_FIELDS);
  if (!lock || !isDeepStrictEqual(Object.keys(lock).sort(), [...expectedFields].sort())) {
    throw new Error(`the Studio release lock must contain exactly ${expectedFields.join(", ")}`);
  }
  const invalid = expectedFields.find((field) => typeof lock[field] !== "string" || !LOCK_FIELDS[field](lock[field]));
  if (invalid) throw new Error(`the Studio release lock has an invalid ${invalid}`);
  return Object.freeze(lock);
}

export async function readStudioReleaseLock(path = STUDIO_RELEASE_LOCK_PATH) {
  return parseStudioReleaseLock(await readFile(path, "utf8"));
}

export function releaseAssetUrl({ tag, asset }) {
  return `https://github.com/${STUDIO_REPOSITORY}/releases/download/${tag}/${asset}`;
}

export async function fetchStudioRelease(lock, destination, { cacheDirectory, fetchImpl = fetch, limits = RELEASE_LIMITS } = {}) {
  const archive = await obtainReleaseArchive(lock, { cacheDirectory, fetchImpl, limits });
  const staging = await mkdtemp(join(tmpdir(), "honua-studio-release-"));
  try {
    await extractTarGz(archive, staging, limits);
    await writeStudioProvenance(staging, lock);
    await verifyStudioAssets(staging, lock);
    await rm(destination, { recursive: true, force: true });
    await cp(staging, destination, { recursive: true });
  } finally {
    await rm(staging, { recursive: true, force: true });
  }
  await verifyStudioAssets(destination, lock);
}

async function obtainReleaseArchive(lock, { cacheDirectory, fetchImpl, limits }) {
  const cached = cacheDirectory && join(cacheDirectory, lock.asset);
  const reusable = cached && await readCachedArchive(cached, limits.maxArchiveBytes);
  if (reusable && sha256(reusable) === lock.sha256) return reusable;
  const bytes = await downloadArchive(releaseAssetUrl(lock), { fetchImpl, maxArchiveBytes: limits.maxArchiveBytes });
  const received = sha256(bytes);
  if (received !== lock.sha256) {
    throw new Error(`${lock.asset} has sha256 ${received}; the Studio release lock requires ${lock.sha256}`);
  }
  if (cached) {
    await mkdir(cacheDirectory, { recursive: true });
    await writeFile(cached, bytes);
  }
  return bytes;
}

async function readCachedArchive(path, maxArchiveBytes) {
  if (!existsSync(path) || (await stat(path)).size > maxArchiveBytes) return undefined;
  return readFile(path);
}

async function downloadArchive(url, { fetchImpl, maxArchiveBytes }) {
  const response = await fetchImpl(url, { redirect: "follow" });
  if (!response.ok) throw new Error(`downloading ${url} failed with HTTP ${response.status}`);
  if (response.url && !response.url.startsWith("https://")) throw new Error(`downloading ${url} left HTTPS`);
  const oversize = new Error(`${url} exceeds the ${maxArchiveBytes}-byte Studio release archive limit`);
  if (Number(response.headers.get("content-length") ?? 0) > maxArchiveBytes) throw oversize;
  const chunks = [];
  let received = 0;
  for await (const chunk of response.body ?? []) {
    received += chunk.length;
    if (received > maxArchiveBytes) throw oversize;
    chunks.push(chunk);
  }
  return Buffer.concat(chunks);
}

async function writeStudioProvenance(root, lock) {
  const main = await readFile(join(root, "console.js"), "utf8");
  if (!main.includes("createWorkspaceAgentSession") || main.includes("__honuaStudioFixtureToken")) {
    throw new Error("the release does not contain a production Console Studio entry");
  }
  const sdk = JSON.parse(await readFile(join(root, SDK_PROVENANCE), "utf8"));
  if (sdk.sourceCommit !== lock.sdkRevision) throw new Error("the release SDK provenance does not match the locked SDK revision");
  // Provenance describes this vendoring operation, never one shipped inside the release.
  await rm(join(root, PROVENANCE), { force: true });
  await rm(join(root, ARTIFACT_MANIFEST), { force: true });
  const entries = await inventory(root);
  const provenance = Buffer.from(`${JSON.stringify(provenanceDocument(lock, sdk, entries), null, 2)}\n`);
  await writeFile(join(root, PROVENANCE), provenance);
  const files = [...entries, describe(PROVENANCE, provenance)].sort(byPath);
  const manifest = { schemaVersion: ARTIFACT_SCHEMA, studioRevision: lock.sourceCommit, sdkRevision: lock.sdkRevision, files };
  await writeFile(join(root, ARTIFACT_MANIFEST), `${JSON.stringify(manifest, null, 2)}\n`);
}

function provenanceDocument(lock, sdk, entries) {
  return {
    schema: PROVENANCE_SCHEMA,
    sourceCommit: lock.sourceCommit,
    release: releaseIdentity(lock),
    releaseQualified: false,
    integrationQualification: "pending-installed-workspace-journey",
    sdk,
    files: fileDigests(entries),
  };
}

export async function verifyStudioAssets(root, lock) {
  const [manifest, provenance, sdk] = await Promise.all(
    [ARTIFACT_MANIFEST, PROVENANCE, SDK_PROVENANCE].map(async (name) => JSON.parse(await readFile(join(root, name), "utf8"))));
  requireLockedIdentity({ manifest, provenance, sdk }, lock);
  const listed = new Map(manifest.files.map((file) => [file.path, file]));
  const missing = REQUIRED_FILES.find((path) => !listed.has(path));
  if (missing) throw new Error(`the vendored Studio manifest does not list ${missing}`);
  const present = (await inventory(root)).filter((file) => file.path !== ARTIFACT_MANIFEST);
  const changed = present.find((file) => listed.has(file.path) && !isDeepStrictEqual(file, listed.get(file.path)));
  if (changed) throw new Error(`the vendored Studio asset ${changed.path} does not match its recorded digest`);
  const presentPaths = new Set(present.map((file) => file.path));
  const absent = [...listed.keys()].find((path) => !presentPaths.has(path));
  if (absent) throw new Error(`the vendored Studio asset ${absent} is missing`);
  const unlisted = [...presentPaths].filter((path) => !listed.has(path));
  await verifyCompressedCopies({
    root, paths: unlisted, originals: new Set([...listed.keys(), ARTIFACT_MANIFEST]), label: "Studio",
  });
}

function requireLockedIdentity({ manifest, provenance, sdk }, lock) {
  const recordedFiles = (manifest.files ?? []).filter((file) => file.path !== PROVENANCE);
  const expectations = [
    ["artifact manifest schema", manifest.schemaVersion, ARTIFACT_SCHEMA],
    ["artifact Studio revision", manifest.studioRevision, lock.sourceCommit],
    ["artifact SDK revision", manifest.sdkRevision, lock.sdkRevision],
    ["provenance schema", provenance.schema, PROVENANCE_SCHEMA],
    ["provenance Studio revision", provenance.sourceCommit, lock.sourceCommit],
    ["SDK revision", sdk.sourceCommit, lock.sdkRevision],
    ["provenance SDK receipt", provenance.sdk, sdk],
    ["provenance release", provenance.release, releaseIdentity(lock)],
    ["provenance file digests", provenance.files, fileDigests(recordedFiles)],
  ];
  const mismatch = expectations.find(([, actual, expected]) => !isDeepStrictEqual(actual, expected));
  if (mismatch) throw new Error(`the vendored Studio ${mismatch[0]} does not match the release lock`);
}

function releaseIdentity({ repository, tag, asset, sha256: archiveSha256, signerWorkflow }) {
  return { repository, tag, asset, sha256: archiveSha256, signerWorkflow };
}

function fileDigests(entries) {
  return Object.fromEntries(entries.map(({ path, bytes, sha256: digest }) => [path, { bytes, sha256: digest }]));
}

async function inventory(root, directory = root) {
  const entries = [];
  for (const item of await readdir(directory, { withFileTypes: true })) {
    const path = join(directory, item.name);
    if (item.isDirectory()) entries.push(...await inventory(root, path));
    else if (item.isFile()) entries.push(describe(relative(root, path).split(sep).join("/"), await readFile(path)));
    else throw new Error(`Studio assets must contain only regular files and directories: ${path}`);
  }
  return entries.sort(byPath);
}

function describe(path, content) {
  return { path, bytes: content.length, sha256: sha256(content) };
}

function byPath(left, right) {
  return left.path.localeCompare(right.path);
}

function sha256(content) {
  return createHash("sha256").update(content).digest("hex");
}
