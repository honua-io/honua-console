import { readFile } from "node:fs/promises";
import { resolve } from "node:path";
import { promisify } from "node:util";
import { brotliDecompress, gunzip } from "node:zlib";

const DECOMPRESSORS = { ".br": promisify(brotliDecompress), ".gz": promisify(gunzip) };

export async function verifyCompressedCopies({ root, paths, originals, label }) {
  for (const path of paths) {
    const extension = Object.keys(DECOMPRESSORS).find((candidate) => path.endsWith(candidate));
    if (!extension) throw new Error(`published ${label} tree contains unpinned file ${path}`);
    const sourcePath = path.slice(0, -extension.length);
    if (!originals.has(sourcePath)) throw new Error(`published ${label} tree contains unpinned compressed file ${path}`);
    const decompressed = await DECOMPRESSORS[extension](await readFile(resolve(root, path)));
    if (!decompressed.equals(await readFile(resolve(root, sourcePath)))) {
      throw new Error(`published ${label} compression does not reproduce pinned bytes: ${path}`);
    }
  }
}
