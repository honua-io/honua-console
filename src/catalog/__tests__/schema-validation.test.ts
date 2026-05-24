import addFormats from "ajv-formats";
import Ajv2020 from "ajv/dist/2020.js";
import { describe, expect, it } from "vitest";

import { CAPABILITIES, ITEM_TYPES, SHARING_LEVELS, SOURCE_KINDS } from "../../contracts/content-item.js";
import { FIXTURE_FILES, readFixture } from "../fixtures.js";

const contentItemSchema = readFixture("../../schemas/content-item-v1.json");
const catalogApiSchema = readFixture("../../schemas/catalog-api-v1.json");
const publishHandoffSchema = readFixture("../../schemas/publish-handoff-v1.json");

function buildAjv(): Ajv2020 {
  const ajv = new Ajv2020({ allErrors: true, strict: false });
  addFormats(ajv);
  ajv.addSchema(contentItemSchema as object);
  ajv.addSchema(catalogApiSchema as object);
  ajv.addSchema(publishHandoffSchema as object);
  return ajv;
}

describe("content-item-v1 schema", () => {
  const ajv = buildAjv();
  const validateItem = ajv.getSchema("https://schemas.honua.io/content-item/v1.1.0/content-item.json");
  if (!validateItem) throw new Error("content-item schema failed to register");

  const itemFixtures = FIXTURE_FILES;

  for (const file of itemFixtures) {
    it(`validates fixture ${file}`, () => {
      const fixture = readFixture(file);
      const ok = validateItem(fixture);
      expect(validateItem.errors ?? []).toEqual([]);
      expect(ok).toBe(true);
    });
  }

  it("rejects an item with mismatched target.type", () => {
    const broken = {
      ...readFixture<Record<string, unknown>>("service.json"),
      target: {
        type: "layer",
        serviceId: "01HXY3ZK7N1J2Q9V8M0FQ2PWAB",
        layerId: 0,
        geometryType: "polygon",
        fieldCount: 1,
      },
    };
    expect(validateItem(broken)).toBe(false);
  });

  it("rejects an item with openData=true and sharing=org", () => {
    const broken = {
      ...readFixture<Record<string, unknown>>("service.json"),
      access: { sharing: "org", embeddable: false, openData: true },
    };
    expect(validateItem(broken)).toBe(false);
  });

  it("rejects an item with a malformed ULID", () => {
    const broken = { ...readFixture<Record<string, unknown>>("service.json"), id: "not-a-ulid" };
    expect(validateItem(broken)).toBe(false);
  });

  it("rejects javascript URL schemes on user-facing URL fields", () => {
    const base = readFixture<Record<string, unknown>>("external-url.json");
    const broken = {
      ...base,
      target: { type: "external-url", url: "javascript:alert(1)" },
    };
    expect(validateItem(broken)).toBe(false);
  });

  it("rejects an item with an out-of-range WGS84 bbox", () => {
    const broken = {
      ...readFixture<Record<string, unknown>>("service.json"),
      extent: { bbox: [999, -999, 1000, 999], crs: "EPSG:4326" },
    };
    expect(validateItem(broken)).toBe(false);
  });
});

describe("catalog-api-v1 schema", () => {
  const ajv = buildAjv();
  const listResponseValidator = ajv.compile({
    $ref: "https://schemas.honua.io/content-item/v1.1.0/catalog-api.json#/$defs/ListItemsResponse",
  });
  const errorEnvelopeValidator = ajv.compile({
    $ref: "https://schemas.honua.io/content-item/v1.1.0/catalog-api.json#/$defs/ErrorEnvelope",
  });

  it("validates the list-response.json fixture", () => {
    const fixture = readFixture("list-response.json");
    const ok = listResponseValidator(fixture);
    expect(listResponseValidator.errors ?? []).toEqual([]);
    expect(ok).toBe(true);
  });

  it("validates an empty list response", () => {
    const fixture = readFixture("empty.json");
    const ok = listResponseValidator(fixture);
    expect(ok).toBe(true);
  });

  it("validates well-formed error envelopes", () => {
    expect(errorEnvelopeValidator({ error: { code: "missing", message: "no" } })).toBe(true);
    expect(errorEnvelopeValidator({ error: { code: "unauthorized", message: "no" } })).toBe(true);
    expect(errorEnvelopeValidator({ error: { code: "unsupported", message: "no" } })).toBe(true);
  });

  it("rejects unknown error codes", () => {
    expect(errorEnvelopeValidator({ error: { code: "boom", message: "x" } })).toBe(false);
  });
});

describe("publish-handoff-v1 schema", () => {
  const ajv = buildAjv();
  const validate = ajv.compile(publishHandoffSchema as object);

  it("validates the publish-handoff.json fixture", () => {
    const ok = validate(readFixture("publish-handoff.json"));
    expect(validate.errors ?? []).toEqual([]);
    expect(ok).toBe(true);
  });

  it("rejects publish handoff missing source.kind", () => {
    const fixture = readFixture<Record<string, unknown>>("publish-handoff.json");
    const broken = { ...fixture, source: { sourceId: null, jobId: null, publishedBy: null } };
    expect(validate(broken)).toBe(false);
  });
});

describe("typescript enum mirrors match the JSON Schema", () => {
  const defs = (contentItemSchema as { $defs: Record<string, { enum?: readonly string[] }> }).$defs;

  function enumOf(name: string): readonly string[] {
    const def = defs[name];
    if (!def?.enum) throw new Error(`schema $defs.${name}.enum is missing`);
    return def.enum;
  }

  it("ITEM_TYPES mirrors content-item-v1.json#/$defs/ItemType", () => {
    expect([...ITEM_TYPES].sort()).toEqual([...enumOf("ItemType")].sort());
  });

  it("CAPABILITIES mirrors content-item-v1.json#/$defs/Capability", () => {
    expect([...CAPABILITIES].sort()).toEqual([...enumOf("Capability")].sort());
  });

  it("SHARING_LEVELS mirrors content-item-v1.json#/$defs/Access.sharing", () => {
    const sharing = (contentItemSchema as { $defs: { Access: { properties: { sharing: { enum: string[] } } } } }).$defs
      .Access.properties.sharing.enum;
    expect([...SHARING_LEVELS].sort()).toEqual([...sharing].sort());
  });

  it("SOURCE_KINDS mirrors content-item-v1.json#/$defs/SourceKind", () => {
    expect([...SOURCE_KINDS].sort()).toEqual([...enumOf("SourceKind")].sort());
  });
});
