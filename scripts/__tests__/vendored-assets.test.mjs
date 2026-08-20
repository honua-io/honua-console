import { strict as assert } from "node:assert";
import { readFileSync, readdirSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { test } from "node:test";
import { fileURLToPath } from "node:url";

import { verify } from "../vendor-assets.mjs";

// honua-console#333, #334 — the Console must not fetch executable code from a CDN at page load.
//
// As of #334 there is NO remaining executable-code CDN: MapLibre and Vega are committed vendored
// assets, and Cesium is fetched from this origin under /vendor/cesium (placed at deploy/build time
// by scripts/fetch-cesium.mjs, gitignored because its Build tree is ~20 MB). The only external
// origin left is raster tile imagery.
//
// Three invariants, all offline so they run in the blocking CI gate (`npm test`):
//
//   1. The committed vendored assets are the bytes the lock says they are.
//   2. MapLibre (#333) and Vega (#334) are loaded from this origin, not unpkg/jsdelivr.
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

const manifest = JSON.parse(readFileSync(resolve(repoRoot, "scripts/vendored-assets.json"), "utf8"));

test("the vendored chart stack is on the patched Vega 6 compatibility line", () => {
  const versions = Object.fromEntries(manifest.packages.map((entry) => [entry.name, entry.version]));
  assert.equal(versions.vega, "6.4.0", "Vega must remain at or above the 6.2.0 XSS fix line");
  assert.equal(versions["vega-lite"], "6.4.3", "Vega-Lite must remain on its Vega 6 peer line");
  assert.equal(versions["vega-embed"], "7.1.0", "vega-embed must remain on its Vega 6-compatible line");
});

// Every library the Console vendors, and the interop module that must consume it from this origin.
// Adding a package here is what makes the two tests below cover it.
const VENDORED = [
  { name: "maplibre-gl", consumer: "map-preview.js" },
  { name: "vega", consumer: "chart-preview.js" },
  { name: "vega-lite", consumer: "chart-preview.js" },
  { name: "vega-embed", consumer: "chart-preview.js" },
];

test("every pinned version is recorded and updatable through a documented step", () => {
  for (const { name } of VENDORED) {
    const pkg = manifest.packages.find((entry) => entry.name === name);
    assert.ok(pkg, `scripts/vendored-assets.json must pin ${name}`);
    assert.match(pkg.version, /^\d+\.\d+\.\d+$/, `the ${name} pin must be an exact version, never a range`);

    const readme = readFileSync(resolve(repoRoot, pkg.destination, "README.md"), "utf8");
    const escapeRegExp = (value) => value.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
    assert.match(readme, new RegExp(`${escapeRegExp(name)}@${escapeRegExp(pkg.version)}`));
    assert.match(readme, /node scripts\/vendor-assets\.mjs --update/, "the update step must be documented");
  }
});

test("each interop script loads its library from this origin, not a CDN", () => {
  for (const { name, consumer } of VENDORED) {
    const source = readFileSync(resolve(wwwroot, consumer), "utf8");
    assert.match(
      source,
      new RegExp(`_content/Honua\\.Console\\.Shell/vendor/`),
      `${consumer} must reference its vendored assets through the same-origin path`,
    );
    const pkg = manifest.packages.find((entry) => entry.name === name);
    // The file the script actually asks for must be one this repo committed and locked.
    const asset = pkg.files.map((file) => file.split("/").pop()).find((file) => file.endsWith(".js"));
    assert.ok(
      source.includes(asset),
      `${consumer} must load the vendored ${asset}`,
    );
  }

  const mapPreview = readFileSync(resolve(wwwroot, "map-preview.js"), "utf8");
  assert.doesNotMatch(mapPreview, /unpkg\.com/, "MapLibre must no longer be fetched from unpkg");
  const chartPreview = readFileSync(resolve(wwwroot, "chart-preview.js"), "utf8");
  assert.doesNotMatch(chartPreview, /cdn\.jsdelivr\.net/, "Vega must no longer be fetched from jsdelivr");
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

// jsdelivr is still in the CSP, and #334 is explicit that it stays only for Cesium. Pinning that
// down here is what turns "we deferred Cesium" from a comment into a checked fact: the day
// As of #334 no interop script reaches a CDN for executable code at all: MapLibre and Vega are
// committed vendored assets, and Cesium is served from this origin under /vendor/cesium. This test
// is now an absolute — the day ANY script reaches for a code CDN again, it fails.
test("no interop script fetches executable code from a CDN", () => {
  // Absolute, not a shrinking allowlist. Tile imagery is the only external origin the Console may
  // reach (asserted by the set-equality test above); executable code must come from this origin.
  const CODE_CDNS = [
    "https://cdn.jsdelivr.net",
    "https://unpkg.com",
    "https://cdnjs.cloudflare.com",
    "https://esm.sh",
    "https://cdn.skypack.dev",
  ];
  const offenders = [];
  for (const { name, source } of interopScripts) {
    const origins = fetchedOrigins(source);
    for (const cdn of CODE_CDNS) {
      if (origins.has(cdn)) offenders.push(`${name} -> ${cdn}`);
    }
  }
  assert.deepEqual(
    offenders,
    [],
    "Executable code must be served from this origin. MapLibre and Vega are committed under " +
      "wwwroot/vendor; Cesium is placed at /vendor/cesium by scripts/fetch-cesium.mjs at " +
      "deploy time (honua-console#334). Vendor the new dependency rather than reaching for a CDN.",
  );
});

test("scene-viewer loads Cesium from this origin", () => {
  // The deploy-time fetch is only a real fix if the loader actually points at it. If Cesium is ever
  // pointed back at a CDN, the CDN test above catches it; this catches the subtler regression of a
  // wrong same-origin path that silently degrades every deployment to the SVG placeholder.
  const sceneViewer = readFileSync(resolve(wwwroot, "scene-viewer.js"), "utf8");
  assert.match(
    sceneViewer,
    /const CESIUM_BASE_URL = '\/vendor\/cesium\/'/,
    "scene-viewer.js must resolve Cesium under /vendor/cesium on this origin",
  );
  assert.doesNotMatch(
    sceneViewer,
    /integrity\s*=/,
    "same-origin assets need no Subresource Integrity — SRI defends against a third-party CDN, " +
      "and there is no third party left in this path",
  );
});

test("the NOTICE records the vendored third-party asset and its license", () => {
  const notice = readFileSync(resolve(repoRoot, "NOTICE"), "utf8");
  const lock = JSON.parse(readFileSync(resolve(repoRoot, "scripts/vendored-assets.lock.json"), "utf8"));
  for (const [name, locked] of Object.entries(lock.packages)) {
    assert.ok(notice.includes(name), `NOTICE must attribute the vendored ${name}`);
    assert.ok(notice.includes(locked.license), `NOTICE must record the ${locked.license} license of ${name}`);
  }
});
