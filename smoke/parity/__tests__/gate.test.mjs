import { strict as assert } from "node:assert";
import { describe, test } from "node:test";

import { evaluateGate, GATE_BLOCKERS } from "../check-gate.mjs";
import { runParitySmoke } from "../run.mjs";

const ORIGIN = "http://127.0.0.1:4174";

// A minimal well-formed real-server evidence stub.
function realServerEvidence(overrides = {}) {
  return {
    scenario: "console-parity-publish-to-embed",
    result: "ok",
    sourceHydrated: true,
    server: { image: "ghcr.io/honua-io/honua-server@sha256:abc", commit: "deadbeef", seedProfile: "console-e2e" },
    ...overrides,
  };
}

describe("honua-console#9 real-server gate", () => {
  test("an in-memory parity run does NOT satisfy the gate (AC2)", async () => {
    const { report } = await runParitySmoke({ originUrl: ORIGIN });

    // The in-memory run is green and useful as a contract-shape check...
    assert.equal(report.result, "ok");
    // ...but it is not hydrated from a real server, so the gate refuses it.
    assert.equal(report.sourceHydrated, false);
    assert.equal(report.server, null);

    const gate = evaluateGate(report);
    assert.equal(gate.satisfied, false, "mock-only evidence must not satisfy the gate");
    assert.equal(gate.pending, true, "mock-only evidence is pending, not a hard failure");
  });

  test("complete, green real-server evidence satisfies the gate", () => {
    const gate = evaluateGate(realServerEvidence());
    assert.equal(gate.satisfied, true);
    assert.equal(gate.pending, false);
    assert.deepEqual(gate.reasons, []);
  });

  test("real-server evidence missing image/commit/seedProfile is rejected (not pending)", () => {
    const gate = evaluateGate(realServerEvidence({ server: { image: "ghcr.io/honua-io/honua-server:x" } }));
    assert.equal(gate.satisfied, false);
    assert.equal(gate.pending, false, "a claimed real-server run with bad provenance is a failure, not pending");
    assert.ok(gate.reasons.some((r) => r.includes("server.commit")));
    assert.ok(gate.reasons.some((r) => r.includes("server.seedProfile")));
  });

  test("a failed real-server scenario does not satisfy the gate", () => {
    const gate = evaluateGate(realServerEvidence({ result: "failed" }));
    assert.equal(gate.satisfied, false);
    assert.equal(gate.pending, false);
    assert.ok(gate.reasons.some((r) => r.includes("result")));
  });

  test("evidence that claims hydration but omits the server block is rejected, not pending", () => {
    // sourceHydrated true with server=null is an inconsistent/forged claim, not
    // an honest mock-only run, so it must fail rather than slip through pending.
    const gate = evaluateGate({ result: "ok", sourceHydrated: true, server: null });
    assert.equal(gate.satisfied, false);
    assert.equal(gate.pending, false);
    assert.ok(gate.reasons.some((r) => r.includes("server provenance")));
  });

  test("non-object evidence is rejected", () => {
    assert.equal(evaluateGate(null).satisfied, false);
    assert.equal(evaluateGate(undefined).pending, false);
  });

  test("the upstream blockers are named so PENDING is actionable", () => {
    assert.ok(GATE_BLOCKERS.some((b) => b.includes("honua-server#1162")));
    assert.ok(GATE_BLOCKERS.some((b) => b.includes("honua-sdk-dotnet#166")));
  });
});
