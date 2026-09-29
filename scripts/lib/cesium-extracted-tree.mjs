import { createHash } from "node:crypto";
import { readFile, readdir, stat } from "node:fs/promises";
import { relative, resolve, sep } from "node:path";
import { promisify } from "node:util";
import { brotliDecompress, gunzip } from "node:zlib";

const decompressBrotli = promisify(brotliDecompress);
const decompressGzip = promisify(gunzip);

export const CESIUM_PACKAGE = "cesium";
export const CESIUM_VERSION = "1.119.0";
export const CESIUM_ARCHIVE_SHA256 = "2daa7203af810ddb320d7990ef26812309336f4559b3d9b2d1b1450f8110cd7d";
export const CESIUM_MANIFEST_SCHEMA = "honua.console.cesium-extracted-tree/v1";
export const CESIUM_LICENSE_PATH = "LICENSE.md";
export const CESIUM_PUBLISHED_MANIFEST = "cesium.manifest.json";

export async function inventoryTree(root) {
  const files = [];
  await visit(resolve(root));
  files.sort((left, right) => left.path.localeCompare(right.path, "en"));
  return files;

  async function visit(directory) {
    const entries = await readdir(directory, { withFileTypes: true });
    entries.sort((left, right) => left.name.localeCompare(right.name, "en"));
    for (const entry of entries) {
      const absolute = resolve(directory, entry.name);
      if (entry.isSymbolicLink()) throw new Error(`Cesium tree must not contain symbolic links: ${absolute}`);
      if (entry.isDirectory()) {
        await visit(absolute);
        continue;
      }
      if (!entry.isFile()) throw new Error(`Cesium tree contains unsupported entry: ${absolute}`);
      const path = relative(root, absolute).split(sep).join("/");
      if (path === CESIUM_PUBLISHED_MANIFEST) continue;
      const metadata = await stat(absolute);
      const bytes = await readFile(absolute);
      files.push({ path, bytes: metadata.size, sha256: sha256(bytes) });
    }
  }
}

export function buildManifest(files) {
  const license = files.find((file) => file.path === CESIUM_LICENSE_PATH);
  if (!license) throw new Error(`Cesium extracted tree is missing ${CESIUM_LICENSE_PATH}`);
  return {
    schemaVersion: CESIUM_MANIFEST_SCHEMA,
    package: CESIUM_PACKAGE,
    version: CESIUM_VERSION,
    archiveSha256: CESIUM_ARCHIVE_SHA256,
    license: { spdx: "Apache-2.0", path: license.path, sha256: license.sha256 },
    files,
    treeSha256: sha256(Buffer.from(canonicalJson(files), "utf8")),
  };
}

export async function verifyTree(root, expected, { requirePublishedManifest = false } = {}) {
  validateManifest(expected);
  const inventory = await inventoryTree(root);
  const expectedPaths = new Set(expected.files.map((file) => file.path));
  const originals = inventory.filter((file) => expectedPaths.has(file.path));
  const actual = buildManifest(originals);
  if (canonicalJson(actual) !== canonicalJson(expected)) {
    throw new Error(
      `Cesium extracted tree does not match the pinned ${CESIUM_VERSION} manifest ` +
      `(expected ${expected.treeSha256}, received ${actual.treeSha256})`,
    );
  }
  if (requirePublishedManifest) {
    const published = JSON.parse(await readFile(resolve(root, CESIUM_PUBLISHED_MANIFEST), "utf8"));
    if (canonicalJson(published) !== canonicalJson(expected)) {
      throw new Error("published Cesium manifest does not match the repository-pinned manifest");
    }
  }
  const derived = inventory.filter((file) => !expectedPaths.has(file.path));
  for (const file of derived) {
    const extension = file.path.endsWith(".br") ? ".br" : file.path.endsWith(".gz") ? ".gz" : undefined;
    if (!extension) throw new Error(`published Cesium tree contains unpinned file ${file.path}`);
    const sourcePath = file.path.slice(0, -extension.length);
    if (!expectedPaths.has(sourcePath) && sourcePath !== CESIUM_PUBLISHED_MANIFEST) {
      throw new Error(`published Cesium tree contains unpinned compressed file ${file.path}`);
    }
    const compressed = await readFile(resolve(root, file.path));
    const source = await readFile(resolve(root, sourcePath));
    const decompressed = extension === ".br"
      ? await decompressBrotli(compressed)
      : await decompressGzip(compressed);
    if (!decompressed.equals(source)) {
      throw new Error(`published Cesium compression does not reproduce pinned bytes: ${file.path}`);
    }
  }
  return actual;
}

export function validateManifest(value) {
  if (!value || typeof value !== "object" || Array.isArray(value)) throw new Error("Cesium manifest must be an object");
  if (value.schemaVersion !== CESIUM_MANIFEST_SCHEMA) throw new Error("unsupported Cesium manifest schemaVersion");
  if (value.package !== CESIUM_PACKAGE || value.version !== CESIUM_VERSION) throw new Error("Cesium manifest package/version mismatch");
  if (value.archiveSha256 !== CESIUM_ARCHIVE_SHA256) throw new Error("Cesium manifest archive digest mismatch");
  if (value.license?.spdx !== "Apache-2.0" || value.license?.path !== CESIUM_LICENSE_PATH) {
    throw new Error("Cesium manifest license identity mismatch");
  }
  if (!Array.isArray(value.files) || value.files.length === 0) throw new Error("Cesium manifest files must be non-empty");
  const paths = value.files.map((file) => file?.path);
  if (new Set(paths).size !== paths.length || paths.some((path) => typeof path !== "string" || path.startsWith("/") || path.includes(".."))) {
    throw new Error("Cesium manifest contains an unsafe or repeated path");
  }
  const license = value.files.find((file) => file.path === CESIUM_LICENSE_PATH);
  if (!license || license.sha256 !== value.license.sha256) throw new Error("Cesium manifest license digest mismatch");
  if (sha256(Buffer.from(canonicalJson(value.files), "utf8")) !== value.treeSha256) {
    throw new Error("Cesium manifest tree digest is invalid");
  }
}

export function canonicalJson(value) {
  if (Array.isArray(value)) return `[${value.map(canonicalJson).join(",")}]`;
  if (value && typeof value === "object") {
    return `{${Object.keys(value).sort().map((key) => `${JSON.stringify(key)}:${canonicalJson(value[key])}`).join(",")}}`;
  }
  return JSON.stringify(value);
}

function sha256(value) {
  return createHash("sha256").update(value).digest("hex");
}
