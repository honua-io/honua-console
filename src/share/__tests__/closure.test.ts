import { describe, expect, it } from "vitest";
import { DEFAULT_CLOSURE_LIMITS, buildLookup, getDependencyClosure } from "../closure.js";
import type { ClosureItem } from "../types.js";
import { FIXTURE_ITEMS } from "./fixtures.js";

describe("getDependencyClosure", () => {
  it("walks BFS and returns each transitive dep once", () => {
    const result = getDependencyClosure("map-1", buildLookup(FIXTURE_ITEMS));
    expect(result.truncated).toBe(false);
    expect(result.nodes.map((n) => n.id)).toEqual(["layer-a", "layer-b", "style-1", "service-a", "service-b"]);
  });

  it("does not include the root", () => {
    const result = getDependencyClosure("map-1", buildLookup(FIXTURE_ITEMS));
    expect(result.nodes.find((n) => n.id === "map-1")).toBeUndefined();
  });

  it("renders unresolvable refs as unsupported", () => {
    const item: ClosureItem = {
      id: "map-2",
      type: "map",
      dependencies: [{ id: "ghost", type: "layer", role: "operationalLayer" }],
    };
    const result = getDependencyClosure("map-2", buildLookup([item]));
    expect(result.nodes).toHaveLength(1);
    expect(result.nodes[0]?.id).toBe("ghost");
    expect(result.nodes[0]?.access).toBe("unsupported");
  });

  it("handles cycles without looping", () => {
    const items: ClosureItem[] = [
      {
        id: "a",
        type: "layer",
        dependencies: [{ id: "b", type: "layer", role: "operationalLayer" }],
      },
      {
        id: "b",
        type: "layer",
        dependencies: [{ id: "a", type: "layer", role: "operationalLayer" }],
      },
    ];
    const result = getDependencyClosure("a", buildLookup(items));
    expect(result.nodes.map((n) => n.id)).toEqual(["b"]);
  });

  it("respects maxDepth", () => {
    const chain: ClosureItem[] = ["a", "b", "c", "d", "e", "f", "g"].map((id, i, arr) => ({
      id,
      type: "layer",
      dependencies:
        i + 1 < arr.length
          ? [
              {
                id: arr[i + 1] ?? "",
                type: "layer",
                role: "operationalLayer",
              },
            ]
          : [],
    }));
    const result = getDependencyClosure("a", buildLookup(chain), {
      ...DEFAULT_CLOSURE_LIMITS,
      maxDepth: 2,
    });
    expect(result.nodes.map((n) => n.id)).toEqual(["b", "c"]);
  });

  it("respects maxNodes (truncates)", () => {
    const items: ClosureItem[] = [
      {
        id: "root",
        type: "map",
        dependencies: Array.from({ length: 5 }, (_, i) => ({
          id: `dep-${i}`,
          type: "layer" as const,
          role: "operationalLayer" as const,
        })),
      },
      ...Array.from({ length: 5 }, (_, i) => ({
        id: `dep-${i}`,
        type: "layer" as const,
        dependencies: [],
      })),
    ];
    const result = getDependencyClosure("root", buildLookup(items), {
      maxDepth: 5,
      maxNodes: 2,
    });
    expect(result.truncated).toBe(true);
    expect(result.nodes).toHaveLength(2);
  });

  it("returns empty closure for an unknown root", () => {
    const result = getDependencyClosure("missing", buildLookup(FIXTURE_ITEMS));
    expect(result.nodes).toEqual([]);
    expect(result.truncated).toBe(false);
  });
});
