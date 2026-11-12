import { strict as assert } from "node:assert";
import { readFileSync } from "node:fs";
import { spawnSync } from "node:child_process";
import { createServer } from "node:http";
import { fileURLToPath } from "node:url";
import { test } from "node:test";

import {
  defaultIndexPath,
  headerPolicy,
  loadPolicySource,
  metaPolicy,
  readMetaPolicy,
  stampMetaTag,
  verifyServedHeaders,
} from "../console-csp.mjs";

const indexHtml = readFileSync(defaultIndexPath, "utf8");
const cspScript = fileURLToPath(new URL("../console-csp.mjs", import.meta.url));
const directive = (policy, name) => policy.split("; ").find((entry) => entry.startsWith(`${name} `));

test("index.html carries the meta policy generated from config/console-csp.json", () => {
  assert.equal(
    readMetaPolicy(indexHtml),
    metaPolicy(),
    "run `node scripts/console-csp.mjs --write` after editing config/console-csp.json",
  );
  const metaAt = indexHtml.indexOf("Content-Security-Policy");
  for (const governed of ["<base", "<link", "<script"]) {
    assert.ok(metaAt < indexHtml.indexOf(governed), `the meta policy must precede the first ${governed}`);
  }
});

test("--write refuses a document it cannot place the policy in", () => {
  assert.throws(() => stampMetaTag("<html><head></head></html>"), /neither a CSP <meta>/);
  assert.equal(readMetaPolicy(stampMetaTag('<head>\n  <meta charset="utf-8" />\n  <base href="/" />')), metaPolicy());
});

test("every directive is unique and justified", () => {
  const source = loadPolicySource();
  const forms = Object.values(source.headerForms);
  const all = [...source.directives, ...forms.flatMap((form) => form.add)];
  for (const { name, reason } of all) assert.ok(reason?.length > 0, `${name} needs a reason`);
  for (const form of forms) {
    const names = [...source.directives, ...form.add].map(({ name }) => name);
    assert.equal(new Set(names).size, names.length);
  }
});

test("the app header limits framing to the Console; the embed header leaves framing open", () => {
  assert.equal(directive(headerPolicy("app"), "frame-ancestors"), "frame-ancestors 'self'");
  assert.equal(directive(headerPolicy("embed"), "frame-ancestors"), undefined);
  assert.equal(headerPolicy("embed"), metaPolicy());
  assert.equal(directive(metaPolicy(), "frame-ancestors"), undefined);
  assert.throws(() => headerPolicy("unknown"), /unknown header form/);
});

test("the policy never admits inline script or plugins", () => {
  const scriptSrc = directive(headerPolicy(), "script-src");
  assert.doesNotMatch(scriptSrc, /'unsafe-inline'|https:|data:|blob:|\*/);
  assert.equal(directive(headerPolicy(), "object-src"), "object-src 'none'");
  assert.equal(directive(headerPolicy(), "base-uri"), "base-uri 'self'");
});

test("index.html has no inline script or inline event handler", () => {
  for (const [tag] of indexHtml.matchAll(/<script\b[^>]*>/g)) {
    assert.match(tag, /\bsrc="/, `inline script is blocked by script-src: ${tag}`);
  }
  assert.doesNotMatch(indexHtml, /\son[a-z]+="/, "inline event handlers are blocked by script-src");
  assert.doesNotMatch(indexHtml, /href="javascript:/i);
});

test("--verify-origin compares the served app and embed headers with the source", async () => {
  let embedPolicy = headerPolicy("embed");
  let embedStatus = 200;
  const server = createServer((request, response) => {
    const embed = request.url.startsWith("/console/embed/");
    response.writeHead(embed ? embedStatus : 200, { "Content-Security-Policy": embed ? embedPolicy : headerPolicy("app") }).end();
  });
  await new Promise((listening) => server.listen(0, "127.0.0.1", listening));
  try {
    const origin = `http://127.0.0.1:${server.address().port}`;
    assert.deepEqual(await verifyServedHeaders(origin), []);
    embedPolicy = headerPolicy("app");
    const problems = await verifyServedHeaders(origin);
    assert.equal(problems.length, 1);
    assert.match(problems[0], /embed\/maps\/csp-check: .*expected the embed form/);
    embedPolicy = headerPolicy("embed");
    for (const status of [302, 404]) {
      embedStatus = status;
      const refused = await verifyServedHeaders(origin);
      assert.equal(refused.length, 1);
      assert.match(refused[0], new RegExp(`returned HTTP ${status}`));
    }
  } finally {
    server.close();
  }
});

test("--header rejects a space-separated form name instead of printing the app form", () => {
  const result = spawnSync(process.execPath, [cspScript, "--header", "embed"], { encoding: "utf8" });
  assert.notEqual(result.status, 0);
  assert.match(result.stderr, /--header=embed/);
});
