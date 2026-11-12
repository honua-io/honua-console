#!/usr/bin/env node
// Renders the Console Content-Security-Policy from config/console-csp.json, the single source of
// truth for every delivery form:
//   --header[=app|embed]       print a response-header policy (CDN / static host)
//   --write [file]             stamp the <meta> fallback into index.html (default: the Web project's)
//   --check [file]             exit 1 when that file's <meta> policy differs from the source
//   --verify-origin <url>      exit 1 when a deployed origin serves different header forms

import { readFileSync, writeFileSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const repoRoot = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const sourcePath = resolve(repoRoot, "config/console-csp.json");
export const defaultIndexPath = resolve(repoRoot, "src/Honua.Console.Web/wwwroot/index.html");
const metaPattern = /<meta http-equiv="Content-Security-Policy" content="([^"]*)" \/>/;
const charsetPattern = /(<meta charset="utf-8" \/>\n)(\s*)/;
const CONSOLE_BASE = "/console/";
const SAMPLE_PATHS = { app: "", embed: "embed/maps/csp-check" };

export function loadPolicySource(path = sourcePath) {
  return JSON.parse(readFileSync(path, "utf8"));
}

function render(directives) {
  return directives.map(({ name, sources }) => [name, ...sources].join(" ")).join("; ");
}

export function metaPolicy(source = loadPolicySource()) {
  return render(source.directives);
}

export function headerPolicy(form = "app", source = loadPolicySource()) {
  const headerForm = source.headerForms[form];
  if (!headerForm) throw new Error(`unknown header form "${form}"; expected ${Object.keys(source.headerForms).join(" or ")}`);
  return render([...source.directives, ...headerForm.add]);
}

export function metaTag(policy = metaPolicy()) {
  return `<meta http-equiv="Content-Security-Policy" content="${policy}" />`;
}

export function readMetaPolicy(html) {
  return html.match(metaPattern)?.[1] ?? null;
}

export function stampMetaTag(html) {
  if (metaPattern.test(html)) return html.replace(metaPattern, metaTag());
  if (charsetPattern.test(html)) return html.replace(charsetPattern, `$1$2${metaTag()}\n$2`);
  throw new Error('found neither a CSP <meta> nor <meta charset="utf-8" /> to place one after');
}

export async function verifyServedHeaders(origin, { fetchImpl = globalThis.fetch, base = CONSOLE_BASE } = {}) {
  const problems = [];
  for (const [form, path] of Object.entries(SAMPLE_PATHS)) {
    const url = new URL(`${base}${path}`, origin);
    const response = await fetchImpl(url, { headers: { Accept: "text/html" }, redirect: "manual" });
    if (response.status !== 200) {
      problems.push(`${url}: returned HTTP ${response.status}, expected 200 from the Console fallback`);
      continue;
    }
    const served = response.headers.get("content-security-policy");
    if (served !== headerPolicy(form)) problems.push(`${url}: served ${served ?? "no"} CSP, expected the ${form} form`);
  }
  return problems;
}

async function main([command, argument]) {
  const [verb, form] = command?.split("=") ?? [];
  if (verb === "--header") {
    if (argument !== undefined) throw new Error(`--header takes no argument; use --header=${argument}`);
    process.stdout.write(`${headerPolicy(form ?? "app")}\n`);
    return 0;
  }
  const file = argument ?? defaultIndexPath;
  if (command === "--write") {
    writeFileSync(file, stampMetaTag(readFileSync(file, "utf8")));
    return 0;
  }
  if (command === "--check") {
    if (readMetaPolicy(readFileSync(file, "utf8")) === metaPolicy()) return 0;
    process.stderr.write(`${file}: CSP <meta> differs from config/console-csp.json; run node scripts/console-csp.mjs --write\n`);
    return 1;
  }
  if (command === "--verify-origin" && argument) {
    const problems = await verifyServedHeaders(argument);
    for (const problem of problems) process.stderr.write(`${problem}\n`);
    return problems.length === 0 ? 0 : 1;
  }
  process.stderr.write("usage: console-csp.mjs --header[=app|embed] | --write [index.html] | --check [index.html] | --verify-origin <url>\n");
  return 2;
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try {
    process.exitCode = await main(process.argv.slice(2));
  } catch (error) {
    process.stderr.write(`console-csp: ${error.message}\n`);
    process.exitCode = 1;
  }
}
