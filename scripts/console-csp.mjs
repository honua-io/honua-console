#!/usr/bin/env node
// Renders the Console Content-Security-Policy from config/console-csp.json, the single source of
// truth for both delivery forms:
//   --header           print the response-header policy (CDN / static host)
//   --write [file]     stamp the <meta> fallback into index.html (default: the Web project's)
//   --check [file]     exit 1 when that file's <meta> policy differs from the source

import { readFileSync, writeFileSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const repoRoot = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const sourcePath = resolve(repoRoot, "config/console-csp.json");
export const defaultIndexPath = resolve(repoRoot, "src/Honua.Console.Web/wwwroot/index.html");
const metaPattern = /<meta http-equiv="Content-Security-Policy" content="([^"]*)" \/>/;

export function loadDirectives(path = sourcePath) {
  return JSON.parse(readFileSync(path, "utf8")).directives;
}

function render(directives) {
  return directives.map(({ name, sources }) => [name, ...sources].join(" ")).join("; ");
}

export function headerPolicy(directives = loadDirectives()) {
  return render(directives);
}

export function metaPolicy(directives = loadDirectives()) {
  return render(directives.filter((directive) => !directive.headerOnly));
}

export function metaTag(policy = metaPolicy()) {
  return `<meta http-equiv="Content-Security-Policy" content="${policy}" />`;
}

export function readMetaPolicy(html) {
  return html.match(metaPattern)?.[1] ?? null;
}

function stampMetaTag(html) {
  if (metaPattern.test(html)) return html.replace(metaPattern, metaTag());
  return html.replace(/(<meta charset="utf-8" \/>\n)(\s*)/, `$1$2${metaTag()}\n$2`);
}

function main([command, file = defaultIndexPath]) {
  if (command === "--header") {
    process.stdout.write(`${headerPolicy()}\n`);
    return 0;
  }
  if (command === "--write") {
    writeFileSync(file, stampMetaTag(readFileSync(file, "utf8")));
    return 0;
  }
  if (command === "--check") {
    if (readMetaPolicy(readFileSync(file, "utf8")) === metaPolicy()) return 0;
    process.stderr.write(`${file}: CSP <meta> differs from config/console-csp.json; run node scripts/console-csp.mjs --write\n`);
    return 1;
  }
  process.stderr.write("usage: console-csp.mjs --header | --write [index.html] | --check [index.html]\n");
  return 2;
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  process.exitCode = main(process.argv.slice(2));
}
