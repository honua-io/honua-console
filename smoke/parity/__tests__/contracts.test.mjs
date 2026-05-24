import { strict as assert } from "node:assert";
import { describe, test } from "node:test";

import { CONTRACT_VERSIONS, findContract } from "../contracts.mjs";
import { OWNING_LAYER_IDS } from "../owning-layers.mjs";

describe("contracts registry", () => {
  test("every entry declares a valid owning layer", () => {
    for (const entry of CONTRACT_VERSIONS) {
      assert.ok(
        OWNING_LAYER_IDS.includes(entry.owningLayer),
        `${entry.name} declares unknown owningLayer ${entry.owningLayer}`,
      );
    }
  });

  test("every entry names a source repo and explanatory note", () => {
    for (const entry of CONTRACT_VERSIONS) {
      assert.equal(typeof entry.sourceRepo, "string");
      assert.ok(entry.sourceRepo.length > 0);
      assert.equal(typeof entry.note, "string");
      assert.ok(entry.note.length > 0);
    }
  });

  test("contracts called out in honua-console#9 acceptance are all present", () => {
    // The cross-surface chain (publish -> catalog -> Studio -> share/embed)
    // must include at minimum the contracts for: content-item (catalog),
    // publish-handoff (operator publish), webmap-doc (saved map), share-access
    // (share dialog), embed-token (embed surface), generated-app-lifecycle
    // (Studio output), and build-artifact (devops promotion).
    const names = new Set(CONTRACT_VERSIONS.map((c) => c.name));
    for (const required of [
      "content-item",
      "publish-handoff",
      "webmap-doc",
      "share-access",
      "embed-token",
      "generated-app-lifecycle",
      "build-artifact",
    ]) {
      assert.ok(names.has(required), `contracts registry missing ${required}`);
    }
  });

  test("findContract throws for unknown names", () => {
    assert.throws(() => findContract("not-a-contract"), /Unknown contract/);
  });

  test("contract names are unique", () => {
    const names = CONTRACT_VERSIONS.map((c) => c.name);
    assert.equal(names.length, new Set(names).size, "duplicate contract names in registry");
  });
});
