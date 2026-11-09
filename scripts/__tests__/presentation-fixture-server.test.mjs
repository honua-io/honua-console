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
  await fs.writeFile(path.join(assets, "private.json"), "private");
  for (const assetPrefix of ["/", "/_content/Honua.Console.Shell/"]) {
    const server = await startPresentationFixtureServer({
      pages: new Map([["page", "<h1>Evidence</h1>"], ["navigation", "<nav>Browse</nav>"]]),
      title: "Fixture",
      stylesheets: [`${assetPrefix}fixture.css`],
      assets,
      assetPrefix,
      unwrappedPages: ["navigation"],
    });
    try {
      const origin = `http://127.0.0.1:${server.address().port}`;
      const response = await fetch(`${origin}/page`);
      assert.match(response.headers.get("content-security-policy"), /script-src 'none'/);
      assert.match(await response.text(), /<main class="console-main"><h1>Evidence<\/h1><\/main>/);
      const navigation = await (await fetch(`${origin}/navigation`)).text();
      assert.match(navigation, /<body><nav>Browse<\/nav><\/body>/);
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
