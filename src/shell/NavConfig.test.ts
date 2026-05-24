import { describe, expect, it } from "vitest";

import { memberSession, operatorSession, unauthenticatedSession } from "../../tests/fixtures";
import { visibleNavItems, visibleOperatorLinks } from "./NavConfig";

describe("NavConfig", () => {
  it("exposes the AC1 routes for an authenticated member", () => {
    const ids = visibleNavItems(memberSession).map((item) => item.id);
    expect(ids).toEqual(["home", "catalog", "maps", "data", "groups", "public"]);
  });

  it("only exposes Public to unauthenticated visitors", () => {
    const ids = visibleNavItems(unauthenticatedSession).map((item) => item.id);
    expect(ids).toEqual(["public"]);
  });

  it("filters operator links out for non-operators (AC2)", () => {
    expect(visibleOperatorLinks(memberSession)).toHaveLength(0);
    expect(visibleOperatorLinks(unauthenticatedSession)).toHaveLength(0);
  });

  it("exposes the admin link-back to operators (AC3)", () => {
    const links = visibleOperatorLinks(operatorSession);
    expect(links.map((l) => l.id)).toContain("open-admin");
  });
});
