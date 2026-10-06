import { readFileSync } from "node:fs";
import { extname, resolve } from "node:path";
import { spawnSync } from "node:child_process";
import { fileURLToPath } from "node:url";

const POLICY_FILES = new Set([
  "scripts/check-public-content.mjs",
  "scripts/__tests__/public-content.test.mjs",
]);

// Maintained documentation globs from .github/workflows/ci.yml, plus source.
const TEXT_EXTENSIONS = new Set([
  ".aprx", ".atbx", ".cs", ".csproj", ".css", ".html", ".js", ".json", ".jsx", ".md", ".mdx", ".mjs",
  ".props", ".py", ".razor", ".rst", ".sh", ".slnx", ".targets", ".toml", ".ts", ".tsx",
  ".txt", ".xml", ".yaml", ".yml",
]);

const desktopClient = String.raw`(?:arcgis[ -]?pro)`;
// A gap stays inside one sentence so an earlier setup step cannot attach to a
// later compatibility statement ("Install Honua Console. ArcGIS Pro can consume…").
const sameSentence = String.raw`[^.!?\n]{0,160}`;
// Generic setup verbs count only when they name the desktop client. "Install"
// of some other product in the same sentence is not a desktop procedure.
const setupVerb = String.raw`(?:(?:re)?install(?:ed|ation|er|ing)?|activat(?:e|ed|ion|ing)?)`;
const attachedSetup = String.raw`(?:\b${setupVerb}\s+(?:(?:of|the|an?)\s+)?${desktopClient}\b|${desktopClient}(?:'s)?\s+${setupVerb}\b)`;
const uiDetail = String.raw`(?:screen(?:shot|\s+capture)|click(?:ed|ing)?\s+(?:the|on)|open(?:ed|ing)?\s+(?:the\s+)?(?:menu|dialog|ribbon|toolbox)|licen[cs](?:e|ed|ing)?\s+(?:key|server|manager|seat|activation|configuration|checkout)|(?:key|server|manager|seat|activation|configuration|checkout)\s+licen[cs](?:e|ed|ing)?)`;
const resultWord = String.raw`(?:passed|passing|failed|failing|failures?)`;
const countedResult = String.raw`(?:pass(?:ed|ing)?|fail(?:ed|ing|ure|ures)?)`;
const testNoun = String.raw`(?:tests?|replays?)`;
// Ordinary tallies ("tests: 12 passed, 1 failed") count. "passed"/"failed" do
// not have to be followed by in/on/with, and the noun may be plural.
const testClaim = String.raw`(?:desktop\s+${testNoun}|client\s+${testNoun}|certif(?:y|ied|ication)|pass\s*\/\s*fail|(?:pass(?:ed)?|fail(?:ed)?)\s+(?:in|on|with)\b|\b(?:\d+|all|none)\s+${countedResult}\b|\b${resultWord}\b|${testNoun}\s*:\s*\d+|${testNoun}\s+(?:matrix|tally|results?))`;

const RULES = [
  {
    id: "desktop-client-procedure",
    expression: new RegExp(
      String.raw`(?:${attachedSetup}|${desktopClient}${sameSentence}${uiDetail}|${uiDetail}${sameSentence}${desktopClient})`,
      "giu",
    ),
    message: "desktop-client UI, installation, or licensing procedures are private certification detail",
  },
  {
    id: "desktop-client-result",
    expression: new RegExp(
      String.raw`(?:${desktopClient}${sameSentence}${testClaim}|${testClaim}${sameSentence}${desktopClient})`,
      "giu",
    ),
    message: "desktop-client test execution and result detail must be cited from private evidence",
  },
];

function lineNumber(text, offset) {
  return text.slice(0, offset).split("\n").length;
}

export function isPublicTextPath(path) {
  const extension = extname(path).toLowerCase();
  return TEXT_EXTENSIONS.has(extension) || extension.length === 0;
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
    .filter((path) => isPublicTextPath(path));
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
