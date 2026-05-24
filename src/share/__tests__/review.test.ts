import { describe, expect, it } from "vitest";

import type { GetDependenciesResponse } from "../../contracts/content-item.js";
import { reviewShareChange } from "../review.js";

const emptyClosure: GetDependenciesResponse = {
  nodes: [],
  missing: [],
  unauthorized: [],
  unsupported: [],
  truncated: false,
};

describe("reviewShareChange", () => {
  it("does not block narrowing when stale or revoked dependencies exist", () => {
    const result = reviewShareChange({
      current: "public",
      proposed: "org",
      closure: {
        ...emptyClosure,
        unauthorized: [{ id: "private-layer", type: "layer", role: "operationalLayer" }],
        missing: [{ id: "missing-layer", type: "layer", role: "operationalLayer" }],
      },
    });
    expect(result).toEqual({ kind: "ok", widening: false, notices: [] });
  });

  it("blocks widening when the dependency closure contains narrower, denied, or missing dependencies", () => {
    const result = reviewShareChange({
      current: "org",
      proposed: "public",
      closure: {
        nodes: [
          {
            id: "org-layer",
            type: "layer",
            role: "operationalLayer",
            depth: 1,
            summary: {
              id: "org-layer",
              slug: null,
              type: "layer",
              title: "Org layer",
              summary: "",
              owner: { id: "org_honua", name: "City of Honua", kind: "org" },
              tags: [],
              extent: null,
              preview: { thumbnail: null },
              modified: "2026-05-01T00:00:00.000Z",
              capabilities: [],
              formats: [],
              sharing: "org",
              openData: false,
              viewerSupport: null,
            },
          },
        ],
        unauthorized: [{ id: "revoked-layer", type: "layer", role: "operationalLayer" }],
        missing: [{ id: "missing-layer", type: "layer", role: "operationalLayer" }],
        unsupported: [{ id: "legacy-style", type: "service", role: "operationalLayer" }],
        truncated: false,
      },
    });

    expect(result.kind).toBe("blocked");
    if (result.kind !== "blocked") return;
    expect(result.blockers.map((blocker) => `${blocker.reason}:${blocker.id}`)).toEqual([
      "narrower-tier:org-layer",
      "unauthorized:revoked-layer",
      "missing:missing-layer",
    ]);
    expect(result.notices.map((notice) => notice.id)).toEqual(["legacy-style"]);
  });
});
