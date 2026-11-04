import { gunzipSync } from "node:zlib";

export const MIB = 1024 * 1024;
export const TAR_LIMITS = Object.freeze({
  maxInflatedBytes: 128 * MIB,
  maxEntries: 4096,
  maxEntryBytes: 32 * MIB,
});

const BLOCK_BYTES = 512;
const END_OF_ARCHIVE_BYTES = 2 * BLOCK_BYTES;
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

export function readTarArchive(archive, { limits = TAR_LIMITS, emptyNumericFields = [] } = {}) {
  const tar = inflate(archive, limits.maxInflatedBytes);
  const entries = [];
  let offset = 0;
  while (!isZeroBlock(tar, offset)) {
    if (entries.length === limits.maxEntries) throw new Error(`archive has more than ${limits.maxEntries} entries`);
    const { entry, next } = readEntry(tar, offset, { maxEntryBytes: limits.maxEntryBytes, emptyNumericFields });
    entries.push(entry);
    offset = next;
  }
  requireEndOfArchive(tar, offset);
  return entries;
}

function inflate(archive, maxInflatedBytes) {
  try {
    return gunzipSync(archive, { maxOutputLength: maxInflatedBytes });
  } catch (error) {
    if (error.code === "ERR_BUFFER_TOO_LARGE") throw new Error(`archive inflates beyond ${maxInflatedBytes} bytes`);
    throw error;
  }
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

function readEntry(tar, offset, { maxEntryBytes, emptyNumericFields }) {
  const header = tar.subarray(offset, offset + BLOCK_BYTES);
  requireSupportedHeader(header, emptyNumericFields);
  const size = octal(header, "size");
  const name = entryName(header);
  if (size > maxEntryBytes) throw new Error(`archive entry ${name} exceeds ${maxEntryBytes} bytes`);
  const bodyStart = offset + BLOCK_BYTES;
  const next = bodyStart + Math.ceil(size / BLOCK_BYTES) * BLOCK_BYTES;
  if (next > tar.length) throw new Error(`archive entry ${name} is truncated`);
  return { entry: { name, type: raw(header, "type"), body: tar.subarray(bodyStart, bodyStart + size) }, next };
}

function entryName(header) {
  const prefix = raw(header, "format") === POSIX_USTAR_FORMAT ? text(header, "prefix") : "";
  return prefix ? `${prefix}/${text(header, "name")}` : text(header, "name");
}

function requireSupportedHeader(header, emptyNumericFields) {
  if (!SUPPORTED_FORMATS.has(raw(header, "format"))) throw new Error("archive header is not ustar or GNU tar");
  const [checksumStart, checksumLength] = HEADER_FIELDS.checksum;
  const isChecksumField = (index) => index >= checksumStart && index < checksumStart + checksumLength;
  const sum = header.reduce((total, byte, index) => total + (isChecksumField(index) ? CHECKSUM_PLACEHOLDER : byte), 0);
  if (sum !== octal(header, "checksum")) throw new Error("archive header checksum does not match");
  const isEmpty = (field) => emptyNumericFields.includes(field) && /^[\0 ]*$/.test(raw(header, field));
  for (const field of NUMERIC_FIELDS.filter((candidate) => !isEmpty(candidate))) octal(header, field);
}

function octal(header, field) {
  const value = raw(header, field).replace(/[\0 ]+$/, "").replace(/^ +/, "");
  if (!/^[0-7]+$/.test(value)) throw new Error(`archive header has a malformed ${field} field`);
  return Number.parseInt(value, 8);
}

function text(header, field) {
  const bytes = fieldBytes(header, field);
  const end = bytes.indexOf(0);
  return bytes.subarray(0, end === -1 ? bytes.length : end).toString("utf8");
}

function raw(header, field) {
  return fieldBytes(header, field).toString("latin1");
}

function fieldBytes(header, field) {
  const [start, length] = HEADER_FIELDS[field];
  return header.subarray(start, start + length);
}
