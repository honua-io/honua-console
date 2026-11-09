import assert from "node:assert/strict";
import fs from "node:fs/promises";
import os from "node:os";
import path from "node:path";
import test from "node:test";
import { startPresentationFixtureServer } from "../../e2e/playwright/presentation-fixture-server.mjs";

test("presentation fixtures serve configured assets and isolate the asset root", async t => {
  const directory = await fs.mkdtemp(path.join(os.tmpdir(), "console-preview-"));
  t.after(() => fs.rm(directory, { recursive: true, force: true }));
  const assets = path.join(directory, "assets");
  await fs.mkdir(assets);
  await fs.writeFile(path.join(assets, "fixture.css"), "body { color: black; }");
  await fs.writeFile(path.join(directory, "outside.css"), "private");
  await fs.writeFile(path.join(assets, "navigation.js"), "document.body.dataset.ready = true;");
  await fs.writeFile(path.join(assets, "private.json"), "private");
  for (const assetPrefix of ["/", "/_content/Honua.Console.Shell/"]) {
    const server = await startPresentationFixtureServer({
      pages: new Map([["page", "<h1>Evidence</h1>"], ["navigation", "<nav>Browse</nav>"]]),
      title: "Fixture",
      stylesheets: [`${assetPrefix}fixture.css`],
      assets,
      assetPrefix,
      unwrappedPages: ["navigation"],
      scripts: new Map([["navigation", [`${assetPrefix}navigation.js`]]]),
    });
    try {
      const origin = `http://127.0.0.1:${server.address().port}`;
      const response = await fetch(`${origin}/page`);
      assert.match(response.headers.get("content-security-policy"), /script-src 'none'/);
      assert.match(await response.text(), /<main class="console-main"><h1>Evidence<\/h1><\/main>/);
      const navigationResponse = await fetch(`${origin}/navigation`);
      assert.match(navigationResponse.headers.get("content-security-policy"), /script-src 'self'/);
      const navigation = await navigationResponse.text();
      assert.ok(navigation.includes(`<body><nav>Browse</nav><script src="${assetPrefix}navigation.js" defer></script></body>`));
      const script = await fetch(`${origin}${assetPrefix}navigation.js`);
      assert.equal(script.status, 200);
      assert.equal(script.headers.get("content-type"), "text/javascript");
      const stylesheet = await fetch(`${origin}${assetPrefix}fixture.css`);
      assert.equal(stylesheet.status, 200);
      assert.equal(stylesheet.headers.get("content-type"), "text/css");
      for (const suffix of ["missing.css", "private.json", "..%2foutside.css"]) {
        assert.equal((await fetch(`${origin}${assetPrefix}${suffix}`)).status, 404);
      }
      if (assetPrefix !== "/") assert.equal((await fetch(`${origin}/fixture.css`)).status, 404);
    } finally {
      await new Promise(resolve => server.close(resolve));
    }
  }
});
