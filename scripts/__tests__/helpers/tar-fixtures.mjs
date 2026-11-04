import { gzipSync } from "node:zlib";

const BLOCK_BYTES = 512;
const CHECKSUM_OFFSET = 148;
const CHECKSUM_BYTES = 8;

export const HEADER_OFFSETS = { size: 124, checksum: CHECKSUM_OFFSET, type: 156, format: 257, prefix: 345 };
export const GNU_FORMAT = "ustar  \u0000";

export function tarEntry(name, { type = "0", body = Buffer.alloc(0), link = "" } = {}) {
  const header = Buffer.alloc(BLOCK_BYTES);
  header.write(name, 0, 100);
  header.write("0000644\0", 100);
  header.write("0000000\0", 108);
  header.write("0000000\0", 116);
  header.write(`${body.length.toString(8).padStart(11, "0")}\0`, HEADER_OFFSETS.size);
  header.write("15272616132\0", 136);
  header.write(type, HEADER_OFFSETS.type, 1, "latin1");
  header.write(link, 157, 100);
  header.write("ustar\u000000", HEADER_OFFSETS.format, "latin1");
  sealChecksum(header);
  return Buffer.concat([header, body, Buffer.alloc((BLOCK_BYTES - (body.length % BLOCK_BYTES)) % BLOCK_BYTES)]);
}

export function withHeaderField(entry, offset, value) {
  const patched = Buffer.from(entry);
  patched.write(value, offset, "latin1");
  sealChecksum(patched);
  return patched;
}

export function withUtf8HeaderField(entry, { offset, value, length }) {
  const patched = Buffer.from(entry);
  patched.fill(0, offset, offset + length);
  if (patched.write(value, offset, length, "utf8") !== Buffer.byteLength(value)) {
    throw new Error(`${value} does not fit in ${length} bytes`);
  }
  sealChecksum(patched);
  return patched;
}

export function withUnsealedHeaderField(entry, offset, value) {
  const patched = Buffer.from(entry);
  patched.write(value, offset, "latin1");
  return patched;
}

export function tarGz(...entries) {
  return gzipSync(Buffer.concat([...entries, Buffer.alloc(2 * BLOCK_BYTES)]));
}

function sealChecksum(header) {
  header.fill(0x20, CHECKSUM_OFFSET, CHECKSUM_OFFSET + CHECKSUM_BYTES);
  const sum = header.subarray(0, BLOCK_BYTES).reduce((total, byte) => total + byte, 0);
  header.write(`${sum.toString(8).padStart(6, "0")}\0 `, CHECKSUM_OFFSET, "latin1");
}
