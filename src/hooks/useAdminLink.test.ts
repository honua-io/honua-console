import { describe, expect, it } from "vitest";

import { memberSession, operatorSession, unauthenticatedSession } from "../../tests/fixtures";
import { buildAdminLink } from "./useAdminLink";

describe("buildAdminLink", () => {
  it("returns null for non-operators (AC2)", () => {
    expect(buildAdminLink(memberSession, "", "https://admin.example")).toBeNull();
    expect(buildAdminLink(unauthenticatedSession, "", "https://admin.example")).toBeNull();
  });

  it("returns the configured base URL for operators when no path is supplied (AC3)", () => {
    expect(buildAdminLink(operatorSession, "", "https://admin.example/")).toBe("https://admin.example");
  });

  it("appends the requested path for operators", () => {
    expect(buildAdminLink(operatorSession, "services/foo", "https://admin.example")).toBe(
      "https://admin.example/services/foo",
    );
    expect(buildAdminLink(operatorSession, "/services/foo", "https://admin.example/")).toBe(
      "https://admin.example/services/foo",
    );
  });

  it("returns null for operators when no admin base URL is configured", () => {
    expect(buildAdminLink(operatorSession, "anything", undefined)).toBeNull();
  });
});
