import { strict as assert } from "node:assert";
import { mkdtemp, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { brotliCompressSync, gzipSync } from "node:zlib";
import { after, test } from "node:test";

import {
  CESIUM_PUBLISHED_MANIFEST,
  buildManifest,
  inventoryTree,
  verifyTree,
} from "../lib/cesium-extracted-tree.mjs";

const root = await mkdtemp(join(tmpdir(), "honua-console-cesium-tree-"));
after(async () => rm(root, { recursive: true, force: true }));

test("published verification accepts only pinned originals and lossless build compression", async () => {
  const runtime = Buffer.from("reviewed Cesium runtime", "utf8");
  await writeFile(join(root, "Cesium.js"), runtime);
  await writeFile(join(root, "LICENSE.md"), "Apache License\n", "utf8");
  const manifest = buildManifest(await inventoryTree(root));
  await writeFile(join(root, CESIUM_PUBLISHED_MANIFEST), `${JSON.stringify(manifest, null, 2)}\n`, "utf8");
  await writeFile(join(root, "Cesium.js.gz"), gzipSync(runtime));
  await writeFile(join(root, "Cesium.js.br"), brotliCompressSync(runtime));

  await verifyTree(root, manifest, { requirePublishedManifest: true });

  await writeFile(join(root, "unreviewed.js"), "unreviewed", "utf8");
  await assert.rejects(
    verifyTree(root, manifest, { requirePublishedManifest: true }),
    /unpinned file unreviewed\.js/,
  );
  await rm(join(root, "unreviewed.js"));

  await writeFile(join(root, "Cesium.js.gz"), gzipSync("different runtime"));
  await assert.rejects(
    verifyTree(root, manifest, { requirePublishedManifest: true }),
    /does not reproduce pinned bytes: Cesium\.js\.gz/,
  );
});
