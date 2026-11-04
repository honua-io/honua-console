import { mkdir, writeFile } from "node:fs/promises";
import { dirname, join } from "node:path";
import { gunzipSync } from "node:zlib";

const BLOCK_BYTES = 512;
const DIRECTORY_TYPE = "5";
const REGULAR_FILE_TYPES = new Set(["0", "\0"]);
const POSIX_MAGIC = "ustar";
const HEADER_FIELDS = { name: [0, 100], size: [124, 12], magic: [257, 6], prefix: [345, 155] };
const TYPE_OFFSET = 156;

export async function extractTarGz(archive, destination) {
  const tar = gunzipSync(archive);
  let offset = 0;
  while (offset + BLOCK_BYTES <= tar.length) {
    const header = tar.subarray(offset, offset + BLOCK_BYTES);
    if (header.every((byte) => byte === 0)) return;
    const entry = readHeader(header);
    const bodyStart = offset + BLOCK_BYTES;
    if (!Number.isSafeInteger(entry.size) || bodyStart + entry.size > tar.length) {
      throw new Error(`archive entry ${entry.name} is malformed or truncated`);
    }
    await writeEntry(destination, entry, tar.subarray(bodyStart, bodyStart + entry.size));
    offset = bodyStart + Math.ceil(entry.size / BLOCK_BYTES) * BLOCK_BYTES;
  }
  throw new Error("archive ends without a tar end-of-archive marker");
}

function readHeader(header) {
  const field = (name) => {
    const [start, length] = HEADER_FIELDS[name];
    return header.subarray(start, start + length).toString("utf8").replace(/\0[\s\S]*$/, "");
  };
  const prefix = field("magic") === POSIX_MAGIC ? field("prefix") : "";
  return {
    name: prefix ? `${prefix}/${field("name")}` : field("name"),
    size: Number.parseInt(field("size").trim() || "0", 8),
    type: String.fromCharCode(header[TYPE_OFFSET]),
  };
}

async function writeEntry(destination, { name, type }, body) {
  const path = confinedPath(destination, name);
  if (type === DIRECTORY_TYPE) return mkdir(path, { recursive: true });
  if (!REGULAR_FILE_TYPES.has(type)) throw new Error(`archive entry ${name} is not a regular file or directory`);
  await mkdir(dirname(path), { recursive: true });
  await writeFile(path, body, { flag: "wx" });
}

function confinedPath(destination, name) {
  const segments = name.split("/").filter((segment) => segment !== "" && segment !== ".");
  if (name.startsWith("/") || name.includes("\\") || segments.includes("..")) {
    throw new Error(`archive entry ${name} escapes the extraction directory`);
  }
  return join(destination, ...segments);
}
