import { strict as assert } from "node:assert";
import { describe, test } from "node:test";

import { OWNING_LAYER_IDS } from "../owning-layers.mjs";
import {
  READ_MATRIX_CASES,
  READ_STATUS,
  SCENARIO_MATRIX_STEPS,
  anonymous,
  authenticated,
  createCatalogFixture,
  resolveItemRead,
  resolveSearch,
  runScenarioMatrix,
} from "../scenario-matrix.mjs";

const ORIGIN = "http://127.0.0.1:4174";

describe("Console catalog/share read-context matrix", () => {
  test("every matrix case resolves to its expected read-state and is console-owned", async () => {
    const { report } = await runScenarioMatrix({ originUrl: ORIGIN });

    assert.equal(report.result, "ok", `matrix failed: ${report.failure?.message ?? "unknown"}`);
    assert.equal(report.failure, null);
    // build-artifact step + one step per matrix case.
    assert.equal(report.steps.length, SCENARIO_MATRIX_STEPS.length);
    assert.equal(report.steps.length, READ_MATRIX_CASES.length + 1);

    for (const step of report.steps) {
      assert.equal(step.status, "ok", `${step.id} ended in ${step.status}: ${step.error}`);
      assert.ok(OWNING_LAYER_IDS.includes(step.owningLayer));
    }

    // Every read-case step is attributed to the console layer (the read-context
    // rules are Console-owned, in front of the eventual server read endpoint).
    const readSteps = report.steps.filter((s) => s.id.startsWith("console/read-"));
    assert.equal(readSteps.length, READ_MATRIX_CASES.length);
    for (const step of readSteps) {
      assert.equal(step.owningLayer, "console");
      assert.ok(step.evidence.axis, `${step.id} must record its matrix axis`);
    }
  });

  test("matrix spans the auth, RBAC-denied, and empty-state axes", () => {
    const axisFamilies = new Set(READ_MATRIX_CASES.map((c) => c.axis.split("/")[0]));
    assert.ok(axisFamilies.has("auth"), "matrix must cover the auth axis");
    assert.ok(axisFamilies.has("rbac"), "matrix must cover the RBAC-denied axis");
    assert.ok(axisFamilies.has("empty"), "matrix must cover the empty-state axis");
  });

  test("anonymous public read is allowed and flagged anonymous", () => {
    const items = createCatalogFixture();
    const result = resolveItemRead(items, "coastal-flood-service", anonymous());
    assert.equal(result.status, READ_STATUS.allowed);
    assert.equal(result.anonymousRead, true);
  });

  test("public-link read requires the matching token", () => {
    const items = createCatalogFixture();
    assert.equal(resolveItemRead(items, "utilities-layer", anonymous("pl-utilities")).status, READ_STATUS.allowed);
    assert.equal(resolveItemRead(items, "utilities-layer", anonymous("wrong")).status, READ_STATUS.unavailable);
    assert.equal(resolveItemRead(items, "utilities-layer", anonymous()).status, READ_STATUS.unavailable);
  });

  test("RBAC denies anonymous reads of org items but allows authenticated reads", () => {
    const items = createCatalogFixture();
    assert.equal(resolveItemRead(items, "capital-projects-dashboard", anonymous()).status, READ_STATUS.unavailable);
    assert.equal(resolveItemRead(items, "capital-projects-dashboard", authenticated()).status, READ_STATUS.allowed);
  });

  test("unknown ids resolve to missing, distinct from RBAC-denied unavailable", () => {
    const items = createCatalogFixture();
    assert.equal(resolveItemRead(items, "nope", anonymous()).status, READ_STATUS.missing);
    assert.notEqual(READ_STATUS.missing, READ_STATUS.unavailable);
  });

  test("anonymous browse hides protected items and tag filters cannot bypass visibility", () => {
    const items = createCatalogFixture();
    const visible = resolveSearch(items, anonymous());
    assert.deepEqual(
      visible.map((i) => i.id),
      ["svc-coastal-flood"],
    );
    // 'utilities' tag belongs only to the public-link layer; untokened anonymous
    // browse must not surface it.
    assert.equal(resolveSearch(items, anonymous(), { tag: "utilities" }).length, 0);
  });

  test("a query matching nothing returns an empty result set", () => {
    const items = createCatalogFixture();
    assert.equal(resolveSearch(items, authenticated(), { query: "no-such-content" }).length, 0);
  });

  test("a matrix-case regression fails the smoke and is attributed to console", async () => {
    const brokenCase = READ_MATRIX_CASES.find((c) => c.id === "anon-org-item-rbac-denied");
    const customSteps = SCENARIO_MATRIX_STEPS.map((step) =>
      step.id === `console/read-${brokenCase.id}`
        ? {
            ...step,
            async run() {
              // Simulate a read-context regression that lets an anonymous user
              // read an org item — the matrix must catch it.
              const items = createCatalogFixture();
              const result = resolveItemRead(items, "capital-projects-dashboard", anonymous());
              if (result.status !== READ_STATUS.allowed) {
                throw new Error(
                  `read-state mismatch: expected "${READ_STATUS.allowed}", got "${result.status}"`,
                );
              }
              return { evidence: {} };
            },
          }
        : step,
    );
    const { report } = await runScenarioMatrix({ originUrl: ORIGIN, steps: customSteps });
    assert.equal(report.result, "failed");
    assert.equal(report.failure.stepId, `console/read-${brokenCase.id}`);
    assert.equal(report.failure.owningLayer, "console");
    assert.match(report.failure.message, /read-state mismatch/);
  });
});
