import { test } from "node:test";
import assert from "node:assert/strict";
import { readFile, readdir } from "node:fs/promises";
import { join, relative } from "node:path";
import { fileURLToPath } from "node:url";

const repo = fileURLToPath(new URL("../../", import.meta.url));

async function scopedStylesheets(directory) {
  const found = [];
  for (const entry of await readdir(directory, { withFileTypes: true })) {
    if (["bin", "obj", "node_modules"].includes(entry.name)) continue;
    const path = join(directory, entry.name);
    if (entry.isDirectory()) found.push(...await scopedStylesheets(path));
    else if (entry.name.endsWith(".razor.css")) found.push(relative(repo, path));
  }
  return found;
}

test("component styles are global because the static index links no scoped-CSS bundle", async () => {
  const index = await readFile(join(repo, "src/Honua.Console.Web/wwwroot/index.html"), "utf8");
  const linksBundle = /href="[^"]*\.styles\.css"/.test(index);
  const scoped = await scopedStylesheets(join(repo, "src"));
  assert.ok(linksBundle || scoped.length === 0,
    `index.html links no *.styles.css, so these never load; move them into wwwroot CSS: ${scoped.join(", ")}`);
});
