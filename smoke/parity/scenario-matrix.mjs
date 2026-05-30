#!/usr/bin/env node
// Console read-context scenario matrix for honua-console#34 (Catalog/Share).
//
// The cross-surface parity smoke (scenario.mjs) walks one happy path:
// publish -> catalog -> Studio -> share/embed. This matrix complements it by
// fanning the *read* side of the Catalog/Share slice across the auth and
// state axes the single happy path does not exercise:
//
//   - auth mode:    anonymous public read, anonymous public-link read
//                   (matching token and wrong/absent token), authenticated.
//   - RBAC-denied:  org/private items requested without a session, and a
//                   public-link item requested without the link token.
//   - empty state:  a catalog query that matches nothing, and an anonymous
//                   browse of a workspace whose only items are protected.
//
// This mirrors the .NET InMemoryConsoleCatalogClient authorization contract
// (src/Honua.Console.Shell/Services/InMemoryConsoleCatalogClient.cs) so the
// Console-side read-state behavior is regression-covered in the same harness
// that gates the publish chain. Like the rest of smoke/parity/, the transport
// is an in-memory stand-in; every read decision is tagged with its owning
// layer (console) and the share-access contract it exercises, so a future
// swap to the live honua-server/SDK read endpoint stays attributable.
//
// The matrix is server-independent: it evaluates the same-origin read-context
// rules Console enforces in front of (eventually) honua-server, not server
// behavior itself.

import { fileURLToPath } from "node:url";
import { dirname, resolve } from "node:path";

import { loadBuildArtifact } from "./adapters/devops.mjs";
import { buildEvidenceReport, formatTextSummary, writeEvidence } from "./evidence.mjs";
import { findContract } from "./contracts.mjs";
import { resolveOwningLayer } from "./owning-layers.mjs";

const HERE = dirname(fileURLToPath(import.meta.url));
const REPO_ROOT = resolve(HERE, "../..");

export const SCENARIO_MATRIX_ID = "console-catalog-share-read-context-matrix";

// Read-state vocabulary, kept aligned with the .NET CatalogReadStatus enum so
// evidence terms do not drift between the JS smoke and the Razor surface.
export const READ_STATUS = Object.freeze({
  allowed: "allowed",
  unavailable: "unavailable",
  missing: "missing",
});

// Share tiers, aligned with content-item/v1.1.0 Access.sharing + the .NET
// CatalogSharingTiers constants.
const SHARING = Object.freeze({
  public: "public",
  publicLink: "public-link",
  org: "org",
  private: "private",
});

// Catalog fixture for the read matrix. Mirrors the seed items in the .NET
// InMemoryConsoleCatalogClient (coastal-flood public open-data service,
// utilities public-link layer, capital-projects org dashboard) so the two
// implementations stay contract-compatible.
export function createCatalogFixture() {
  return [
    {
      id: "svc-coastal-flood",
      slug: "coastal-flood-service",
      type: "service",
      title: "Coastal Flood Service",
      tags: ["flood", "open-data", "coastal"],
      access: { sharing: SHARING.public, openData: true, embeddable: true, publicLinkToken: "" },
    },
    {
      id: "lyr-utilities",
      slug: "utilities-layer",
      type: "layer",
      title: "Utilities Critical Layer",
      tags: ["utilities", "response"],
      access: { sharing: SHARING.publicLink, openData: false, embeddable: false, publicLinkToken: "pl-utilities" },
    },
    {
      id: "dash-capital-projects",
      slug: "capital-projects-dashboard",
      type: "dashboard",
      title: "Capital Projects Dashboard",
      tags: ["capital", "projects"],
      access: { sharing: SHARING.org, openData: false, embeddable: false, publicLinkToken: "" },
    },
  ];
}

// A read context mirroring CatalogReadContext: anonymous reads carry an
// optional public-link token; authenticated reads always pass.
export function anonymous(token = "") {
  return { anonymous: true, publicLinkToken: (token ?? "").trim() };
}

export function authenticated() {
  return { anonymous: false, publicLinkToken: "" };
}

// Visibility predicate mirroring InMemoryConsoleCatalogClient.IsVisibleToContext.
function isVisibleToContext(item, context) {
  if (!context.anonymous) return true;
  if (item.access.sharing === SHARING.public) return true;
  return (
    item.access.sharing === SHARING.publicLink &&
    !!context.publicLinkToken &&
    context.publicLinkToken === item.access.publicLinkToken
  );
}

// Read-state resolver mirroring InMemoryConsoleCatalogClient.GetCatalogItemAsync:
// unknown id -> missing; authorized -> allowed; otherwise -> unavailable
// (RBAC-denied / wrong-or-absent public link).
export function resolveItemRead(items, idOrSlug, context) {
  const item = items.find((i) => i.id === idOrSlug || i.slug === idOrSlug);
  if (!item) return { status: READ_STATUS.missing };
  if (!isVisibleToContext(item, context)) return { status: READ_STATUS.unavailable };
  return { status: READ_STATUS.allowed, item, anonymousRead: context.anonymous };
}

// Search resolver mirroring InMemoryConsoleCatalogClient.SearchAsync visibility
// filter + optional query/tag filter.
export function resolveSearch(items, context, { query, tag } = {}) {
  let visible = items.filter((i) => isVisibleToContext(i, context));
  if (query) {
    const q = query.toLowerCase();
    visible = visible.filter(
      (i) => i.title.toLowerCase().includes(q) || i.tags.some((t) => t.toLowerCase().includes(q)),
    );
  }
  if (tag) {
    visible = visible.filter((i) => i.tags.some((t) => t.toLowerCase() === tag.toLowerCase()));
  }
  return visible;
}

// The matrix cases. Each declares the read it performs and the read-state it
// must resolve to; the step runner asserts and records evidence per case.
export const READ_MATRIX_CASES = [
  {
    id: "anon-public-read-allowed",
    axis: "auth/anonymous-public",
    description: "Anonymous read of a public open-data service is allowed.",
    run: (items) => {
      const result = resolveItemRead(items, "coastal-flood-service", anonymous());
      assertStatus(result, READ_STATUS.allowed);
      assertTrue(result.anonymousRead, "public anonymous read must be flagged anonymousRead");
      return { target: "svc-coastal-flood", status: result.status, anonymousRead: result.anonymousRead };
    },
  },
  {
    id: "anon-public-link-matching-token-allowed",
    axis: "auth/public-link",
    description: "Anonymous public-link read with the matching token is allowed.",
    run: (items) => {
      const result = resolveItemRead(items, "utilities-layer", anonymous("pl-utilities"));
      assertStatus(result, READ_STATUS.allowed);
      return { target: "lyr-utilities", status: result.status, anonymousRead: result.anonymousRead };
    },
  },
  {
    id: "anon-public-link-wrong-token-denied",
    axis: "rbac/public-link",
    description: "Anonymous public-link read with a wrong token is denied (unavailable, not missing).",
    run: (items) => {
      const result = resolveItemRead(items, "utilities-layer", anonymous("not-the-token"));
      assertStatus(result, READ_STATUS.unavailable);
      return { target: "lyr-utilities", status: result.status };
    },
  },
  {
    id: "anon-public-link-absent-token-denied",
    axis: "rbac/public-link",
    description: "Anonymous public-link read with no token is denied (unavailable).",
    run: (items) => {
      const result = resolveItemRead(items, "utilities-layer", anonymous());
      assertStatus(result, READ_STATUS.unavailable);
      return { target: "lyr-utilities", status: result.status };
    },
  },
  {
    id: "anon-org-item-rbac-denied",
    axis: "rbac/anonymous-org",
    description: "Anonymous read of an org-tier dashboard is RBAC-denied (unavailable).",
    run: (items) => {
      const result = resolveItemRead(items, "capital-projects-dashboard", anonymous());
      assertStatus(result, READ_STATUS.unavailable);
      return { target: "dash-capital-projects", status: result.status };
    },
  },
  {
    id: "authenticated-org-item-allowed",
    axis: "auth/authenticated",
    description: "Authenticated read of the same org-tier dashboard is allowed.",
    run: (items) => {
      const result = resolveItemRead(items, "capital-projects-dashboard", authenticated());
      assertStatus(result, READ_STATUS.allowed);
      assertTrue(!result.anonymousRead, "authenticated read must not be flagged anonymousRead");
      return { target: "dash-capital-projects", status: result.status, anonymousRead: !!result.anonymousRead };
    },
  },
  {
    id: "unknown-item-missing",
    axis: "rbac/unknown",
    description: "Reading an unknown id resolves to missing, not unavailable (no existence oracle leak).",
    run: (items) => {
      const result = resolveItemRead(items, "does-not-exist", anonymous());
      assertStatus(result, READ_STATUS.missing);
      return { target: "does-not-exist", status: result.status };
    },
  },
  {
    id: "anon-browse-hides-protected-items",
    axis: "empty/anonymous-filtered",
    description:
      "Anonymous catalog browse lists only the public item; org and untokened public-link items are filtered out.",
    run: (items) => {
      const visible = resolveSearch(items, anonymous());
      const visibleIds = visible.map((i) => i.id).sort();
      assertDeepEqual(visibleIds, ["svc-coastal-flood"], "anonymous browse must hide protected items");
      return { visibleIds, hiddenCount: items.length - visible.length };
    },
  },
  {
    id: "query-matches-nothing-empty-state",
    axis: "empty/no-match",
    description: "A catalog query that matches no visible item returns an empty result (empty-state surface).",
    run: (items) => {
      const visible = resolveSearch(items, authenticated(), { query: "no-such-content-anywhere" });
      assertTrue(visible.length === 0, "query matching nothing must return an empty result set");
      return { matchCount: visible.length, isEmpty: true };
    },
  },
  {
    id: "anon-tag-filter-to-empty",
    axis: "empty/anonymous-tag-filter",
    description:
      "Anonymous browse filtered to a tag carried only by a protected item returns empty, not the protected item.",
    run: (items) => {
      // 'utilities' is only on the public-link utilities layer; an anonymous,
      // untokened browse must not surface it even via tag filter.
      const visible = resolveSearch(items, anonymous(), { tag: "utilities" });
      assertTrue(visible.length === 0, "tag filter must not bypass anonymous visibility rules");
      return { matchCount: visible.length, isEmpty: true };
    },
  },
];

function assertStatus(result, expected) {
  if (result.status !== expected) {
    throw new Error(`read-state mismatch: expected "${expected}", got "${result.status}"`);
  }
}

function assertTrue(condition, message) {
  if (!condition) throw new Error(message);
}

function assertDeepEqual(actual, expected, message) {
  if (JSON.stringify(actual) !== JSON.stringify(expected)) {
    throw new Error(`${message}: expected ${JSON.stringify(expected)}, got ${JSON.stringify(actual)}`);
  }
}

export const SCENARIO_MATRIX_STEPS = [
  {
    id: "devops/build-artifact",
    owningLayer: "devops",
    description: "Verify the single deployable Console artifact before exercising the read matrix.",
    async run(ctx) {
      const { metadata, source, path, contract } = await loadBuildArtifact({
        repoRoot: ctx.repoRoot,
        originUrl: ctx.originUrl,
        fetchImpl: ctx.fetchImpl,
      });
      ctx.buildArtifact = { metadata, source, path };
      return {
        evidence: { source, path, version: metadata.version, areas: metadata.areas },
        contracts: [contract],
      };
    },
  },
  ...READ_MATRIX_CASES.map((matrixCase) => ({
    id: `console/read-${matrixCase.id}`,
    owningLayer: "console",
    description: matrixCase.description,
    async run(ctx) {
      ctx.catalog = ctx.catalog ?? createCatalogFixture();
      const evidence = matrixCase.run(ctx.catalog);
      ctx.matrixResults = ctx.matrixResults ?? {};
      ctx.matrixResults[matrixCase.id] = { axis: matrixCase.axis, ...evidence };
      return {
        evidence: { case: matrixCase.id, axis: matrixCase.axis, ...evidence },
        contracts: [findContract("share-access")],
      };
    },
  })),
];

function parseArgs(argv) {
  const args = { originUrl: "http://127.0.0.1:4174", outputPath: null, quiet: false };
  for (let i = 0; i < argv.length; i += 1) {
    const a = argv[i];
    if (a === "--origin" || a === "-o") {
      args.originUrl = argv[++i];
    } else if (a === "--output") {
      args.outputPath = argv[++i];
    } else if (a === "--quiet") {
      args.quiet = true;
    } else if (a === "--help" || a === "-h") {
      args.help = true;
    } else {
      throw new Error(`Unknown argument: ${a}`);
    }
  }
  return args;
}

export async function runScenarioMatrix({
  originUrl,
  repoRoot = REPO_ROOT,
  steps = SCENARIO_MATRIX_STEPS,
  fetchImpl,
} = {}) {
  if (!originUrl) throw new Error("runScenarioMatrix requires originUrl");
  const normalizedOriginUrl = new URL(originUrl).origin;
  const ranAt = new Date().toISOString();
  const ctx = { repoRoot, originUrl: normalizedOriginUrl, itemIds: {}, fetchImpl };
  const executed = [];
  let failure = null;

  for (const step of steps) {
    resolveOwningLayer(step.owningLayer);
    const startedAt = performance.now();
    let outcome = { evidence: null, contracts: [] };
    let status = "ok";
    let error = null;
    try {
      outcome = (await step.run(ctx)) ?? outcome;
    } catch (e) {
      status = "failed";
      error = e instanceof Error ? e.message : String(e);
      failure = { stepId: step.id, owningLayer: step.owningLayer, message: error };
    }
    executed.push({
      id: step.id,
      owningLayer: step.owningLayer,
      description: step.description,
      status,
      durationMs: Math.round(performance.now() - startedAt),
      evidence: outcome.evidence ?? null,
      contracts: outcome.contracts ?? [],
      error,
    });
    if (failure) break;
  }

  const ran = executed.map((step) => step.id);
  for (const step of steps) {
    if (!ran.includes(step.id)) {
      executed.push({
        id: step.id,
        owningLayer: step.owningLayer,
        description: step.description,
        status: "skipped",
        durationMs: 0,
        evidence: null,
        contracts: [],
        error: null,
      });
    }
  }

  const report = buildEvidenceReport({
    scenarioId: SCENARIO_MATRIX_ID,
    ranAt,
    ctx,
    steps: executed,
    result: failure ? "failed" : "ok",
    failure,
  });
  return { report, ctx };
}

async function main(argv) {
  let args;
  try {
    args = parseArgs(argv);
  } catch (e) {
    process.stderr.write(`${e.message}\n`);
    process.exitCode = 2;
    return;
  }
  if (args.help) {
    process.stdout.write(`Usage: node smoke/parity/scenario-matrix.mjs [--origin URL] [--output path.json] [--quiet]\n`);
    return;
  }
  const { report } = await runScenarioMatrix({ originUrl: args.originUrl });
  const outputPath = args.outputPath
    ? resolve(process.cwd(), args.outputPath)
    : resolve(REPO_ROOT, "smoke-evidence/catalog-share-read-matrix.json");
  await writeEvidence({ outputPath, report });
  if (!args.quiet) {
    process.stdout.write(`${formatTextSummary(report)}\n`);
    process.stdout.write(`\nevidence written to ${outputPath}\n`);
  }
  if (report.result === "failed") process.exitCode = 1;
}

const isDirectInvocation = fileURLToPath(import.meta.url) === resolve(process.argv[1] ?? "");
if (isDirectInvocation) {
  main(process.argv.slice(2)).catch((err) => {
    process.stderr.write(
      `scenario matrix crashed: ${err instanceof Error ? err.stack ?? err.message : String(err)}\n`,
    );
    process.exitCode = 2;
  });
}
