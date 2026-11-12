import { strict as assert } from "node:assert";
import { readFileSync } from "node:fs";
import { test } from "node:test";

import { defaultIndexPath, headerPolicy, loadDirectives, metaPolicy, readMetaPolicy } from "../console-csp.mjs";

const indexHtml = readFileSync(defaultIndexPath, "utf8");
const directive = (policy, name) => policy.split("; ").find((entry) => entry.startsWith(`${name} `));

test("index.html carries the meta policy generated from config/console-csp.json", () => {
  assert.equal(
    readMetaPolicy(indexHtml),
    metaPolicy(),
    "run `node scripts/console-csp.mjs --write` after editing config/console-csp.json",
  );
  assert.ok(
    indexHtml.indexOf("Content-Security-Policy") < indexHtml.indexOf("<link"),
    "the meta policy must precede every stylesheet and script it governs",
  );
});

test("every directive is unique and justified", () => {
  const directives = loadDirectives();
  assert.equal(new Set(directives.map(({ name }) => name)).size, directives.length);
  for (const { name, reason } of directives) assert.ok(reason?.length > 0, `${name} needs a reason`);
});

test("the header form adds what a meta policy cannot carry", () => {
  assert.equal(directive(headerPolicy(), "frame-ancestors"), "frame-ancestors 'none'");
  assert.equal(directive(metaPolicy(), "frame-ancestors"), undefined);
  assert.ok(headerPolicy().startsWith(metaPolicy()));
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
