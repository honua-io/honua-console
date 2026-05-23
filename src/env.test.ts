import { describe, expect, it } from "vitest";

import { parseAuthDriver } from "./env";

describe("parseAuthDriver", () => {
  it("defaults non-production builds to fixture auth", () => {
    expect(parseAuthDriver("", { allowFixtureAuth: false, productionBuild: false })).toBe("fixture");
  });

  it("defaults production builds to whoami auth", () => {
    expect(parseAuthDriver("", { allowFixtureAuth: false, productionBuild: true })).toBe("whoami");
  });

  it("requires an explicit production override before fixture auth can be used", () => {
    expect(() => parseAuthDriver("fixture", { allowFixtureAuth: false, productionBuild: true })).toThrow(
      /VITE_ALLOW_FIXTURE_AUTH=true/,
    );
    expect(parseAuthDriver("fixture", { allowFixtureAuth: true, productionBuild: true })).toBe("fixture");
  });
});
