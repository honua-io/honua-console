import { describe, expect, it } from "vitest";
import { mapUrl, parseMapUrl } from "../url.js";

describe("mapUrl", () => {
  it("emits the canonical /maps/{id} path", () => {
    expect(mapUrl("01HABC")).toBe("/maps/01HABC");
  });

  it("appends ephemeral view state when provided", () => {
    expect(
      mapUrl("01HABC", {
        center: [-122.4194, 37.7749],
        zoom: 12,
        t: "2026-05-06T00:00:00Z",
      }),
    ).toBe("/maps/01HABC?center=-122.4194,37.7749&zoom=12&t=2026-05-06T00%3A00%3A00Z");
  });

  it("encodes ids with reserved characters", () => {
    expect(mapUrl("foo/bar")).toBe("/maps/foo%2Fbar");
  });

  it("rejects empty ids", () => {
    expect(() => mapUrl("")).toThrow(/required/);
  });
});

describe("parseMapUrl", () => {
  it("parses a canonical path", () => {
    expect(parseMapUrl("/maps/01HABC")).toEqual({ id: "01HABC" });
  });

  it("parses ephemeral view state from the query string", () => {
    expect(parseMapUrl("/maps/01HABC?center=-122.4194,37.7749&zoom=12.5&t=2026")).toEqual({
      id: "01HABC",
      viewState: {
        center: [-122.4194, 37.7749],
        zoom: 12.5,
        t: "2026",
      },
    });
  });

  it("parses an absolute URL", () => {
    expect(parseMapUrl("https://portal.example.com/maps/abc?zoom=4")).toEqual({
      id: "abc",
      viewState: { zoom: 4 },
    });
  });

  it("returns null for non-saved-map paths", () => {
    expect(parseMapUrl("/catalog?type=map")).toBeNull();
    expect(parseMapUrl("/maps")).toBeNull();
  });

  it("ignores malformed center/zoom and surfaces only valid view-state fields", () => {
    expect(parseMapUrl("/maps/abc?center=garbage&zoom=NaN&t=ok")).toEqual({
      id: "abc",
      viewState: { t: "ok" },
    });
  });
});
