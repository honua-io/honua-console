import { strict as assert } from "node:assert";
import { readFileSync, readdirSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { test } from "node:test";
import { fileURLToPath } from "node:url";

import { verify } from "../vendor-assets.mjs";

// honua-console#333 — the Console must not fetch executable code from a CDN at page load.
//
// Three invariants, all offline so they run in the blocking CI gate (`npm test`):
//
//   1. The committed vendored assets are the bytes the lock says they are.
//   2. MapLibre is loaded from this origin, not unpkg.
//   3. The external origins the wwwroot interop scripts reach for and the external origins the CSP
//      admits are the SAME SET. This is the drift-killer: it is what makes vendoring a library
//      *force* the matching CSP entry away, and what makes adding a new CDN to a script fail CI
//      until someone widens the policy deliberately.
//
// Invariant 3 is a structural check, not proof of runtime behaviour — that is asserted separately
// and behaviourally by e2e/playwright/specs/no-external-requests.spec.ts, which records every
// request a real browser makes and fails on any that leaves the origin.

const here = dirname(fileURLToPath(import.meta.url));
const repoRoot = resolve(here, "../..");
const wwwroot = resolve(repoRoot, "src/Honua.Console.Shell/wwwroot");
const programPath = resolve(repoRoot, "src/Honua.Console.Web/Program.cs");

/**
 * External origins the Console is currently permitted to reach at runtime, each with the reason it
 * is still here. Shrinking this list is the goal; an entry may only be added alongside the code that
 * needs it and the CSP directive that admits it.
 */
const DECLARED_EXTERNAL_ORIGINS = new Map([
  [
    "https://cdn.jsdelivr.net",
    "Cesium (scene-viewer.js) and Vega (chart-preview.js) still load their runtime from the CDN; " +
      "honua-console#334 tracks vendoring both the way MapLibre was vendored in #333.",
  ],
  [
    "https://tile.openstreetmap.org",
    "Optional raster basemap tiles under the feature layers in map-preview.js. Tile IMAGERY, not " +
      "executable code, and the map still renders its features when the tiles are unreachable.",
  ],
]);

/** Absolute http(s) origins referenced by a source file. */
function externalOrigins(source) {
  const found = new Set();
  for (const match of source.matchAll(/https?:\/\/[^\s'"`)]+/g)) {
    try {
      found.add(new URL(match[0]).origin);
    } catch {
      // Not a parseable URL (a prose fragment in a comment); ignore.
    }
  }
  return found;
}

/** Origins in a file that are genuinely fetched, i.e. ignoring comment lines. */
function fetchedOrigins(source) {
  const code = source
    .split("\n")
    .filter((line) => !line.trimStart().startsWith("//"))
    .join("\n");
  return externalOrigins(code);
}

const interopScripts = readdirSync(wwwroot)
  .filter((name) => name.endsWith(".js"))
  .map((name) => ({ name, source: readFileSync(resolve(wwwroot, name), "utf8") }));

test("vendored assets match the digests recorded at vendoring time", async () => {
  const problems = await verify();
  assert.deepEqual(
    problems,
    [],
    `vendored assets are out of sync:\n  ${problems.join("\n  ")}\n` +
      "Re-run `node scripts/vendor-assets.mjs --update` and commit the result.",
  );
});

test("the pinned MapLibre version is recorded and updatable through a documented step", () => {
  const manifest = JSON.parse(readFileSync(resolve(repoRoot, "scripts/vendored-assets.json"), "utf8"));
  const maplibre = manifest.packages.find((pkg) => pkg.name === "maplibre-gl");
  assert.ok(maplibre, "scripts/vendored-assets.json must pin maplibre-gl");
  assert.match(maplibre.version, /^\d+\.\d+\.\d+$/, "the pin must be an exact version, never a range");

  const readme = readFileSync(resolve(wwwroot, "vendor/maplibre-gl/README.md"), "utf8");
  assert.match(readme, new RegExp(`maplibre-gl@${maplibre.version.replace(/\./g, "\\.")}`));
  assert.match(readme, /node scripts\/vendor-assets\.mjs --update/, "the update step must be documented");
});

test("map-preview.js loads MapLibre from this origin, not a CDN", () => {
  const source = readFileSync(resolve(wwwroot, "map-preview.js"), "utf8");
  assert.match(
    source,
    /_content\/Honua\.Console\.Shell\/vendor\/maplibre-gl\//,
    "MapLibre must be referenced through the vendored same-origin path",
  );
  assert.doesNotMatch(source, /unpkg\.com/, "MapLibre must no longer be fetched from unpkg");
});

test("no wwwroot interop script reaches an undeclared external origin", () => {
  for (const { name, source } of interopScripts) {
    for (const origin of fetchedOrigins(source)) {
      assert.ok(
        DECLARED_EXTERNAL_ORIGINS.has(origin),
        `wwwroot/${name} loads from ${origin}, which is not declared in DECLARED_EXTERNAL_ORIGINS. ` +
          "Vendor the asset (scripts/vendor-assets.mjs) rather than widening the allowlist.",
      );
    }
  }
});

test("the CSP admits exactly the external origins the interop scripts still use", () => {
  const program = readFileSync(programPath, "utf8");
  const csp = program.slice(program.indexOf("var contentSecurityPolicy"), program.indexOf("app.Use(async"));
  assert.ok(csp.includes("script-src"), "could not locate the CSP definition in Program.cs");

  const policyOrigins = externalOrigins(csp);
  const scriptOrigins = new Set(interopScripts.flatMap(({ source }) => [...fetchedOrigins(source)]));

  // Nothing a script fetches may be blocked by the policy...
  for (const origin of scriptOrigins) {
    assert.ok(policyOrigins.has(origin), `${origin} is used by an interop script but not admitted by the CSP`);
  }
  // ...and the policy may not admit an origin nothing uses. Vendoring a library must shrink the CSP.
  for (const origin of policyOrigins) {
    assert.ok(
      scriptOrigins.has(origin),
      `the CSP admits ${origin} but no wwwroot interop script loads from it — remove the directive entry`,
    );
  }

  // The specific regression #333 is about.
  assert.doesNotMatch(csp, /unpkg\.com/, "the CSP must no longer admit unpkg.com");
});

test("the NOTICE records the vendored third-party asset and its license", () => {
  const notice = readFileSync(resolve(repoRoot, "NOTICE"), "utf8");
  const lock = JSON.parse(readFileSync(resolve(repoRoot, "scripts/vendored-assets.lock.json"), "utf8"));
  for (const [name, locked] of Object.entries(lock.packages)) {
    assert.ok(notice.includes(name), `NOTICE must attribute the vendored ${name}`);
    assert.ok(notice.includes(locked.license), `NOTICE must record the ${locked.license} license of ${name}`);
  }
});
