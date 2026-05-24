import { describe, expect, it } from "vitest";
import { sanitizeReturnTo } from "./returnTo.js";

describe("sanitizeReturnTo", () => {
  it("keeps internal absolute paths with search and hash", () => {
    expect(sanitizeReturnTo("/catalog?type=map#saved")).toBe("/catalog?type=map#saved");
  });

  it("falls back for external, protocol-relative, and relative targets", () => {
    expect(sanitizeReturnTo("https://evil.example/catalog")).toBe("/");
    expect(sanitizeReturnTo("//evil.example/catalog")).toBe("/");
    expect(sanitizeReturnTo("catalog")).toBe("/");
  });

  it("blocks auth loop targets", () => {
    expect(sanitizeReturnTo("/auth/signin?returnTo=/catalog")).toBe("/");
    expect(sanitizeReturnTo("/auth/callback?returnTo=/catalog")).toBe("/");
    expect(sanitizeReturnTo("/auth/signed-out")).toBe("/");
  });
});
