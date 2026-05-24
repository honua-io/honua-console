import { describe, expect, it } from "vitest";
import { canEmbedAudienceAccess, compareTier, evaluateShareEscalation, tierRank } from "../policy.js";
import { type ClosureNode, SHARING_TIER_ORDER } from "../types.js";

const node = (
  id: string,
  sharing: ClosureNode["access"] extends infer A ? (A extends { sharing: infer S } ? S : never) : never,
  embeddable = false,
): ClosureNode => ({
  id,
  type: "layer",
  role: "operationalLayer",
  access: sharing === undefined ? "unsupported" : { sharing, embeddable },
});

describe("tier ordering", () => {
  it("ranks tiers narrowest → widest", () => {
    expect(SHARING_TIER_ORDER).toEqual(["private", "org", "group", "public-link", "public"]);
    expect(tierRank("private")).toBe(0);
    expect(tierRank("public")).toBe(4);
  });

  it("compareTier is sign-correct", () => {
    expect(compareTier("private", "public")).toBeLessThan(0);
    expect(compareTier("public", "private")).toBeGreaterThan(0);
    expect(compareTier("org", "org")).toBe(0);
  });
});

describe("evaluateShareEscalation", () => {
  it("AC1: narrowing or equal-tier change is always ok", () => {
    expect(
      evaluateShareEscalation({
        current: "public",
        proposed: "private",
        closure: [node("dep", "private")],
      }),
    ).toEqual({ kind: "ok", widening: false });

    expect(
      evaluateShareEscalation({
        current: "org",
        proposed: "org",
        closure: [node("dep", "private")],
      }),
    ).toEqual({ kind: "ok", widening: false });
  });

  it("AC1: widening with all deps already that wide is ok", () => {
    expect(
      evaluateShareEscalation({
        current: "private",
        proposed: "org",
        closure: [node("a", "org"), node("b", "public")],
      }),
    ).toEqual({ kind: "ok", widening: true });
  });

  it("dependency-closure block when escalating to public with a private dep", () => {
    const result = evaluateShareEscalation({
      current: "private",
      proposed: "public",
      closure: [node("a", "public"), node("b", "private"), node("c", "org")],
    });
    expect(result.kind).toBe("blocked");
    if (result.kind !== "blocked") return;
    expect(result.blockers.map((b) => b.id)).toEqual(["b", "c"]);
  });

  it("dependency-closure block on public-link with a private dep", () => {
    const result = evaluateShareEscalation({
      current: "org",
      proposed: "public-link",
      closure: [node("dep", "private")],
    });
    expect(result.kind).toBe("blocked");
  });

  it("unsupported deps never block escalation", () => {
    const unsupported: ClosureNode = {
      id: "style-1",
      type: "style",
      role: "style",
      access: "unsupported",
    };
    const result = evaluateShareEscalation({
      current: "private",
      proposed: "public",
      closure: [unsupported],
    });
    expect(result).toEqual({ kind: "ok", widening: true });
  });

  it("preserves blocker order from input closure (server parity)", () => {
    const result = evaluateShareEscalation({
      current: "private",
      proposed: "public",
      closure: [node("z", "private"), node("a", "private"), node("m", "public")],
    });
    expect(result.kind).toBe("blocked");
    if (result.kind !== "blocked") return;
    expect(result.blockers.map((b) => b.id)).toEqual(["z", "a"]);
  });
});

describe("canEmbedAudienceAccess (AC2 matrix)", () => {
  it("public viewer can read public; cannot read private", () => {
    expect(canEmbedAudienceAccess({ viewerTier: "public", nodeTier: "public" })).toBe(true);
    expect(canEmbedAudienceAccess({ viewerTier: "public", nodeTier: "private" })).toBe(false);
  });

  it("public viewer can read public-link items", () => {
    expect(canEmbedAudienceAccess({ viewerTier: "public", nodeTier: "public-link" })).toBe(true);
  });

  it("public viewer cannot read org or group", () => {
    expect(canEmbedAudienceAccess({ viewerTier: "public", nodeTier: "org" })).toBe(false);
    expect(canEmbedAudienceAccess({ viewerTier: "public", nodeTier: "group" })).toBe(false);
  });

  it("org viewer can read org and group", () => {
    expect(canEmbedAudienceAccess({ viewerTier: "org", nodeTier: "org" })).toBe(true);
    expect(canEmbedAudienceAccess({ viewerTier: "org", nodeTier: "group" })).toBe(true);
  });

  it("private nodes are never readable", () => {
    for (const viewer of SHARING_TIER_ORDER) {
      expect(canEmbedAudienceAccess({ viewerTier: viewer, nodeTier: "private" })).toBe(false);
    }
  });
});
