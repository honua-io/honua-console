import { strict as assert } from "node:assert";
import { spawnSync } from "node:child_process";
import { resolve } from "node:path";
import { test } from "node:test";
import { fileURLToPath } from "node:url";

// Screenshots, design hand-off corpora, backlogs and per-issue evidence go to
// gitignored artifacts/, CI or release assets, and issues; not the tracked tree.
const repoRoot = resolve(fileURLToPath(new URL("../..", import.meta.url)));

const deniedPaths = [
  /^docs\/(images|design-handoff|roadmap|audit|validation)\//,
  /(^|\/)evidence\//,
  /-evidence\.md$/,
  /(^|\/)test-results\//,
  /^artifacts\//,
  /(^|\/)runs\/[^/]+\//,
  /\.(tgz|log)$/,
];

const imagePattern = /\.(png|jpe?g|gif|webp|bmp|tiff?)$/i;
const imageBudgetBytes = 64 * 1024;

function git(args, input) {
  const result = spawnSync("git", args, { cwd: repoRoot, encoding: "utf8", input });
  assert.equal(result.status, 0, `git ${args.join(" ")} failed: ${result.stderr}`);
  return result.stdout;
}

function trackedFiles() {
  return git(["ls-files", "-z"]).split("\0").filter(Boolean);
}

test("no evidence, screenshot or backlog paths are tracked", () => {
  assert.deepEqual(trackedFiles().filter((path) => deniedPaths.some((rule) => rule.test(path))), []);
});

test("tracked raster images stay within the byte budget", () => {
  // Sizes come from the index, so a file missing from the working tree cannot crash the scan.
  const images = git(["ls-files", "-s", "-z"])
    .split("\0")
    .filter(Boolean)
    .map((entry) => {
      const [meta, path] = entry.split("\t");
      return { blob: meta.split(" ")[1], path };
    })
    .filter(({ path }) => imagePattern.test(path));
  if (images.length === 0) return;
  const sizes = git(["cat-file", "--batch-check=%(objectsize)"], images.map(({ blob }) => blob).join("\n") + "\n")
    .trim()
    .split("\n")
    .map(Number);
  const bytes = sizes.reduce((sum, size) => sum + size, 0);
  assert.ok(bytes <= imageBudgetBytes, `${bytes} bytes of tracked images (${images.map(({ path }) => path).join(", ")}) exceed ${imageBudgetBytes}`);
});
