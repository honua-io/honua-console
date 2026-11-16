import { strict as assert } from "node:assert";
import { spawnSync } from "node:child_process";
import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import { test } from "node:test";
import { fileURLToPath } from "node:url";

// Console-owned UI wording and type names that the Honua vocabulary retires. The shared
// honua-server retired-terms checker (AGENTS.md "Vocabulary review") covers the cross-repo
// literal list on changed lines; this guard keeps the whole Console source free of the
// phrases below.
const RULES = [
  { id: "form-release-label", expression: />(?:Publish|Published)<|before publish(?:ing)?[ .<]/u, use: "Go live or Released", path: /StudioFormBuilderPage\.razor$/u },
  { id: "form-package-route", expression: /\/forms\/packages\b/u, use: "/forms" },
  { id: "content-item", expression: /\bcontent items?\b/iu, use: "the actual kind (map, app, workflow) or item" },
  { id: "serving-target", expression: /\bserving targets?\b/iu, use: "Layer" },
  { id: "hosted-layer", expression: /\bhosted layers?\b/iu, use: "Layer" },
  { id: "portal-item", expression: /\bportal items?\b/iu, use: "the actual kind" },
  { id: "web-map", expression: /\bweb ?maps?\b/iu, use: "Map; name the source format only in import readers" },
  {
    id: "studio-content-package",
    expression: /\b(?:map|app|dashboard|report|form|notebook|workflow|analysis|query) packages?\b/iu,
    use: "the kind itself (Map, App, Form, Workflow)",
  },
  { id: "package-family", expression: /\bpackage famil(?:y|ies)\b/iu, use: "content kind" },
  { id: "dataset-tree-type", expression: /\b(?:record|class|interface|enum|struct)\s+(?:ResourceTreeNode|PublicationTreeNode|ResourcePublicationsTreeBuilder|OperateResourceEditPreview|OperateResourcesView|OperateResourceIdentity|OperateTransitionWorkspace)\b/u, use: "DatasetTreeNode, DatasetEndpointNode, DatasetEndpointsTreeBuilder, OperateDatasetEditPreview, OperateDatasetsView" },
  { id: "envelope-type", expression: /\b(?:record|class|interface|enum|struct)\s+\w*(?:MapPackage|AppPackage|HostedMap|HonuaApiToken)/u, use: "Map, App, ApiKey" },
  {
    id: "workspace-tenancy",
    expression: /\b(?:your|this|current) workspace\b|\bworkspace (?:members|session|policy|permission|roles|access|user)\b/iu,
    use: "the server, environment, or the user's access",
  },
  { id: "domain-slot", expression: /\b(?:publication|layer|service|FeatureServer) slots?\b/iu, use: "Layer or endpoint" },
  { id: "tenant-label", expression: /Label="Tenant"|<dt>Tenant<\/dt>/u, use: "no tenant surface in a single-tenant UI" },
];

// Exact exceptions: source-format readers that must name the foreign format, and wire literals.
const ALLOWED = [
  { path: /^src\/Honua\.Console\.Shell\/(?:Pages\/ImportEsri|Services\/EsriContentImportParser|Services\/EsriImportSampleDocuments|Components\/EsriImportIntake|Models\/EsriContentImportModels|Validation\/EsriImportValidator)/u, rule: "web-map", reason: "reader for the source system's Web Map document" },
  { path: /^src\/Honua\.Console\.Web\/wwwroot\/js\/(?:agent-workspace|studio-embed)\.mjs$/u, rule: "workspace-tenancy", reason: "agent collaboration workspace session" },
  { path: /^src\/Honua\.Console\.Shell\/Services\/(?:ConsoleContentMapper|InMemoryConsoleCatalogClient)\.cs$/u, rule: "web-map", contains: /"(?:honua-webmap|webmap-doc)\/v/u, reason: "server format identifiers" },
];

const SCANNED = /^src\/.+\.(?:razor|cs|m?js)$/u;
const SKIPPED = /\/wwwroot\/vendor\//u;

const repoRoot = resolve(fileURLToPath(new URL("../..", import.meta.url)));

function trackedSources() {
  const listed = spawnSync("git", ["ls-files", "-z", "src"], { cwd: repoRoot, encoding: "utf8" });
  assert.equal(listed.status, 0, listed.stderr);
  return listed.stdout.split("\0").filter((path) => SCANNED.test(path) && !SKIPPED.test(path));
}

function isAllowed(path, rule, line) {
  return ALLOWED.some((entry) => entry.rule === rule && entry.path.test(path) && (!entry.contains || entry.contains.test(line)));
}

export function findRetiredVocabulary(path, text) {
  const findings = [];
  text.split("\n").forEach((line, index) => {
    for (const rule of RULES) {
      if ((!rule.path || rule.path.test(path)) && rule.expression.test(line) && !isAllowed(path, rule.id, line)) {
        findings.push(`${path}:${index + 1} [${rule.id}] use ${rule.use}: ${line.trim().slice(0, 120)}`);
      }
    }
  });
  return findings;
}

test("Console source uses the Honua vocabulary", () => {
  const findings = trackedSources().flatMap((path) =>
    findRetiredVocabulary(path, readFileSync(resolve(repoRoot, path), "utf8")));
  assert.deepEqual(findings, []);
});

test("retired phrases are caught and protocol or agent words are not", () => {
  const caught = [
    'Message="Reading your workspace content."',
    "<dt>Content item</dt>",
    "Create map package",
    "GET /api/v1/admin/forms/packages",
    "GET /api/v1/forms/packages/{formId}/offline-policy",
    "queryable through its FeatureServer slot.",
    'ContextRow Label="Tenant"',
    "public sealed record ConsoleMapPackage",
    "public sealed record ResourceTreeNode",
    "public sealed record PublicationTreeNode",
    "public sealed record OperateResourceEditPreview",
    "public sealed record OperateResourcesView",
    "public sealed record OperateTransitionWorkspace",
    "public static class OperateResourceIdentity",
    "public static class ResourcePublicationsTreeBuilder",
  ];
  for (const line of caught) {
    assert.notDeepEqual(findRetiredVocabulary("src/Honua.Console.Shell/Pages/X.razor", line), [], line);
  }
  const kept = [
    "Join an existing workspace",
    "Coordinate projection EPSG:3857",
    "honua.map-package.v1",
    "Available slots",
    "public sealed record DatasetTreeNode",
    "public sealed record DatasetEndpointNode",
    "public sealed record OperateDatasetEditPreview",
    "public sealed record OperateDatasetsView",
    "public sealed record OperateEnvironmentSnapshot",
    "public static class DatasetEndpointsTreeBuilder",
  ];
  for (const line of kept) {
    assert.deepEqual(findRetiredVocabulary("src/Honua.Console.Shell/Pages/X.razor", line), [], line);
  }
});

test("foreign-format readers may name the source Web Map", () => {
  assert.deepEqual(
    findRetiredVocabulary("src/Honua.Console.Shell/Pages/ImportEsriWebMapPage.razor", "<h1>Import source Web Map</h1>"),
    [],
  );
  assert.notDeepEqual(findRetiredVocabulary("src/Honua.Console.Shell/Pages/SharePage.razor", "web map tiles"), []);
});


test("Form release labels follow the workflow vocabulary", () => {
  const path = "src/Honua.Console.Shell/Pages/StudioFormBuilderPage.razor";
  for (const text of ["<button>Publish</button>", "<th>Published</th>", "Validate before publishing."]) {
    assert.notDeepEqual(findRetiredVocabulary(path, text), [], text);
  }
  for (const text of ["<button>Go live</button>", "<th>Released</th>", 'data-form-status="published"']) {
    assert.deepEqual(findRetiredVocabulary(path, text), [], text);
  }
});
