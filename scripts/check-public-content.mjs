import { readFileSync } from "node:fs";
import { extname, resolve } from "node:path";
import { spawnSync } from "node:child_process";
import { fileURLToPath } from "node:url";

const POLICY_FILES = new Set([
  "scripts/check-public-content.mjs",
  "scripts/__tests__/public-content.test.mjs",
]);

const TEXT_EXTENSIONS = new Set([
  ".aprx", ".atbx", ".cs", ".csproj", ".css", ".html", ".js", ".json", ".jsx", ".md", ".mjs",
  ".props", ".py", ".razor", ".sh", ".slnx", ".targets", ".toml", ".ts", ".tsx",
  ".txt", ".xml", ".yaml", ".yml",
]);

const desktopClient = String.raw`(?:arcgis[ -]?pro)`;
const detail = String.raw`(?:screen(?:shot|\s+capture)|install(?:ation|er|ing)?|activat(?:e|ion)|click(?:ed|ing)?\s+(?:the|on)|open(?:ed|ing)?\s+(?:the\s+)?(?:menu|dialog|ribbon|toolbox)|licen[cs](?:e|ed|ing)?\s+(?:key|server|manager|seat|activation|configuration|checkout)|(?:key|server|manager|seat|activation|configuration|checkout)\s+licen[cs](?:e|ed|ing)?)`;
const testClaim = String.raw`(?:desktop\s+(?:test|replay)|client\s+(?:test|replay)|certif(?:y|ied|ication)|pass\s*\/\s*fail|pass(?:ed)?\s+(?:in|on|with)|fail(?:ed)?\s+(?:in|on|with)|test\s+(?:matrix|tally|result))`;

const RULES = [
  {
    id: "desktop-client-procedure",
    expression: new RegExp(
      String.raw`(?:${desktopClient}[\s\S]{0,160}${detail}|${detail}[\s\S]{0,160}${desktopClient})`,
      "giu",
    ),
    message: "desktop-client UI, installation, or licensing procedures are private certification detail",
  },
  {
    id: "desktop-client-result",
    expression: new RegExp(
      String.raw`(?:${desktopClient}[\s\S]{0,160}${testClaim}|${testClaim}[\s\S]{0,160}${desktopClient})`,
      "giu",
    ),
    message: "desktop-client test execution and result detail must be cited from private evidence",
  },
];

function lineNumber(text, offset) {
  return text.slice(0, offset).split("\n").length;
}

export function findPublicContentViolations(path, text) {
  if (POLICY_FILES.has(path)) {
    return [];
  }

  const artifactViolation = /\.(?:aprx|atbx)$/iu.test(path)
    ? [{
        path,
        line: 1,
        rule: "desktop-project-artifact",
        message: "desktop-client project/toolbox artifacts are private certification detail",
      }]
    : [];

  return artifactViolation.concat(RULES.flatMap((rule) => [...text.matchAll(rule.expression)].map((match) => ({
    path,
    line: lineNumber(text, match.index ?? 0),
    rule: rule.id,
    message: rule.message,
  }))));
}

export function trackedTextFiles(repoRoot) {
  const result = spawnSync("git", ["ls-files", "-z"], {
    cwd: repoRoot,
    encoding: "utf8",
  });
  if (result.status !== 0) {
    throw new Error(result.stderr.trim() || "git ls-files failed");
  }

  return result.stdout
    .split("\0")
    .filter(Boolean)
    .filter((path) => TEXT_EXTENSIONS.has(extname(path).toLowerCase()) || !extname(path));
}

export function scanPublicRepository(repoRoot) {
  return trackedTextFiles(repoRoot).flatMap((path) => {
    const text = readFileSync(resolve(repoRoot, path), "utf8");
    return findPublicContentViolations(path, text);
  });
}

const invokedPath = process.argv[1] ? resolve(process.argv[1]) : undefined;
if (invokedPath === fileURLToPath(import.meta.url)) {
  const repoRoot = fileURLToPath(new URL("..", import.meta.url));
  const violations = scanPublicRepository(repoRoot);
  for (const violation of violations) {
    console.error(`${violation.path}:${violation.line}: ${violation.message} [${violation.rule}]`);
  }
  if (violations.length > 0) {
    process.exitCode = 1;
  }
}
