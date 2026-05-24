import { describe, expect, it } from "vitest";
import {
  DEFAULT_EMBED_ROUTE_PARAMS,
  parseEmbedParams,
  parseEmbedTokenFragment,
  parseExtent,
  resolveEffectiveExtent,
} from "../route.js";

describe("parseEmbedParams (AC3 chrome variants)", () => {
  it("returns defaults on an empty query", () => {
    expect(parseEmbedParams("")).toEqual(DEFAULT_EMBED_ROUTE_PARAMS);
  });

  it("parses chrome=minimal|none|full", () => {
    expect(parseEmbedParams("chrome=minimal").chrome).toBe("minimal");
    expect(parseEmbedParams("chrome=none").chrome).toBe("none");
    expect(parseEmbedParams("chrome=full").chrome).toBe("full");
  });

  it("falls back to default chrome on garbage", () => {
    expect(parseEmbedParams("chrome=floating").chrome).toBe("minimal");
  });

  it("parses legend/zoom on/off case-insensitively", () => {
    expect(parseEmbedParams("legend=ON&zoom=OFF").legend).toBe(true);
    expect(parseEmbedParams("legend=ON&zoom=OFF").zoom).toBe(false);
    expect(parseEmbedParams("legend=true&zoom=0").legend).toBe(true);
    expect(parseEmbedParams("zoom=0").zoom).toBe(false);
  });

  it("parses a valid extent in WGS84 lon/lat", () => {
    const params = parseEmbedParams("extent=-180,-90,180,90");
    expect(params.extent).toEqual({
      west: -180,
      south: -90,
      east: 180,
      north: 90,
    });
  });
});

describe("parseExtent fallbacks (AC3 defensive parsing)", () => {
  it("returns null on null/empty", () => {
    expect(parseExtent(null)).toBeNull();
    expect(parseExtent("")).toBeNull();
  });

  it("returns null on wrong arity", () => {
    expect(parseExtent("1,2,3")).toBeNull();
    expect(parseExtent("1,2,3,4,5")).toBeNull();
  });

  it("returns null on non-numeric components", () => {
    expect(parseExtent("foo,2,3,4")).toBeNull();
    expect(parseExtent("1,2,3,NaN")).toBeNull();
  });

  it("returns null on out-of-range lon/lat", () => {
    expect(parseExtent("-181,0,1,1")).toBeNull();
    expect(parseExtent("0,-91,1,1")).toBeNull();
  });

  it("returns null on degenerate extent (west>=east)", () => {
    expect(parseExtent("10,0,5,5")).toBeNull();
    expect(parseExtent("0,10,5,5")).toBeNull();
  });
});

describe("resolveEffectiveExtent", () => {
  it("prefers query extent when present", () => {
    const query = { west: -1, south: -1, east: 1, north: 1 };
    const persisted = { west: -2, south: -2, east: 2, north: 2 };
    expect(resolveEffectiveExtent({ query, persisted })).toBe(query);
  });

  it("falls back to persisted when query is null", () => {
    const persisted = { west: -2, south: -2, east: 2, north: 2 };
    expect(resolveEffectiveExtent({ query: null, persisted })).toBe(persisted);
  });

  it("returns null when neither is present", () => {
    expect(resolveEffectiveExtent({ query: null, persisted: null })).toBeNull();
  });
});

describe("parseEmbedTokenFragment (AC: token in URL fragment, not query)", () => {
  it("extracts the token from #embedToken=…", () => {
    expect(parseEmbedTokenFragment("#embedToken=abc")).toBe("abc");
    expect(parseEmbedTokenFragment("embedToken=abc")).toBe("abc");
  });

  it("decodes percent-encoded tokens", () => {
    expect(parseEmbedTokenFragment("#embedToken=tok%2Bslash%2F%3D")).toBe("tok+slash/=");
  });

  it("returns null on absent or empty fragments", () => {
    expect(parseEmbedTokenFragment(null)).toBeNull();
    expect(parseEmbedTokenFragment("")).toBeNull();
    expect(parseEmbedTokenFragment("#")).toBeNull();
  });

  it("returns null when fragment has no embedToken key", () => {
    expect(parseEmbedTokenFragment("#chrome=full")).toBeNull();
  });

  it("handles tokens containing literal '%' without throwing (regression: double-decode)", () => {
    // buildEmbedUrl encodes literal '%' as '%25'; URLSearchParams already
    // decodes '%25' back to '%'. A second decodeURIComponent would throw
    // URIError on the resulting '%foo' sequence.
    expect(() => parseEmbedTokenFragment("#embedToken=tok%25foo")).not.toThrow();
    expect(parseEmbedTokenFragment("#embedToken=tok%25foo")).toBe("tok%foo");
  });

  it("never throws on malformed percent-encoded fragments (defensive parsing)", () => {
    expect(() => parseEmbedTokenFragment("#embedToken=%E0")).not.toThrow();
    expect(() => parseEmbedTokenFragment("#embedToken=%fo")).not.toThrow();
    expect(() => parseEmbedTokenFragment("#embedToken=valid&extra=%")).not.toThrow();
  });
});
