import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { createHash } from "node:crypto";
import { mkdtemp, mkdir, readFile, rm, symlink, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { dirname, join, resolve } from "node:path";
import { test } from "node:test";
import { fileURLToPath } from "node:url";

const root = resolve(dirname(fileURLToPath(import.meta.url)), "../..");

test("static bundle supersedes stale pull-request heads but never cancels release runs", async () => {
  const workflow = await readFile(join(root, ".github/workflows/static-bundle.yml"), "utf8");
  assert.match(
    workflow,
    /concurrency:\n(?:  #.*\n)*  group: \$\{\{ github\.workflow \}\}-\$\{\{ github\.event_name == 'pull_request' && github\.event\.pull_request\.number \|\| github\.run_id \}\}\n  cancel-in-progress: \$\{\{ github\.event_name == 'pull_request' \}\}\n/,
  );
});

test("every trunk commit publishes a SHA-bound bundle the release resolver can bind", async () => {
  const workflow = await readFile(join(root, ".github/workflows/static-bundle.yml"), "utf8");
  assert.match(workflow, /\n  push:\n    branches: \[trunk\]\n/);
  assert.match(workflow, /SOURCE_SHA: \$\{\{ github\.sha \}\}/);
  assert.match(workflow, /elif \[ -z "\$PR_NUMBER" \]; then\n\s+version="0\.0\.0-trunk\.\$\{SOURCE_SHA:0:12\}"/);
  const attest = workflow.indexOf("  attest:\n");
  assert.ok(attest > 0);
  const job = workflow.slice(attest);
  assert.match(job, /if: github.event_name == 'push'/);
  assert.match(job, /id-token: write\n      attestations: write/);
  assert.match(job, /sha256sum --check --strict/);
  assert.ok(job.indexOf("sha256sum --check") < job.indexOf("actions/attest-build-provenance@"));
  assert.doesNotMatch(workflow.slice(0, attest), /id-token: write|attestations: write/);
});

test("static release validates locked assets before packaging and limits release writes", async () => {
  const workflow = await readFile(join(root, ".github/workflows/static-bundle.yml"), "utf8");
  assert.match(workflow, /permissions:\n  contents: read/);
  const release = workflow.indexOf("  release:\n    name:");
  assert.ok(release > 0);
  assert.doesNotMatch(workflow.slice(0, release), /contents: write|packages: write|pull_request_target:/);
  assert.match(workflow.slice(release), /if: github.event_name == 'release'/);
  assert.match(workflow.slice(release), /contents: write/);
  assert.match(workflow, /--configfile NuGet.config --no-cache --locked-mode/);
  assert.match(workflow, /cp src\/Honua.Console.Web\/global.json global.json/);
  assert.match(workflow, /--repo honua-io\/honua-studio/);
  assert.match(workflow, /--signer-workflow honua-io\/honua-studio\/\.github\/workflows\/release.yml/);
  assert.match(workflow, /--source-ref/);
  assert.match(workflow, /--source-digest/);
  assert.match(workflow, /--deny-self-hosted-runners/);
  const publish = workflow.indexOf("dotnet publish");
  const verifyCesium = workflow.indexOf("node scripts/verify-published-cesium.mjs");
  const verifyStudio = workflow.indexOf("--verify artifacts/static-publish/wwwroot/console/_studio");
  const browser = workflow.indexOf("playwright test --config playwright.static.config.ts");
  const pack = workflow.indexOf("python3 scripts/package-console-static.py");
  assert.ok(workflow.indexOf("gh attestation verify") < publish);
  assert.ok(publish < verifyCesium && verifyCesium < pack);
  assert.ok(publish < verifyStudio && verifyStudio < pack);
  assert.ok(verifyCesium < browser && verifyStudio < browser && browser < pack);
  assert.match(workflow, /gh release upload "\$RELEASE_TAG" static-release\/\* <\/dev\/null/);
  assert.doesNotMatch(workflow, /--clobber/);
  for (const match of workflow.matchAll(/uses:\s+([^\s]+)/g)) {
    assert.ok(match[1].startsWith("./") || /@[a-f0-9]{40}$/.test(match[1]), match[1]);
  }
});

test("release ZIP contains browser files and license texts only and its receipt verifies the actual bytes", async () => {
  const directory = await mkdtemp(join(tmpdir(), "console-static-"));
  const source = join(directory, "publish", "wwwroot", "console");
  const output = join(directory, "release");
  const run = (...extra) => spawnSync("python3", [join(root, "scripts/package-console-static.py"), source,
    "--output", output, "--version", "0.1.0-rc.2", ...extra], { encoding: "utf8" });
  try {
    await mkdir(join(source, "_framework"), { recursive: true });
    await writeFile(join(source, "index.html"), '<base href="/console/">');
    await writeFile(join(source, "config.json"), '{"apiBaseUrl":"/"}');
    await writeFile(join(source, "_framework", "blazor.webassembly.js"), "// runtime");
    await writeFile(join(directory, "publish", "host-secret.json"), "not a browser file");
    const result = run();
    assert.equal(result.status, 0, result.stderr);
    const receipt = JSON.parse(result.stdout);
    const bundle = await readFile(join(output, receipt.asset));
    const legal = ["LICENSE", "NOTICE", ...JSON.parse(await readFile(join(root, "release/licenses/sources.json"), "utf8"))
      .files.map(entry => `licenses/${entry.file}`), "licenses/sources.json"];
    assert.equal(receipt.files, 3 + legal.length);
    assert.equal(receipt.zipBytes, bundle.length);
    assert.equal(receipt.sha256, createHash("sha256").update(bundle).digest("hex"));
    assert.deepEqual(JSON.parse(await readFile(join(output, `${receipt.asset}.json`))), receipt);
    const inspect = spawnSync("python3", ["-c", "import json,sys,zipfile; print(json.dumps(zipfile.ZipFile(sys.argv[1]).namelist()))", join(output, receipt.asset)], { encoding: "utf8" });
    assert.equal(inspect.status, 0, inspect.stderr);
    assert.deepEqual(JSON.parse(inspect.stdout), ["_framework/blazor.webassembly.js", "config.json", "index.html", ...legal].sort());
    const bytes = spawnSync("python3", ["-c", "import sys,zipfile; sys.stdout.write(zipfile.ZipFile(sys.argv[1]).read('NOTICE').decode())", join(output, receipt.asset)], { encoding: "utf8" });
    assert.equal(bytes.stdout, await readFile(join(root, "NOTICE"), "utf8"), "the bundle carries the Console NOTICE as is");
    await writeFile(join(source, "LICENSE"), "a published file must not replace the Console LICENSE");
    assert.notEqual(run().status, 0, "published files cannot shadow the release licence files");
    await rm(join(source, "LICENSE"));
    assert.notEqual(run("--version", "../../escape").status, 0);
    assert.notEqual(run("--output", join(source, "release")).status, 0);
    await symlink(join(directory, "publish", "host-secret.json"), join(source, "secret.json"));
    assert.notEqual(run().status, 0, "symlinked files must not enter a release");
    await rm(join(source, "secret.json"));
    await writeFile(join(source, "index.html"), '<base href="/">');
    assert.notEqual(run().status, 0, "the root host base cannot ship under /console/");
    await rm(join(source, "config.json"));
    assert.notEqual(run().status, 0, "missing deployment configuration must fail packaging");
  } finally {
    await rm(directory, { recursive: true, force: true });
  }
});

test("attest job enriches the CycloneDX SBOM with Studio, Cesium and WASM runtime and keeps the NuGet guard", async () => {
  const workflow = await readFile(join(root, ".github/workflows/static-bundle.yml"), "utf8");
  const attest = workflow.slice(workflow.indexOf("  attest:\n"));
  assert.match(attest, /persist-credentials: false/);
  assert.match(attest, /sparse-checkout:\s*\|\n\s+src\n\s+scripts\n\s+release\/licenses/);
  assert.match(attest, /python3 source\/scripts\/enrich-bundle-sbom\.py/);
  assert.match(attest, /--validate-only/);
  assert.ok(attest.indexOf("anchore/sbom-action@") < attest.indexOf("enrich-bundle-sbom.py"));
  assert.ok(attest.indexOf("enrich-bundle-sbom.py") < attest.indexOf("--validate-only"));
  assert.ok(attest.indexOf("--validate-only") < attest.indexOf("actions/attest-sbom@"));
});

async function createSbomFixtureBundle(directory, { includeStudio = true, includeCesium = true, includeWasm = true } = {}) {
  const source = join(directory, "publish", "wwwroot", "console");
  const releaseDir = join(directory, "release");
  const vendoredLock = JSON.parse(await readFile(join(root, "scripts/vendored-assets.lock.json"), "utf8"));
  await mkdir(join(source, "_framework"), { recursive: true });
  await writeFile(join(source, "index.html"), '<base href="/console/">');
  await writeFile(join(source, "config.json"), '{"apiBaseUrl":"/"}');
  await writeFile(join(source, "_framework/blazor.webassembly.js"), "// blazor");
  if (includeWasm) {
    await writeFile(join(source, "_framework/dotnet.native.wasm"), Buffer.from([0x00, 0x61, 0x73, 0x6d]));
  }
  if (includeCesium) {
    await mkdir(join(source, "_content/Honua.Console.Shell/vendor/cesium"), { recursive: true });
    await writeFile(join(source, "_content/Honua.Console.Shell/vendor/cesium/Cesium.js"), "// cesium");
  }
  for (const name of Object.keys(vendoredLock.packages)) {
    if (name === "cesium") continue;
    await mkdir(join(source, `_content/Honua.Console.Shell/vendor/${name}`), { recursive: true });
    await writeFile(join(source, `_content/Honua.Console.Shell/vendor/${name}/index.js`), `// ${name}`);
  }
  if (includeStudio) {
    await mkdir(join(source, "_studio"), { recursive: true });
    await writeFile(join(source, "_studio/index.js"), "// studio");
  }
  const packed = spawnSync(
    "python3",
    [join(root, "scripts/package-console-static.py"), source, "--output", releaseDir, "--version", "0.1.0-rc.3"],
    { encoding: "utf8" },
  );
  assert.equal(packed.status, 0, packed.stderr);
  return join(releaseDir, JSON.parse(packed.stdout).asset);
}

function runEnrichSbom(sbomPath, bundlePath, outPath) {
  return spawnSync(
    "python3",
    [
      join(root, "scripts/enrich-bundle-sbom.py"),
      "--sbom", sbomPath,
      "--bundle", bundlePath,
      "--studio-lock", join(root, "scripts/studio-release.lock.json"),
      "--cesium-lock", join(root, "scripts/cesium-extracted-tree.lock.json"),
      "--vendored-lock", join(root, "scripts/vendored-assets.lock.json"),
      "--licenses-sources", join(root, "release/licenses/sources.json"),
      "--output", outPath,
    ],
    { encoding: "utf8" },
  );
}

function sampleNugetBom() {
  return {
    bomFormat: "CycloneDX",
    specVersion: "1.6",
    metadata: { component: { "bom-ref": "honua-console", type: "application", name: "honua-console" } },
    components: [
      { "bom-ref": "pkg:nuget/Honua.Sdk.Admin@2.2.0", type: "library", name: "Honua.Sdk.Admin", version: "2.2.0", purl: "pkg:nuget/Honua.Sdk.Admin@2.2.0" },
      { "bom-ref": "pkg:nuget/Microsoft.AspNetCore.App.Internal.Assets@11.0.0", type: "library", name: "Microsoft.AspNetCore.App.Internal.Assets", version: "11.0.0", purl: "pkg:nuget/Microsoft.AspNetCore.App.Internal.Assets@11.0.0" },
      { "bom-ref": "pkg:nuget/Microsoft.AspNetCore.Components.Analyzers@11.0.0", type: "library", name: "Microsoft.AspNetCore.Components.Analyzers", version: "11.0.0", purl: "pkg:nuget/Microsoft.AspNetCore.Components.Analyzers@11.0.0" },
      { "bom-ref": "pkg:nuget/Microsoft.NET.ILLink.Tasks@11.0.0", type: "library", name: "Microsoft.NET.ILLink.Tasks", version: "11.0.0", purl: "pkg:nuget/Microsoft.NET.ILLink.Tasks@11.0.0" },
      { "bom-ref": "pkg:nuget/Microsoft.AspNetCore.Components.WebAssembly.DevServer@11.0.0", type: "library", name: "Microsoft.AspNetCore.Components.WebAssembly.DevServer", version: "11.0.0", purl: "pkg:nuget/Microsoft.AspNetCore.Components.WebAssembly.DevServer@11.0.0" },
      { "bom-ref": "pkg:nuget/Microsoft.NET.Sdk.WebAssembly.Pack@11.0.0", type: "library", name: "Microsoft.NET.Sdk.WebAssembly.Pack", version: "11.0.0", purl: "pkg:nuget/Microsoft.NET.Sdk.WebAssembly.Pack@11.0.0" },
    ],
    dependencies: [
      { ref: "honua-console", dependsOn: ["pkg:nuget/Honua.Sdk.Admin@2.2.0", "pkg:nuget/Microsoft.NET.ILLink.Tasks@11.0.0"] },
      { ref: "pkg:nuget/Microsoft.NET.ILLink.Tasks@11.0.0", dependsOn: [] },
    ],
  };
}

function assertEnrichedComponents(byPurl, studioLock, cesiumLock, vendoredLock, wasmVersion) {
  assert.ok(byPurl.has("pkg:nuget/Honua.Sdk.Admin@2.2.0"));
  assert.ok(byPurl.has("pkg:nuget/Microsoft.AspNetCore.App.Internal.Assets@11.0.0"));
  for (const dropped of [
    "pkg:nuget/Microsoft.AspNetCore.Components.Analyzers@11.0.0",
    "pkg:nuget/Microsoft.NET.ILLink.Tasks@11.0.0",
    "pkg:nuget/Microsoft.AspNetCore.Components.WebAssembly.DevServer@11.0.0",
    "pkg:nuget/Microsoft.NET.Sdk.WebAssembly.Pack@11.0.0",
  ]) {
    assert.equal(byPurl.has(dropped), false, `expected ${dropped} stripped`);
  }

  const studio = byPurl.get(`pkg:github/${studioLock.repository}@${studioLock.tag}`);
  assert.equal(studio.version, studioLock.tag.replace(/^v/, ""));
  assert.deepEqual(studio.hashes, [{ alg: "SHA-256", content: studioLock.sha256 }]);
  assert.deepEqual(studio.licenses, [{ license: { id: "Apache-2.0" } }]);

  const cesium = byPurl.get(`pkg:npm/${cesiumLock.package}@${cesiumLock.version}`);
  assert.deepEqual(cesium.hashes, [{ alg: "SHA-256", content: cesiumLock.archiveSha256 }]);
  assert.ok(cesium.properties.some((p) => p.name === "honua:cesium:treeSha256" && p.value === cesiumLock.treeSha256));

  assert.ok(byPurl.has(`pkg:npm/maplibre-gl@${vendoredLock.packages["maplibre-gl"].version}`));

  const runtime = byPurl.get(`pkg:nuget/Microsoft.NETCore.App.Runtime.Mono.browser-wasm@${wasmVersion}`);
  assert.equal(runtime.type, "framework");
  assert.equal(runtime.hashes, undefined);
  assert.ok(runtime.properties.some((p) => p.name === "honua:framework:manifestSha256" && /^[0-9a-f]{64}$/.test(p.value)));
  const expectedNativeHash = createHash("sha256").update(Buffer.from([0x00, 0x61, 0x73, 0x6d])).digest("hex");
  assert.ok(runtime.properties.some((p) => p.name === "honua:framework:nativeWasmSha256" && p.value === expectedNativeHash));
}

async function assertMissingAssetRejected(nugetSbomPath, outPath, opts, pattern) {
  const subDir = await mkdtemp(join(tmpdir(), "console-sbom-miss-"));
  try {
    const badBundle = await createSbomFixtureBundle(subDir, opts);
    const res = runEnrichSbom(nugetSbomPath, badBundle, outPath);
    assert.notEqual(res.status, 0);
    assert.match(res.stderr, pattern);
  } finally {
    await rm(subDir, { recursive: true, force: true });
  }
}

test("enrich-bundle-sbom adds Studio, Cesium, vendored JS and WASM runtime and strips build-only NuGet", async () => {
  const directory = await mkdtemp(join(tmpdir(), "console-sbom-"));
  const nugetSbomPath = join(directory, "nuget.cdx.json");
  const outputSbomPath = join(directory, "bundle.cdx.json");
  const studioLock = JSON.parse(await readFile(join(root, "scripts/studio-release.lock.json"), "utf8"));
  const cesiumLock = JSON.parse(await readFile(join(root, "scripts/cesium-extracted-tree.lock.json"), "utf8"));
  const vendoredLock = JSON.parse(await readFile(join(root, "scripts/vendored-assets.lock.json"), "utf8"));
  const sources = JSON.parse(await readFile(join(root, "release/licenses/sources.json"), "utf8"));
  const wasmVersion = sources.files.find((f) => f.source.includes("Mono.browser-wasm/")).source.split("/")[1];
  try {
    const bundlePath = await createSbomFixtureBundle(directory);
    await writeFile(nugetSbomPath, JSON.stringify(sampleNugetBom(), null, 2));
    const ok = runEnrichSbom(nugetSbomPath, bundlePath, outputSbomPath);
    assert.equal(ok.status, 0, ok.stderr);
    const enriched = JSON.parse(await readFile(outputSbomPath, "utf8"));
    const byPurl = new Map(enriched.components.map((c) => [c.purl, c]));
    assertEnrichedComponents(byPurl, studioLock, cesiumLock, vendoredLock, wasmVersion);

    const validateOk = spawnSync("python3", [join(root, "scripts/enrich-bundle-sbom.py"), "--validate-only", "--sbom", outputSbomPath], { encoding: "utf8" });
    assert.equal(validateOk.status, 0, validateOk.stderr);
  } finally {
    await rm(directory, { recursive: true, force: true });
  }
});

test("enrich-bundle-sbom is idempotent and fails closed on missing NuGet graph or bundled assets", async () => {
  const directory = await mkdtemp(join(tmpdir(), "console-sbom-neg-"));
  const nugetSbomPath = join(directory, "nuget.cdx.json");
  const out1 = join(directory, "bundle-1.cdx.json");
  const out2 = join(directory, "bundle-2.cdx.json");
  try {
    const bundlePath = await createSbomFixtureBundle(directory);
    await writeFile(nugetSbomPath, JSON.stringify(sampleNugetBom(), null, 2));
    assert.equal(runEnrichSbom(nugetSbomPath, bundlePath, out1).status, 0);
    assert.equal(runEnrichSbom(out1, bundlePath, out2).status, 0);
    assert.equal(
      JSON.parse(await readFile(out2, "utf8")).components.length,
      JSON.parse(await readFile(out1, "utf8")).components.length,
    );

    const emptyNugetPath = join(directory, "empty.cdx.json");
    await writeFile(emptyNugetPath, JSON.stringify({ bomFormat: "CycloneDX", components: [] }));
    const emptyRes = runEnrichSbom(emptyNugetPath, bundlePath, out2);
    assert.notEqual(emptyRes.status, 0);
    assert.match(emptyRes.stderr, /NuGet graph/);

    const buildOnlyPath = join(directory, "build-only.cdx.json");
    await writeFile(buildOnlyPath, JSON.stringify({
      bomFormat: "CycloneDX",
      components: [{ "bom-ref": "pkg:nuget/Microsoft.NET.ILLink.Tasks@11.0.0", type: "library", name: "Microsoft.NET.ILLink.Tasks", version: "11.0.0", purl: "pkg:nuget/Microsoft.NET.ILLink.Tasks@11.0.0" }],
    }));
    const buildOnlyRes = runEnrichSbom(buildOnlyPath, bundlePath, out2);
    assert.notEqual(buildOnlyRes.status, 0);
    assert.match(buildOnlyRes.stderr, /no shipped NuGet components remaining/);

    for (const [opts, pattern] of [
      [{ includeStudio: false }, /missing shipped Studio assets/],
      [{ includeCesium: false }, /missing shipped Cesium assets/],
      [{ includeWasm: false }, /no \.wasm runtime\/framework files/],
    ]) {
      await assertMissingAssetRejected(nugetSbomPath, out2, opts, pattern);
    }
  } finally {
    await rm(directory, { recursive: true, force: true });
  }
});
