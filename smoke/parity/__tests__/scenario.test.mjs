import { strict as assert } from "node:assert";
import { describe, test } from "node:test";

import { OWNING_LAYER_IDS } from "../owning-layers.mjs";
import { SCENARIO_STEPS } from "../scenario.mjs";
import { runParitySmoke } from "../run.mjs";

const ORIGIN = "https://console.smoke.honua.example";

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
    // same-origin with the deployable artifact.
    assert.ok(report.urls);
    const expectedOrigin = new URL(ORIGIN).origin;
    for (const value of Object.values(report.urls)) {
      assert.equal(new URL(value).origin, expectedOrigin);
    }

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
    assert.deepEqual(report.buildArtifact.areas, ["studio", "catalog", "share", "operate"]);

    // The runner records the origin used so a CI reviewer can correlate the
    // evidence with the artifact it ran against.
    assert.equal(report.originUrl, ORIGIN);

    // Sanity check the ctx is consistent with the report.
    assert.equal(ctx.itemIds.serviceItemId, report.items.serviceItemId);
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
              const { projectCatalogSummary } = await import("../adapters/sdk.mjs");
              const summary = projectCatalogSummary({
                ...ctx.serviceItem,
                // Simulate the SDK accidentally producing a non-service type
                // that fails the viewer-support check.
                type: "scene",
              });
              ctx.summary = summary;
              if (!summary.viewerSupport.supported) {
                throw new Error(`SDK projection marks the published service as unsupported by the viewer: ${summary.viewerSupport.reason}`);
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

  test("a legacy-admin malformed publish event is attributed to legacy-admin", async () => {
    const customSteps = SCENARIO_STEPS.map((s) =>
      s.id === "legacy-admin/operator-publish"
        ? {
            ...s,
            async run() {
              const { validatePublishEvent } = await import("../adapters/admin.mjs");
              validatePublishEvent({ eventKind: "publish" }, "test");
            },
          }
        : s,
    );
    const { report } = await runParitySmoke({ originUrl: ORIGIN, steps: customSteps });
    assert.equal(report.result, "failed");
    assert.equal(report.failure.owningLayer, "legacy-admin");
    assert.equal(report.failure.owningRepo, "honua-server-admin");
  });
});
