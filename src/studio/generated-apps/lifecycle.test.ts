import { describe, expect, it } from "vitest";

import { buildDefaultGeneratedAppLifecycleRecords } from "./default-client.js";
import {
  buildGeneratedAppPreviewUrl,
  publishGeneratedAppItem,
  readGeneratedAppLifecycle,
  rollbackGeneratedAppItem,
} from "./lifecycle.js";
import { GENERATED_APP_EXTENSION, GENERATED_APP_EXTENSION_SCHEMA } from "./types.js";

describe("studio generated-app lifecycle", () => {
  const records = buildDefaultGeneratedAppLifecycleRecords();
  const record = records[0];

  it("materializes a published Console content item with the lifecycle extension", () => {
    expect(record.item.type).toBe("app");
    expect(record.lifecycle.state).toBe("published");
    expect(record.lifecycle.schema).toBe(GENERATED_APP_EXTENSION_SCHEMA);
    expect(record.item.extensions[GENERATED_APP_EXTENSION]).toBeDefined();
    const readBack = readGeneratedAppLifecycle(record.item);
    expect(readBack?.schema).toBe(GENERATED_APP_EXTENSION_SCHEMA);
    expect(readBack?.revisions).toHaveLength(2);
  });

  it("builds preview URLs that target the Console studio route", () => {
    const url = buildGeneratedAppPreviewUrl("https://console.honua.example", "item-abc", "rev-001");
    expect(url).toBe("https://console.honua.example/studio/apps/item-abc/preview?revision=rev-001");
  });

  it("uses the Honua:Console:v1 self-link format on materialized items", () => {
    expect(record.item.endpoints.self.format).toBe("Honua:Console:v1");
  });

  it("rolls back to the previous revision without invoking generation", () => {
    const rolledBack = rollbackGeneratedAppItem(record.item, "rev-001", {
      consoleBaseUrl: "https://console.honua.example",
      actor: "u-member",
      now: "2026-05-08T18:00:00.000Z",
    });
    expect(rolledBack.lifecycle.activeRevisionId).toBe("rev-001");
    expect(rolledBack.lifecycle.revisions).toHaveLength(2);
  });

  it("refuses to publish unsupported lifecycle items", () => {
    const unsupported = {
      ...record.item,
      extensions: {
        ...record.item.extensions,
        [GENERATED_APP_EXTENSION]: {
          ...record.lifecycle,
          state: "unsupported" as const,
          unsupportedReason: "Source format not supported by proof fixture.",
        },
      },
    };
    expect(() =>
      publishGeneratedAppItem(unsupported, {
        consoleBaseUrl: "https://console.honua.example",
        actor: "u-member",
      }),
    ).toThrow(/not supported/);
  });
});
