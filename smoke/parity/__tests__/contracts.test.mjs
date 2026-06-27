import { strict as assert } from "node:assert";
import { describe, test } from "node:test";

import { CONTRACT_VERSIONS, compareServedContractVersions, findContract } from "../contracts.mjs";
import { assertNoContractDrift } from "../adapters/devops.mjs";
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

describe("contract-drift detection (AUD-106)", () => {
  test("is a no-op when version.json serves no contracts block", () => {
    for (const served of [undefined, null, [], "v1", 5]) {
      const summary = compareServedContractVersions(served);
      assert.equal(summary.served, false);
      assert.equal(summary.checked, 0);
      assert.deepEqual(summary.drift, []);
    }
  });

  test("reports no drift when served versions match the registry", () => {
    const served = Object.fromEntries(CONTRACT_VERSIONS.map((c) => [c.name, c.version]));
    const summary = compareServedContractVersions(served);
    assert.equal(summary.served, true);
    assert.equal(summary.checked, CONTRACT_VERSIONS.length);
    assert.deepEqual(summary.drift, []);
    assert.deepEqual(summary.unknown, []);
  });

  test("reports drift when a served version diverges from the registry", () => {
    const entry = CONTRACT_VERSIONS[0];
    const summary = compareServedContractVersions({ [entry.name]: `${entry.version}-drifted` });
    assert.equal(summary.served, true);
    assert.equal(summary.checked, 1);
    assert.equal(summary.drift.length, 1);
    assert.equal(summary.drift[0].name, entry.name);
    assert.equal(summary.drift[0].registryVersion, entry.version);
    assert.equal(summary.drift[0].servedVersion, `${entry.version}-drifted`);
  });

  test("collects unknown served contracts without failing the match", () => {
    const summary = compareServedContractVersions({ "not-a-contract": "v9" });
    assert.equal(summary.served, true);
    assert.equal(summary.checked, 0);
    assert.deepEqual(summary.drift, []);
    assert.equal(summary.unknown.length, 1);
    assert.equal(summary.unknown[0].name, "not-a-contract");
  });

  test("assertNoContractDrift throws on drift and returns the summary otherwise", () => {
    const entry = CONTRACT_VERSIONS[0];
    assert.throws(
      () => assertNoContractDrift({ contracts: { [entry.name]: `${entry.version}-drifted` } }, "test://version.json"),
      /contract-version drift/,
    );
    const ok = assertNoContractDrift({ contracts: { [entry.name]: entry.version } }, "test://version.json");
    assert.equal(ok.served, true);
    assert.equal(ok.drift.length, 0);
    // No contracts block at all -> no-op, no throw.
    assert.equal(assertNoContractDrift({}, "test://version.json").served, false);
  });
});
