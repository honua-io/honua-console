import { readFileSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { describe, expect, it } from "vitest";
import embedToken from "../../../schemas/embed-token-v1.json" with { type: "json" };
import shareAccess from "../../../schemas/share-access-v1.json" with { type: "json" };
import { SHARING_TIER_ORDER } from "../types.js";

const here = dirname(fileURLToPath(import.meta.url));

describe("share-access-v1 schema parity", () => {
  it("enumerates all five tiers in the same order as SHARING_TIER_ORDER", () => {
    const enumeration = shareAccess.properties.sharing.enum;
    expect(enumeration).toEqual([...SHARING_TIER_ORDER]);
  });

  it("requires sharing and embeddable", () => {
    expect(shareAccess.required).toEqual(["sharing", "embeddable"]);
  });

  it("schema file is the canonical source on disk", () => {
    const onDisk = JSON.parse(readFileSync(resolve(here, "../../../schemas/share-access-v1.json"), "utf8"));
    expect(onDisk.$id).toBe(shareAccess.$id);
  });
});

describe("embed-token-v1 schema parity", () => {
  it("requires the consumer-visible fields the embed page reads", () => {
    expect(embedToken.required).toEqual(["token", "itemId", "expiresAt", "audience", "closure"]);
  });

  it("audience is constrained to the documented labels", () => {
    expect(embedToken.properties.audience.enum).toEqual(["pilot", "internal", "partner"]);
  });
});
