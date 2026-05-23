import { describe, expect, it } from "vitest";

import { visibleNavItems } from "./NavConfig";

describe("visibleNavItems", () => {
  it("keeps Operate hidden from non-operator members", () => {
    const items = visibleNavItems({
      status: "authenticated",
      user: { id: "u-1", displayName: "Builder", email: "builder@example.test" },
      workspace: { id: "w-1", name: "Demo" },
      scopes: ["member"],
    });

    expect(items.map((item) => item.id)).toEqual(["home", "studio", "catalog", "share"]);
  });

  it("shows Operate to operators", () => {
    const items = visibleNavItems({
      status: "authenticated",
      user: { id: "u-2", displayName: "Operator", email: "operator@example.test" },
      workspace: { id: "w-1", name: "Demo" },
      scopes: ["member", "operator"],
    });

    expect(items.map((item) => item.id)).toEqual(["home", "studio", "catalog", "operate", "share"]);
  });
});
