import { mkdir, writeFile } from "node:fs/promises";
import { dirname, join } from "node:path";

import { TAR_LIMITS, readTarArchive } from "./tar-archive.mjs";

const REGULAR_FILE_TYPE = "0";
const DIRECTORY_TYPE = "5";

export async function extractTarGz(archive, destination, limits = TAR_LIMITS) {
  const registry = { explicit: new Map(), implicitDirectories: new Set() };
  const entries = readTarArchive(archive, { limits }).map(confinedEntry);
  for (const entry of entries) register(registry, entry);
  for (const entry of entries) await writeEntry(destination, entry);
}

function confinedEntry({ name, type, body }) {
  if (type !== REGULAR_FILE_TYPE && type !== DIRECTORY_TYPE) {
    throw new Error(`archive entry ${name} has unsupported type ${JSON.stringify(type)}`);
  }
  if (type === DIRECTORY_TYPE && body.length !== 0) throw new Error(`archive directory ${name} carries ${body.length} bytes`);
  return { name, type, body, path: canonicalPath(name, type) };
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
