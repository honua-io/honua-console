import { beforeEach, describe, expect, it } from "vitest";

import { createFixtureDriver } from "./fixtureDriver";
import type { AuthenticatedSession } from "./types";

const seededSession: AuthenticatedSession = {
  status: "authenticated",
  user: {
    id: "u-seeded",
    displayName: "Seeded User",
    email: "seeded@example.test",
  },
  workspace: { id: "w-seeded", name: "Seeded Workspace" },
  scopes: ["member"],
};

describe("createFixtureDriver", () => {
  beforeEach(() => {
    window.sessionStorage.clear();
  });

  it("uses the centralized fake session seed supplied by env loading", async () => {
    const driver = createFixtureDriver({ fakeSessionSeed: JSON.stringify(seededSession) });

    await expect(driver.probe()).resolves.toEqual(seededSession);
  });
});
