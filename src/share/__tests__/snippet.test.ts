import { describe, expect, it } from "vitest";
import { parseEmbedTokenFragment } from "../../embed/route.js";
import {
  DEFAULT_EMBED_HEIGHT,
  DEFAULT_EMBED_PARAMS,
  DEFAULT_EMBED_WIDTH,
  buildEmbedSnippet,
  buildEmbedUrl,
  buildShareLink,
} from "../snippet.js";

const HOST = "https://portal.honua.example";

describe("buildShareLink (AC3: copy-link surface)", () => {
  it("resolves saved maps to /maps/:id", () => {
    expect(buildShareLink({ portalHost: HOST, itemId: "m-1", itemKind: "map" })).toBe(`${HOST}/maps/m-1`);
  });

  it("resolves other items to /catalog/:id", () => {
    expect(buildShareLink({ portalHost: HOST, itemId: "s-1", itemKind: "service" })).toBe(`${HOST}/catalog/s-1`);
  });

  it("appends ?token=… for public-link tier", () => {
    const url = buildShareLink({
      portalHost: HOST,
      itemId: "m-1",
      itemKind: "map",
      publicLinkToken: "abc-123",
    });
    expect(url).toBe(`${HOST}/maps/m-1?token=abc-123`);
  });

  it("rejects scheme-less hosts", () => {
    expect(() => buildShareLink({ portalHost: "portal.honua", itemId: "m-1", itemKind: "map" })).toThrow();
  });

  it("strips trailing slashes from the host", () => {
    expect(
      buildShareLink({
        portalHost: `${HOST}//`,
        itemId: "m-1",
        itemKind: "map",
      }),
    ).toBe(`${HOST}/maps/m-1`);
  });
});

describe("buildEmbedSnippet (AC3: copy-embed surface)", () => {
  it("emits the documented snippet shape with defaults", () => {
    const snippet = buildEmbedSnippet({ portalHost: HOST, itemId: "m-1" });
    expect(snippet).toContain(`src="${HOST}/embed/maps/m-1?chrome=minimal&legend=on&zoom=on"`);
    expect(snippet).toContain(`width="${DEFAULT_EMBED_WIDTH}"`);
    expect(snippet).toContain(`height="${DEFAULT_EMBED_HEIGHT}"`);
    expect(snippet).toContain(`loading="lazy"`);
    expect(snippet).toContain(`allow="fullscreen"`);
    expect(snippet).toContain(`referrerpolicy="strict-origin-when-cross-origin"`);
  });

  it("respects custom chrome/legend/zoom and extent", () => {
    const url = buildEmbedUrl({
      portalHost: HOST,
      itemId: "m-1",
      embed: {
        chrome: "none",
        legend: false,
        zoom: false,
        extent: { west: -123, south: 37, east: -122, north: 38 },
      },
    });
    expect(url).toBe(`${HOST}/embed/maps/m-1?chrome=none&legend=off&zoom=off&extent=-123%2C37%2C-122%2C38`);
  });

  it("places the embed token in the URL fragment, not the query", () => {
    const url = buildEmbedUrl({
      portalHost: HOST,
      itemId: "m-1",
      embedToken: "tok+slash/=",
    });
    expect(url.includes("?embedToken=")).toBe(false);
    expect(url.endsWith("#embedToken=tok%2Bslash%2F%3D")).toBe(true);
  });

  it("round-trips tokens with literal '%' through build → parse without throwing", () => {
    const original = "tok%foo";
    const url = buildEmbedUrl({
      portalHost: HOST,
      itemId: "m-1",
      embedToken: original,
    });
    const fragment = url.slice(url.indexOf("#"));
    expect(() => parseEmbedTokenFragment(fragment)).not.toThrow();
    expect(parseEmbedTokenFragment(fragment)).toBe(original);
  });

  it("default params are stable", () => {
    expect(DEFAULT_EMBED_PARAMS).toEqual({
      chrome: "minimal",
      legend: true,
      zoom: true,
      extent: null,
    });
  });
});
