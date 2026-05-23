import { describe, expect, it } from "vitest";

import {
  APP_BUILDER_PROOF_FIXTURES,
  DEFAULT_APP_BUILDER_PROOF_FIXTURE,
  isBlockingProofFixture,
  normalizeAppBuilderProofFixture,
} from "./proofFixture.js";

describe("studio proof fixture surface", () => {
  it("exposes the six fixtures defined by the model-free smoke harness", () => {
    expect([...APP_BUILDER_PROOF_FIXTURES]).toEqual([
      "happy",
      "clarification",
      "unsupported",
      "auth-denied",
      "oversized",
      "apply-failure",
    ]);
  });

  it("normalizes unknown fixture ids to the default", () => {
    expect(normalizeAppBuilderProofFixture("nonsense")).toBe(DEFAULT_APP_BUILDER_PROOF_FIXTURE);
    expect(normalizeAppBuilderProofFixture(null)).toBe(DEFAULT_APP_BUILDER_PROOF_FIXTURE);
  });

  it("marks the unsupported/auth/oversized fixtures as blocking", () => {
    expect(isBlockingProofFixture("unsupported")).toBe(true);
    expect(isBlockingProofFixture("auth-denied")).toBe(true);
    expect(isBlockingProofFixture("oversized")).toBe(true);
    expect(isBlockingProofFixture("happy")).toBe(false);
    expect(isBlockingProofFixture("apply-failure")).toBe(false);
  });
});
