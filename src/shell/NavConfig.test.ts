import { matchRoutes } from "react-router-dom";
import { describe, expect, it } from "vitest";

import { CONSOLE_ROUTES } from "../router";
import { NAV_ITEMS } from "./NavConfig";

describe("Console primary navigation invariant", () => {
  // The router declares both real pages, intentional redirects, and area
  // placeholders. Every entry in NAV_ITEMS must resolve to one of these
  // explicit routes — never the wildcard `*` NotFound — or the primary
  // nav would silently advertise a 404 surface to the user.
  const routePatterns = CONSOLE_ROUTES.map((route) => ({ path: route.path }));

  for (const item of NAV_ITEMS) {
    it(`NAV "${item.id}" target ${item.to} matches an explicit CONSOLE_ROUTES entry`, () => {
      const matches = matchRoutes(routePatterns, item.to);
      expect(matches, `${item.to} should match a registered route`).not.toBeNull();
      const matched = matches?.[0]?.route.path;
      expect(matched).not.toBe("*");
      expect(matched).toBe(item.to);
    });
  }
});
