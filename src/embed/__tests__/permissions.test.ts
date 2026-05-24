import { describe, expect, it } from "vitest";
import { FIXTURE_ITEMS } from "../../share/__tests__/fixtures.js";
import type { ClosureItem, ShareAccess } from "../../share/types.js";
import { resolveEmbedAuthorization } from "../permissions.js";

const access = (sharing: ShareAccess["sharing"], embeddable = false): ShareAccess => ({ sharing, embeddable });

const publicMapWithMixedDeps = (): ClosureItem[] => [
  {
    id: "map-public",
    type: "map",
    title: "Public map",
    access: access("public", true),
    dependencies: [
      { id: "layer-public", type: "layer", role: "operationalLayer" },
      { id: "layer-private", type: "layer", role: "operationalLayer" },
      { id: "style-1", type: "style", role: "style" },
    ],
  },
  {
    id: "layer-public",
    type: "layer",
    title: "Public layer",
    access: access("public"),
    dependencies: [],
  },
  {
    id: "layer-private",
    type: "layer",
    title: "Private layer",
    access: access("private"),
    dependencies: [],
  },
  {
    id: "style-1",
    type: "style",
    title: "Inline style",
    access: "unsupported",
    dependencies: [],
  },
];

describe("resolveEmbedAuthorization (AC2 + per-layer cells)", () => {
  it("public viewer reads a public map with public deps", () => {
    const items: ClosureItem[] = [
      {
        id: "map-1",
        type: "map",
        access: access("public", true),
        dependencies: [{ id: "layer-1", type: "layer", role: "operationalLayer" }],
      },
      {
        id: "layer-1",
        type: "layer",
        access: access("public"),
        dependencies: [],
      },
    ];
    const result = resolveEmbedAuthorization({
      rootId: "map-1",
      rootAccess: access("public", true),
      viewerTier: "public",
      closure: items,
    });
    expect(result.rootReadable).toBe(true);
    expect(result.cells).toHaveLength(1);
    expect(result.cells[0]?.kind).toBe("visible");
    expect(result.hasUnauthorizedDeps).toBe(false);
  });

  it("public viewer of a public-embed-with-private-dep gets per-layer unauthorized cell", () => {
    const items = publicMapWithMixedDeps();
    const result = resolveEmbedAuthorization({
      rootId: "map-public",
      rootAccess: access("public", true),
      viewerTier: "public",
      closure: items,
    });
    expect(result.rootReadable).toBe(true);
    const byKind = result.cells.reduce<Record<string, string[]>>((acc, c) => {
      acc[c.kind] ??= [];
      acc[c.kind]?.push(c.node.id);
      return acc;
    }, {});
    expect(byKind.visible).toEqual(["layer-public"]);
    expect(byKind.unauthorized).toEqual(["layer-private"]);
    expect(byKind.unsupported).toEqual(["style-1"]);
    expect(result.hasUnauthorizedDeps).toBe(true);
  });

  it("anonymous public viewer cannot read a private root map (tier denial)", () => {
    const result = resolveEmbedAuthorization({
      rootId: "map-1",
      rootAccess: access("private", true),
      viewerTier: "public",
      closure: FIXTURE_ITEMS,
    });
    expect(result.rootReadable).toBe(false);
    expect(result.rootBlockedBy).toBe("tier");
  });

  it("org viewer reads org-tier deps and is not blocked by them", () => {
    const result = resolveEmbedAuthorization({
      rootId: "map-1",
      rootAccess: access("org", true),
      viewerTier: "org",
      closure: FIXTURE_ITEMS,
    });
    expect(result.rootReadable).toBe(true);
    expect(result.rootBlockedBy).toBeNull();
    const layerA = result.cells.find((c) => c.node.id === "layer-a");
    expect(layerA?.kind).toBe("visible");
    const serviceA = result.cells.find((c) => c.node.id === "service-a");
    expect(serviceA?.kind).toBe("visible");
  });

  it("public + embeddable:false blocks the iframe surface even when the tier would allow it", () => {
    const items: ClosureItem[] = [
      {
        id: "map-1",
        type: "map",
        access: access("public", false),
        dependencies: [],
      },
    ];
    const result = resolveEmbedAuthorization({
      rootId: "map-1",
      rootAccess: access("public", false),
      viewerTier: "public",
      closure: items,
    });
    expect(result.rootReadable).toBe(false);
    expect(result.rootBlockedBy).toBe("embeddable");
  });

  it("public-link + embeddable:false also blocks the iframe surface", () => {
    const items: ClosureItem[] = [
      {
        id: "map-1",
        type: "map",
        access: access("public-link", false),
        dependencies: [],
      },
    ];
    const result = resolveEmbedAuthorization({
      rootId: "map-1",
      rootAccess: access("public-link", false),
      viewerTier: "public-link",
      closure: items,
    });
    expect(result.rootReadable).toBe(false);
    expect(result.rootBlockedBy).toBe("embeddable");
  });

  it("unsupported root surfaces as `unsupported`, not `unauthorized`", () => {
    const items: ClosureItem[] = [
      {
        id: "map-1",
        type: "map",
        access: "unsupported",
        dependencies: [],
      },
    ];
    const result = resolveEmbedAuthorization({
      rootId: "map-1",
      rootAccess: "unsupported",
      viewerTier: "public",
      closure: items,
    });
    expect(result.rootReadable).toBe(false);
    expect(result.rootBlockedBy).toBe("unsupported");
  });
});
