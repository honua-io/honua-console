import { strict as assert } from "node:assert";
import { describe, test } from "node:test";

import { OWNING_LAYERS, OWNING_LAYER_IDS, resolveOwningLayer } from "../owning-layers.mjs";

describe("owning-layers taxonomy", () => {
  test("matches the AC3 wording on honua-console#9 verbatim", () => {
    // AC3: "Failures identify the owning layer: server, SDK, Console,
    // legacy Admin transition, or devops routing." All five must exist
    // and the smoke driver must reject any other value.
    assert.deepEqual(new Set(OWNING_LAYER_IDS), new Set(["devops", "server", "sdk", "console", "legacy-admin"]));
  });

  test("each descriptor names a source repo so failures route to a single owning team", () => {
    for (const id of OWNING_LAYER_IDS) {
      const d = OWNING_LAYERS[id];
      assert.equal(typeof d.label, "string");
      assert.ok(d.label.length > 0);
      assert.equal(typeof d.repo, "string");
      assert.ok(d.repo.length > 0);
      assert.equal(typeof d.description, "string");
      assert.ok(d.description.length > 0);
    }
  });

  test("resolveOwningLayer throws on unknown ids so a typo cannot silently widen the taxonomy", () => {
    assert.throws(() => resolveOwningLayer("portal"), /Unknown owning layer/);
    assert.throws(() => resolveOwningLayer(""), /Unknown owning layer/);
    assert.throws(() => resolveOwningLayer(undefined), /Unknown owning layer/);
  });

  test("resolveOwningLayer returns the descriptor for every known id", () => {
    for (const id of OWNING_LAYER_IDS) {
      const d = resolveOwningLayer(id);
      assert.equal(d.id, id);
    }
  });
});
