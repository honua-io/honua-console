import { strict as assert } from "node:assert";
import { describe, test } from "node:test";

import { OWNING_LAYER_IDS } from "../owning-layers.mjs";
import { SCENARIO_STEPS } from "../scenario.mjs";
import { runParitySmoke } from "../run.mjs";

const ORIGIN = "http://127.0.0.1:4174";
const REMOTE_ORIGIN = "https://console.smoke.honua.example";
const REMOTE_BUILD_ARTIFACT = {
  name: "honua-console",
  version: "2026.05.23-smoke",
  commit: "0123456789abcdef0123456789abcdef01234567",
  shortCommit: "0123456",
  ref: "refs/heads/feature/honua-console-9",
  builtAt: "2026-05-23T00:00:00.000Z",
  legacy: { portal: "honua-portal@fixture", admin: "honua-server-admin@fixture" },
  areas: ["studio", "catalog", "share", "operate"],
};

describe("parity scenario", () => {
  test("happy path completes every step and records owning-layer-tagged evidence", async () => {
    const { report, ctx } = await runParitySmoke({ originUrl: ORIGIN });
    assert.equal(report.result, "ok", `parity smoke failed: ${report.failure?.message ?? "unknown"}`);
    assert.equal(report.failure, null);

    // Each scenario step ran in order and produced an `ok` status row.
    assert.equal(report.steps.length, SCENARIO_STEPS.length);
    for (const [i, step] of report.steps.entries()) {
      assert.equal(step.id, SCENARIO_STEPS[i].id);
      assert.equal(step.status, "ok", `${step.id} ended in ${step.status}: ${step.error}`);
      assert.ok(OWNING_LAYER_IDS.includes(step.owningLayer));
    }

    // Items section captures the IDs the AC requires.
    assert.ok(report.items.serviceItemId);
    assert.ok(report.items.savedMapId);
    assert.ok(report.items.generatedAppId);
    assert.ok(report.items.embedToken);
    assert.equal(report.items.shareTier, "org");

    // URLs section captures every Console surface in the chain, all
    // same-origin with the deployable artifact. The embed URL keeps the
    // bearer token in the fragment per embed-token/v1.
    assert.ok(report.urls);
    const expectedOrigin = new URL(ORIGIN).origin;
    for (const value of Object.values(report.urls)) {
      assert.equal(new URL(value).origin, expectedOrigin);
    }
    const embedUrl = new URL(report.urls.embed);
    assert.ok(embedUrl.hash.startsWith("#embedToken="), "embed URL must carry token in fragment");
    assert.ok(!embedUrl.searchParams.has("token"), "embed URL must not carry token in query string");
    assert.ok(!embedUrl.searchParams.has("embedToken"), "embed URL must not carry embedToken in query string");

    // Contract-version table includes the seven Console-parity contracts.
    const contractNames = report.contractVersions.map((c) => c.name).sort();
    assert.deepEqual(contractNames, [
      "build-artifact",
      "content-item",
      "embed-token",
      "generated-app-lifecycle",
      "publish-handoff",
      "share-access",
      "webmap-doc",
    ]);

    // Build artifact metadata captured (fixture in this test environment).
    assert.equal(report.buildArtifact.name, "honua-console");
    assert.equal(report.buildArtifact.source, "fixture");
    assert.deepEqual(report.buildArtifact.areas, ["studio", "catalog", "share", "operate"]);

    // The runner records the origin used so a CI reviewer can correlate the
    // evidence with the artifact it ran against.
    assert.equal(report.originUrl, ORIGIN);

    // Sanity check the ctx is consistent with the report.
    assert.equal(ctx.itemIds.serviceItemId, report.items.serviceItemId);
  });

  test("deployed-origin build artifact is fetched from the origin", async () => {
    let requestedUrl = null;
    const fetchImpl = async (url) => {
      requestedUrl = url;
      return {
        ok: true,
        status: 200,
        async json() {
          return REMOTE_BUILD_ARTIFACT;
        },
      };
    };

    const { report } = await runParitySmoke({ originUrl: REMOTE_ORIGIN, fetchImpl });
    assert.equal(report.result, "ok", `parity smoke failed: ${report.failure?.message ?? "unknown"}`);
    assert.equal(requestedUrl, `${REMOTE_ORIGIN}/version.json`);
    assert.equal(report.buildArtifact.source, "origin");
    assert.equal(report.buildArtifact.path, `${REMOTE_ORIGIN}/version.json`);
    assert.equal(report.buildArtifact.version, REMOTE_BUILD_ARTIFACT.version);
  });

  test("deployed-origin build artifact failure is attributed to devops", async () => {
    const fetchImpl = async () => {
      throw new Error("simulated unreachable origin");
    };

    const { report } = await runParitySmoke({ originUrl: REMOTE_ORIGIN, fetchImpl });
    assert.equal(report.result, "failed");
    assert.equal(report.failure.stepId, "devops/build-artifact");
    assert.equal(report.failure.owningLayer, "devops");
    assert.match(report.failure.message, /could not reach/);
    assert.match(report.failure.message, /simulated unreachable origin/);
    assert.equal(report.buildArtifact, null);
  });

  test("server upsert produces canonical content-item v1.1.0 shape consumed downstream", async () => {
    const { ctx } = await runParitySmoke({ originUrl: ORIGIN });

    // Content item carries the publish-handoff fields it received plus
    // server-filled id/timestamps/endpoints.self/source.history.
    const svc = ctx.serviceItem;
    assert.equal(svc.type, "service");
    assert.equal(svc.target.type, "service");
    assert.equal(svc.target.serviceUrl, ctx.publishHandoff.target.serviceUrl);
    assert.equal(svc.source.kind, "publish");
    assert.ok(svc.source.history.length >= 1, "server must append a history entry on publish");
    assert.equal(svc.endpoints.self.format, "Honua:Portal:v1");
    assert.deepEqual(svc.endpoints.geoservices, ctx.publishHandoff.endpoints.geoservices);

    // Summary projection carries every content-item/v1.1.0 ContentItemSummary field.
    const summary = ctx.summary;
    for (const required of [
      "id",
      "slug",
      "type",
      "title",
      "summary",
      "owner",
      "tags",
      "extent",
      "preview",
      "modified",
      "capabilities",
      "formats",
      "sharing",
      "openData",
      "viewerSupport",
    ]) {
      assert.ok(required in summary, `summary missing required field "${required}"`);
    }
    // content-item/v1.1.0 ContentItemSummary.viewerSupport is null when
    // the publisher has not asserted an honua-portal-viewer extension
    // override. The fixture has no extensions, so the projection MUST
    // emit null here — anything else means the SDK adapter has baked the
    // type-default openability gate back into the summary DTO.
    assert.equal(summary.viewerSupport, null);
    // The publish-handoff fixture populates geoservices + ogcFeatures, so
    // the summary's format pills must list both — proving the catalog card
    // can render without a per-item detail fetch.
    assert.deepEqual([...summary.formats].sort(), ["GeoServices:FeatureService", "OGC:API:Features"]);
  });

  test("generated app publish writes target.url/framework and saved-map provenance", async () => {
    const { ctx } = await runParitySmoke({ originUrl: ORIGIN });
    const app = ctx.generatedApp;
    assert.equal(app.type, "app");
    assert.equal(app.target.type, "app");
    assert.equal(app.target.framework, "honua");
    assert.match(app.target.url, new RegExp(`^${ORIGIN.replace(/[.*+?^${}()|[\]\\]/g, "\\$&")}/apps/`));
    assert.equal(app.source.kind, "manual");
    assert.equal(app.source.sourceId, ctx.savedMap.id);
    const dep = app.dependencies.find((d) => d.id === ctx.savedMap.id);
    assert.ok(dep, "generated app must carry a dependency back to the source saved map");
    assert.equal(dep.role, "saved-map", "dependency role must be saved-map per generated-app-lifecycle/v1");
    // Provenance closure on the active revision references the same source.
    const ext = app.extensions["honua-generated-app"];
    const active = ext.revisions.find((r) => r.id === ext.activeRevisionId);
    const provenance = active.provenance.find((p) => p.itemId === ctx.savedMap.id);
    assert.ok(provenance, "active revision must record source provenance");
    assert.equal(provenance.role, "saved-map");
  });

  test("share-access response stays inside share-access/v1 shape (no openData leak)", async () => {
    const { ctx } = await runParitySmoke({ originUrl: ORIGIN });
    // The scenario stores the patched tier on ctx.itemIds; re-issue the
    // patch through the live adapter to inspect the response shape directly.
    const result = ctx.server.patchAccess({ itemId: ctx.generatedApp.id, tier: "org", embeddable: true });
    assert.equal(result.kind, "ok");
    assert.deepEqual(Object.keys(result.access).sort(), ["embeddable", "sharing"]);
    assert.equal(result.access.sharing, "org");
    assert.equal(result.access.embeddable, true);
  });

  test("a server failure is attributed to the server layer and short-circuits the chain", async () => {
    const brokenServerStep = SCENARIO_STEPS.findIndex((s) => s.id === "server/catalog-upsert");
    const customSteps = SCENARIO_STEPS.map((s, i) =>
      i === brokenServerStep
        ? {
            ...s,
            async run() {
              throw new Error("simulated honua-server upsert failure: 500");
            },
          }
        : s,
    );

    const { report } = await runParitySmoke({ originUrl: ORIGIN, steps: customSteps });
    assert.equal(report.result, "failed");
    assert.ok(report.failure);
    assert.equal(report.failure.stepId, "server/catalog-upsert");
    assert.equal(report.failure.owningLayer, "server");
    assert.equal(report.failure.owningRepo, "honua-server");
    assert.match(report.failure.message, /simulated honua-server upsert failure/);

    // Steps after the failure are recorded as skipped (not silently dropped)
    // so the evidence makes the short-circuit visible.
    const skipped = report.steps.filter((s) => s.status === "skipped").map((s) => s.id);
    assert.ok(skipped.length > 0);
    assert.ok(skipped.includes("server/embed-token-mint"));
    assert.ok(skipped.includes("console/embed-render"));
  });

  test("a Console same-origin violation is attributed to the console layer", async () => {
    // Inject a Studio draft step that points at a cross-origin preview, which
    // would happen if the Console route map regressed during the porting work.
    const customSteps = SCENARIO_STEPS.map((s) =>
      s.id === "console/studio-draft"
        ? {
            ...s,
            async run(ctx) {
              // Same shape as the real step but with an off-origin URL.
              const { assertSameOrigin } = await import("../adapters/console.mjs");
              const draftUrl = `https://studio.other.example/drafts/${ctx.savedMap.id}`;
              assertSameOrigin(ctx.originUrl, { draft: draftUrl });
              return { evidence: { draftUrl } };
            },
          }
        : s,
    );

    const { report } = await runParitySmoke({ originUrl: ORIGIN, steps: customSteps });
    assert.equal(report.result, "failed");
    assert.equal(report.failure.stepId, "console/studio-draft");
    assert.equal(report.failure.owningLayer, "console");
    assert.match(report.failure.message, /Same-origin invariant broken/);
  });

  test("a SDK projection failure is attributed to the sdk layer", async () => {
    const customSteps = SCENARIO_STEPS.map((s) =>
      s.id === "sdk/catalog-projection"
        ? {
            ...s,
            async run(ctx) {
              const { projectCatalogSummary, resolveViewerOpenability } = await import("../adapters/sdk.mjs");
              const summary = projectCatalogSummary({
                ...ctx.serviceItem,
                // Simulate the SDK accidentally projecting a non-openable
                // type (the type-default openability gate flips negative).
                type: "scene",
              });
              ctx.summary = summary;
              const openability = resolveViewerOpenability(summary);
              if (openability.supported !== true) {
                throw new Error(
                  `SDK projection + viewer openability mark the published service as unsupported: ${openability.reason ?? "no reason"}`,
                );
              }
              return { evidence: {} };
            },
          }
        : s,
    );
    const { report } = await runParitySmoke({ originUrl: ORIGIN, steps: customSteps });
    assert.equal(report.result, "failed");
    assert.equal(report.failure.owningLayer, "sdk");
  });

  test("a legacy-admin malformed publish-handoff is attributed to legacy-admin", async () => {
    const customSteps = SCENARIO_STEPS.map((s) =>
      s.id === "legacy-admin/operator-publish"
        ? {
            ...s,
            async run() {
              const { validatePublishHandoff } = await import("../adapters/admin.mjs");
              validatePublishHandoff({ type: "service" }, "test");
            },
          }
        : s,
    );
    const { report } = await runParitySmoke({ originUrl: ORIGIN, steps: customSteps });
    assert.equal(report.result, "failed");
    assert.equal(report.failure.owningLayer, "legacy-admin");
    assert.equal(report.failure.owningRepo, "honua-server-admin");
  });

  test("a query-string embed token is attributed to the console layer", async () => {
    const customSteps = SCENARIO_STEPS.map((s) =>
      s.id === "console/embed-render"
        ? {
            ...s,
            async run(ctx) {
              const { assertEmbedTokenInFragment } = await import("../adapters/console.mjs");
              // Build a URL that mistakenly carries the token in the query string.
              const bad = `${ctx.originUrl}/embed/items/${ctx.generatedApp.id}?token=${ctx.embedToken}`;
              assertEmbedTokenInFragment(bad);
            },
          }
        : s,
    );
    const { report } = await runParitySmoke({ originUrl: ORIGIN, steps: customSteps });
    assert.equal(report.result, "failed");
    assert.equal(report.failure.owningLayer, "console");
    assert.match(report.failure.message, /query string|fragment/i);
  });
});
