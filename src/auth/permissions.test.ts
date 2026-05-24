import { describe, expect, it } from "vitest";

import { memberSession, operatorSession, unauthenticatedSession } from "../../tests/fixtures";
import { canSeeOperatorLinks, hasAnyScope, hasScope, isAuthenticated } from "./permissions";

describe("permissions", () => {
  it("treats only authenticated sessions as authenticated", () => {
    expect(isAuthenticated(memberSession)).toBe(true);
    expect(isAuthenticated(unauthenticatedSession)).toBe(false);
    expect(isAuthenticated({ status: "loading" })).toBe(false);
    expect(isAuthenticated({ status: "error", message: "boom" })).toBe(false);
  });

  it("hides operator links from members and unauthenticated visitors", () => {
    expect(canSeeOperatorLinks(memberSession)).toBe(false);
    expect(canSeeOperatorLinks(unauthenticatedSession)).toBe(false);
  });

  it("shows operator links to operators and admins", () => {
    expect(canSeeOperatorLinks(operatorSession)).toBe(true);
    expect(
      canSeeOperatorLinks({
        status: "authenticated",
        user: { id: "u", displayName: "U", email: "u@e" },
        workspace: { id: "w", name: "W" },
        scopes: ["admin"],
      }),
    ).toBe(true);
  });

  it("hasScope and hasAnyScope respect the active scope set", () => {
    expect(hasScope(memberSession, "member")).toBe(true);
    expect(hasScope(memberSession, "operator")).toBe(false);
    expect(hasAnyScope(memberSession, ["operator", "admin"])).toBe(false);
    expect(hasAnyScope(operatorSession, ["operator", "admin"])).toBe(true);
  });
});
