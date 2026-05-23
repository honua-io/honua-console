import { strict as assert } from "node:assert";
import { mkdtemp, readFile, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { afterEach, beforeEach, describe, test } from "node:test";

import { buildEvidenceReport, formatTextSummary, writeEvidence } from "../evidence.mjs";
import { runParitySmoke } from "../run.mjs";

let work;

beforeEach(async () => {
  work = await mkdtemp(join(tmpdir(), "console-parity-evidence-"));
});

afterEach(async () => {
  if (work) await rm(work, { recursive: true, force: true });
});

describe("evidence emitter", () => {
  test("writeEvidence produces a JSON file readable as the schema we declare", async () => {
    const { report } = await runParitySmoke({ originUrl: "https://console.smoke.example" });
    const outputPath = join(work, "console-parity.json");
    await writeEvidence({ outputPath, report });
    const onDisk = JSON.parse(await readFile(outputPath, "utf8"));
    assert.equal(onDisk.scenario, "console-parity-publish-to-embed");
    assert.equal(onDisk.result, "ok");
    assert.equal(onDisk.originUrl, "https://console.smoke.example");
    assert.ok(Array.isArray(onDisk.steps));
    assert.ok(Array.isArray(onDisk.contractVersions));
    assert.ok(onDisk.items);
    assert.ok(onDisk.urls);
  });

  test("formatTextSummary lists every step with its owning layer and the result", async () => {
    const { report } = await runParitySmoke({ originUrl: "https://console.smoke.example" });
    const text = formatTextSummary(report);
    assert.match(text, /console-parity-publish-to-embed/);
    assert.match(text, /result: ok/);
    assert.match(text, /server\/catalog-upsert/);
    assert.match(text, /console\/embed-render/);
    assert.match(text, /\[server\]/);
    assert.match(text, /\[devops\]/);
  });

  test("failure report contains owning-layer attribution suitable for CI logs", () => {
    const ctx = { repoRoot: "/repo", originUrl: "https://console.smoke.example", itemIds: {} };
    const steps = [
      {
        id: "server/catalog-upsert",
        owningLayer: "server",
        description: "upsert",
        status: "failed",
        durationMs: 5,
        evidence: null,
        contracts: [],
        error: "boom",
      },
    ];
    const report = buildEvidenceReport({
      scenarioId: "console-parity-publish-to-embed",
      ranAt: "2026-05-23T00:00:00.000Z",
      ctx,
      steps,
      result: "failed",
      failure: { stepId: "server/catalog-upsert", owningLayer: "server", message: "boom" },
    });
    const text = formatTextSummary(report);
    assert.match(text, /failure step: server\/catalog-upsert/);
    assert.match(text, /failure owner: Honua Server/);
    assert.match(text, /honua-server/);
    assert.match(text, /failure message: boom/);
  });
});
