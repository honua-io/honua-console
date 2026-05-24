import { describe, expect, it } from "vitest";

import { memberSession, operatorSession, unauthenticatedSession } from "../../../tests/fixtures";
import { ROLE_MATRIX, canInviteRole, canUpdateSharing, resolvePortalItemRole, roleMatrixEntry } from "../rbac.js";

const owner = { id: "user_alex", name: "Alex Lee", kind: "user" as const };
const orgOwner = { id: "org_honua", name: "City of Honua", kind: "org" as const };

describe("portal item RBAC matrix", () => {
  it("finalizes owner, editor, and viewer permissions", () => {
    expect(ROLE_MATRIX.map((entry) => entry.role)).toEqual(["owner", "editor", "viewer"]);
    expect(canUpdateSharing("owner")).toBe(true);
    expect(canUpdateSharing("editor")).toBe(true);
    expect(canUpdateSharing("viewer")).toBe(false);
    expect(canInviteRole("owner", "editor")).toBe(true);
    expect(canInviteRole("editor", "editor")).toBe(false);
    expect(canInviteRole("editor", "viewer")).toBe(true);
    expect(roleMatrixEntry("viewer").permissions.revokeAccess).toBe(false);
  });

  it("resolves item ownership before elevated editor scopes", () => {
    const alexSession = {
      ...operatorSession,
      user: { ...operatorSession.user, id: "user_alex" },
    };
    expect(resolvePortalItemRole(alexSession, { owner })).toBe("owner");
  });

  it("resolves operators and share writers as editors for non-owned items", () => {
    const shareWriter = {
      ...memberSession,
      scopes: [...memberSession.scopes, "share:write"],
    };
    expect(resolvePortalItemRole(operatorSession, { owner: orgOwner })).toBe("editor");
    expect(resolvePortalItemRole(shareWriter, { owner: orgOwner })).toBe("editor");
  });

  it("resolves members and unauthenticated sessions as viewers when they do not own the item", () => {
    expect(resolvePortalItemRole(memberSession, { owner })).toBe("viewer");
    expect(resolvePortalItemRole(unauthenticatedSession, { owner })).toBe("viewer");
  });
});
