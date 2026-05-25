#!/usr/bin/env node
// Focused Studio workflow smoke for honua-console#40.
//
// The cross-surface parity smoke covers publish -> catalog -> Studio ->
// share/embed. This scenario covers the new workflow package chain:
//
//   Studio workflow draft -> dry-run -> version save -> publish ->
//   Operate job/event monitor
//
// The transport is still an in-memory stand-in, but every server-owned
// package, dry-run, publication, and job shape is tagged with a contract
// version so the adapter can be replaced by live server/SDK calls later.

import { fileURLToPath } from "node:url";
import { dirname, resolve } from "node:path";

import { assertSameOrigin } from "./adapters/console.mjs";
import { loadBuildArtifact } from "./adapters/devops.mjs";
import { buildEvidenceReport, formatTextSummary, writeEvidence } from "./evidence.mjs";
import { findContract } from "./contracts.mjs";
import { resolveOwningLayer } from "./owning-layers.mjs";

const HERE = dirname(fileURLToPath(import.meta.url));
const REPO_ROOT = resolve(HERE, "../..");

export const WORKFLOW_SCENARIO_ID = "studio-workflow-dry-run-publish-monitor";

function createWorkflowPackage() {
  return {
    draftId: "wf-draft-parcel-etl",
    packageType: "workflow.package",
    schemaVersion: "workflow.package/v1",
    title: "Parcel nightly normalizer",
    summary: "Normalize parcel attributes, route rejected records, and publish validated service output.",
    nodes: [
      { id: "source-parcels", category: "source", type: "honua.source.feature-layer", outputs: ["features"] },
      {
        id: "transform-filter",
        category: "transform",
        type: "honua.transform.filter",
        inputs: ["features"],
        outputs: ["matched", "rejected"],
      },
      {
        id: "transform-field-map",
        category: "transform",
        type: "honua.transform.field-map",
        inputs: ["features"],
        outputs: ["features", "error"],
      },
      { id: "sink-service", category: "sink", type: "honua.sink.feature-service", inputs: ["features"] },
      { id: "sink-failures", category: "sink", type: "honua.sink.failure-queue", inputs: ["error"] },
    ],
    edges: [
      { from: "source-parcels", to: "transform-filter", kind: "success", label: "features" },
      { from: "transform-filter", to: "transform-field-map", kind: "success", label: "matched" },
      { from: "transform-field-map", to: "sink-service", kind: "success", label: "validated" },
      { from: "transform-filter", to: "sink-failures", kind: "failure", label: "rejected" },
      { from: "transform-field-map", to: "sink-failures", kind: "failure", label: "transform errors" },
    ],
    parameters: [
      { name: "county", type: "string", required: true, defaultValue: "honolulu" },
      { name: "effective_date", type: "date", required: true, defaultValue: "2026-05-24" },
    ],
    schedule: { mode: "cron", cron: "0 6 * * *", timeZone: "Pacific/Honolulu" },
    workerProfile: { profileId: "standard-geospatial", runtime: "dotnet-gdal", cpu: 4, memoryGb: 16 },
    retryPolicy: { maxAttempts: 3, backoffSeconds: 60, failureMode: "route-failure-edges" },
    publicationIntent: {
      mode: "process-endpoint",
      visibility: "org",
      routeSlug: "parcel-nightly-normalizer",
      exposeInvocationEndpoint: true,
      requiresApproval: true,
    },
    outputSchemas: [
      {
        name: "validated_parcels",
        sinkNodeId: "sink-service",
        fields: [
          { name: "parcel_id", type: "string", nullable: false },
          { name: "owner_name", type: "string", nullable: true },
          { name: "zoning_code", type: "string", nullable: true },
          { name: "geometry", type: "geometry", nullable: false },
        ],
      },
      {
        name: "rejected_parcels",
        sinkNodeId: "sink-failures",
        fields: [
          { name: "source_row_id", type: "string", nullable: false },
          { name: "failure_reason", type: "string", nullable: false },
          { name: "payload", type: "json", nullable: false },
        ],
      },
    ],
  };
}

function validateWorkflowPackage(pkg) {
  const categories = new Set(pkg.nodes.map((node) => node.category));
  if (!categories.has("source")) throw new Error("workflow.package must include a source node");
  if (!categories.has("transform")) throw new Error("workflow.package must include a transform node");
  if (!categories.has("sink")) throw new Error("workflow.package must include a sink node");
  if (!pkg.edges.some((edge) => edge.kind === "failure")) {
    throw new Error("workflow.package must expose failure edges before publish");
  }
  if (!Array.isArray(pkg.outputSchemas) || pkg.outputSchemas.length === 0) {
    throw new Error("workflow.package must expose output schemas before publish");
  }
  for (const parameter of pkg.parameters) {
    if (!parameter.name || !["string", "date", "number", "boolean", "geometry"].includes(parameter.type)) {
      throw new Error(`invalid workflow parameter contract for ${parameter.name || "(unnamed)"}`);
    }
  }
}

export const WORKFLOW_SCENARIO_STEPS = [
  {
    id: "devops/build-artifact",
    owningLayer: "devops",
    description: "Verify the single deployable Console artifact before exercising Studio workflow routes.",
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
  {
    id: "console/studio-workflow-draft",
    owningLayer: "console",
    description:
      "Studio opens the workflow editor and materializes a workflow.package draft with graph, parameters, schedule, failure edges, output schemas, and publication intent.",
    async run(ctx) {
      const draftUrl = `${ctx.originUrl}/studio/workflows/new`;
      assertSameOrigin(ctx.originUrl, { draft: draftUrl });
      const pkg = createWorkflowPackage();
      validateWorkflowPackage(pkg);
      ctx.workflowPackage = pkg;
      ctx.itemIds.workflowDraftId = pkg.draftId;
      return {
        evidence: {
          draftUrl,
          packageType: pkg.packageType,
          nodeCategories: [...new Set(pkg.nodes.map((node) => node.category))],
          failureEdges: pkg.edges.filter((edge) => edge.kind === "failure").map((edge) => edge.label),
          parameters: pkg.parameters.map((parameter) => ({
            name: parameter.name,
            type: parameter.type,
            required: parameter.required,
          })),
          schedule: pkg.schedule,
          workerProfile: pkg.workerProfile,
          retryPolicy: pkg.retryPolicy,
          publicationIntent: pkg.publicationIntent,
          outputSchemas: pkg.outputSchemas.map((schema) => ({
            name: schema.name,
            fields: schema.fields.map((field) => field.name),
          })),
        },
        contracts: [findContract("workflow-package")],
      };
    },
  },
  {
    id: "server/workflow-dry-run",
    owningLayer: "server",
    description:
      "Server validates the workflow package and returns dry-run sample rows, logs, artifacts, and output schemas.",
    async run(ctx) {
      validateWorkflowPackage(ctx.workflowPackage);
      const jobId = "job-dryrun-0001";
      ctx.itemIds.workflowDryRunJobId = jobId;
      ctx.workflowDryRun = {
        jobId,
        kind: "workflow_dry_run",
        status: "succeeded",
        sampleRows: 48,
        logs: [
          "Loaded 5 workflow nodes from workflow.package.",
          "Validated 2 invocation parameters against sample data.",
          "Materialized output schema and sample artifacts without publishing.",
        ],
        artifacts: [
          "wf-draft-parcel-etl/dry-run/sample-output.geojson",
          "wf-draft-parcel-etl/dry-run/output-schema.json",
        ],
        outputSchemas: ctx.workflowPackage.outputSchemas,
      };
      return {
        evidence: {
          jobId,
          status: ctx.workflowDryRun.status,
          sampleRows: ctx.workflowDryRun.sampleRows,
          artifactCount: ctx.workflowDryRun.artifacts.length,
          outputSchemas: ctx.workflowDryRun.outputSchemas.map((schema) => schema.name),
        },
        contracts: [findContract("workflow-dry-run")],
      };
    },
  },
  {
    id: "server/workflow-version-save",
    owningLayer: "server",
    description:
      "Server persists the workflow.package as a versioned workflow content item, preserving validation evidence.",
    async run(ctx) {
      const contentItemId = "workflow-parcel-nightly-normalizer";
      const versionId = `${contentItemId}:v2`;
      ctx.itemIds.workflowContentItemId = contentItemId;
      ctx.itemIds.workflowVersionId = versionId;
      ctx.workflowVersion = {
        contentItemId,
        versionId,
        contentItemType: "workflow",
        packageType: ctx.workflowPackage.packageType,
        validationStatus: "ready",
      };
      return {
        evidence: ctx.workflowVersion,
        contracts: [findContract("workflow-package")],
      };
    },
  },
  {
    id: "server/workflow-publish",
    owningLayer: "server",
    description:
      "Server creates the workflow publication, queues the job runner, and exposes an invocation endpoint for eligible process workflows.",
    async run(ctx) {
      const parameterValidation = ctx.workflowPackage.parameters.map((parameter) => ({
        name: parameter.name,
        type: parameter.type,
        required: parameter.required,
        valid: true,
      }));
      const publicationId = "pub-parcel-nightly-normalizer-002";
      const jobId = "job-publish-0002";
      const invocationEndpoint = `${ctx.originUrl}/api/workspaces/workspace-honua-demo/workflows/parcel-nightly-normalizer/invoke`;
      assertSameOrigin(ctx.originUrl, { invocationEndpoint });
      ctx.itemIds.workflowPublicationId = publicationId;
      ctx.itemIds.workflowPublishJobId = jobId;
      ctx.workflowPublication = {
        publicationId,
        contentItemId: ctx.workflowVersion.contentItemId,
        versionId: ctx.workflowVersion.versionId,
        mode: ctx.workflowPackage.publicationIntent.mode,
        status: "queued",
        jobId,
        invocationEndpoint,
        parameterValidation,
      };
      return {
        evidence: ctx.workflowPublication,
        contracts: [findContract("workflow-publication")],
      };
    },
  },
  {
    id: "console/operate-job-monitor",
    owningLayer: "console",
    description:
      "Console links the dry-run and publication jobs to Operate job and event evidence on the same origin.",
    async run(ctx) {
      const urls = {
        workflowEditor: `${ctx.originUrl}/studio/workflows/${ctx.workflowPackage.draftId}`,
        dryRunJob: `${ctx.originUrl}/operate/jobs/${ctx.workflowDryRun.jobId}`,
        dryRunEvents: `${ctx.originUrl}/operate/events?jobId=${ctx.workflowDryRun.jobId}`,
        publishJob: `${ctx.originUrl}/operate/jobs/${ctx.workflowPublication.jobId}`,
        publishEvents: `${ctx.originUrl}/operate/events?jobId=${ctx.workflowPublication.jobId}`,
        invocationEndpoint: ctx.workflowPublication.invocationEndpoint,
      };
      assertSameOrigin(ctx.originUrl, urls);
      ctx.urls = urls;
      return {
        evidence: {
          dryRunJobUrl: urls.dryRunJob,
          publishJobUrl: urls.publishJob,
          publishEventsUrl: urls.publishEvents,
          invocationEndpoint: urls.invocationEndpoint,
        },
      };
    },
  },
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

export async function runWorkflowSmoke({
  originUrl,
  repoRoot = REPO_ROOT,
  steps = WORKFLOW_SCENARIO_STEPS,
  fetchImpl,
} = {}) {
  if (!originUrl) throw new Error("runWorkflowSmoke requires originUrl");
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
    scenarioId: WORKFLOW_SCENARIO_ID,
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
    process.stdout.write(
      `Usage: node smoke/parity/workflow.mjs [--origin URL] [--output path.json] [--quiet]\n`,
    );
    return;
  }
  const { report } = await runWorkflowSmoke({ originUrl: args.originUrl });
  const outputPath = args.outputPath
    ? resolve(process.cwd(), args.outputPath)
    : resolve(REPO_ROOT, "smoke-evidence/studio-workflow.json");
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
    process.stderr.write(`workflow smoke crashed: ${err instanceof Error ? err.stack ?? err.message : String(err)}\n`);
    process.exitCode = 2;
  });
}
