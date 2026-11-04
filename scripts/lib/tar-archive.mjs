import { mkdir, writeFile } from "node:fs/promises";
import { dirname, join } from "node:path";
import { gunzipSync } from "node:zlib";

export const MIB = 1024 * 1024;
export const TAR_LIMITS = Object.freeze({
  maxInflatedBytes: 128 * MIB,
  maxEntries: 4096,
  maxEntryBytes: 32 * MIB,
});

const BLOCK_BYTES = 512;
const END_OF_ARCHIVE_BYTES = 2 * BLOCK_BYTES;
const REGULAR_FILE_TYPE = "0";
const DIRECTORY_TYPE = "5";
const POSIX_USTAR_FORMAT = "ustar\u000000";
// GNU tar's default format: bytes 345+ hold access and change times, not a POSIX name prefix.
const GNU_FORMAT = "ustar  \u0000";
const SUPPORTED_FORMATS = new Set([POSIX_USTAR_FORMAT, GNU_FORMAT]);
const CHECKSUM_PLACEHOLDER = 0x20;
const HEADER_FIELDS = {
  name: [0, 100], mode: [100, 8], uid: [108, 8], gid: [116, 8], size: [124, 12], mtime: [136, 12],
  checksum: [148, 8], type: [156, 1], format: [257, 8], prefix: [345, 155],
};
const NUMERIC_FIELDS = ["mode", "uid", "gid", "size", "mtime"];

export async function extractTarGz(archive, destination, limits = TAR_LIMITS) {
  const entries = readEntries(inflate(archive, limits.maxInflatedBytes), limits);
  for (const entry of entries) await writeEntry(destination, entry);
}

function inflate(archive, maxInflatedBytes) {
  try {
    return gunzipSync(archive, { maxOutputLength: maxInflatedBytes });
  } catch (error) {
    if (error.code === "ERR_BUFFER_TOO_LARGE") throw new Error(`archive inflates beyond ${maxInflatedBytes} bytes`);
    throw error;
  }
}

function readEntries(tar, limits) {
  const entries = [];
  const registry = { explicit: new Map(), implicitDirectories: new Set() };
  let offset = 0;
  while (!isZeroBlock(tar, offset)) {
    if (entries.length === limits.maxEntries) throw new Error(`archive has more than ${limits.maxEntries} entries`);
    const entry = readEntry(tar, offset, limits.maxEntryBytes);
    register(registry, entry);
    entries.push(entry);
    offset = entry.next;
  }
  requireEndOfArchive(tar, offset);
  return entries;
}

function isZeroBlock(tar, offset) {
  if (offset + BLOCK_BYTES > tar.length) throw new Error("archive ends without a tar end-of-archive marker");
  return tar.subarray(offset, offset + BLOCK_BYTES).every((byte) => byte === 0);
}

function requireEndOfArchive(tar, offset) {
  const trailer = tar.subarray(offset);
  if (trailer.length < END_OF_ARCHIVE_BYTES || !trailer.every((byte) => byte === 0)) {
    throw new Error("archive has data after its first zero block instead of a tar end-of-archive marker");
  }
}

function readEntry(tar, offset, maxEntryBytes) {
  const header = tar.subarray(offset, offset + BLOCK_BYTES);
  requireSupportedHeader(header);
  const size = octal(header, "size");
  const type = text(header, "type");
  const name = entryName(header);
  if (type !== REGULAR_FILE_TYPE && type !== DIRECTORY_TYPE) {
    throw new Error(`archive entry ${name} has unsupported type ${JSON.stringify(type)}`);
  }
  if (type === DIRECTORY_TYPE && size !== 0) throw new Error(`archive directory ${name} carries ${size} bytes`);
  if (size > maxEntryBytes) throw new Error(`archive entry ${name} exceeds ${maxEntryBytes} bytes`);
  const bodyStart = offset + BLOCK_BYTES;
  const next = bodyStart + Math.ceil(size / BLOCK_BYTES) * BLOCK_BYTES;
  if (next > tar.length) throw new Error(`archive entry ${name} is truncated`);
  return { name, path: canonicalPath(name, type), type, body: tar.subarray(bodyStart, bodyStart + size), next };
}

function entryName(header) {
  const prefix = raw(header, "format") === POSIX_USTAR_FORMAT ? text(header, "prefix") : "";
  return prefix ? `${prefix}/${text(header, "name")}` : text(header, "name");
}

function requireSupportedHeader(header) {
  if (!SUPPORTED_FORMATS.has(raw(header, "format"))) throw new Error("archive header is not ustar or GNU tar");
  const [checksumStart, checksumLength] = HEADER_FIELDS.checksum;
  const isChecksumField = (index) => index >= checksumStart && index < checksumStart + checksumLength;
  const sum = header.reduce((total, byte, index) => total + (isChecksumField(index) ? CHECKSUM_PLACEHOLDER : byte), 0);
  if (sum !== octal(header, "checksum")) throw new Error("archive header checksum does not match");
  for (const field of NUMERIC_FIELDS) octal(header, field);
}

function octal(header, field) {
  const value = raw(header, field).replace(/[\0 ]+$/, "").replace(/^ +/, "");
  if (!/^[0-7]+$/.test(value)) throw new Error(`archive header has a malformed ${field} field`);
  return Number.parseInt(value, 8);
}

function text(header, field) {
  return raw(header, field).replace(/\0[\s\S]*$/, "");
}

function raw(header, field) {
  const [start, length] = HEADER_FIELDS[field];
  return header.subarray(start, start + length).toString("latin1");
}

function canonicalPath(name, type) {
  const segments = name.split("/").filter((segment) => segment !== "" && segment !== ".");
  if (name.startsWith("/") || name.includes("\\") || segments.includes("..")) {
    throw new Error(`archive entry ${name} escapes the extraction directory`);
  }
  if (type === REGULAR_FILE_TYPE && segments.length === 0) throw new Error(`archive file entry ${name} names no file`);
  return segments.join("/");
}

function register({ explicit, implicitDirectories }, { name, path, type }) {
  if (explicit.has(path)) throw new Error(`archive entry ${name} duplicates an earlier entry`);
  const ancestors = path.split("/").slice(0, -1).map((_, index, segments) => segments.slice(0, index + 1).join("/"));
  const fileAncestor = ancestors.find((ancestor) => explicit.get(ancestor) === REGULAR_FILE_TYPE);
  if (fileAncestor) throw new Error(`archive entry ${name} is inside the file ${fileAncestor}`);
  if (type === REGULAR_FILE_TYPE && implicitDirectories.has(path)) {
    throw new Error(`archive file ${name} replaces a directory of earlier entries`);
  }
  explicit.set(path, type);
  for (const ancestor of ancestors) implicitDirectories.add(ancestor);
}

async function writeEntry(destination, { path, type, body }) {
  const target = join(destination, ...path.split("/").filter(Boolean));
  if (type === DIRECTORY_TYPE) return mkdir(target, { recursive: true });
  await mkdir(dirname(target), { recursive: true });
  await writeFile(target, body, { flag: "wx" });
}
