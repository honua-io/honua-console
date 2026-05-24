/**
 * Publish-handoff parity tests. Asserts:
 *
 * 1. The fixture handoff payload validates against `publish-handoff-v1.json`.
 * 2. The portal-side helper that materializes the handoff into a `ContentItem`
 *    (filling in id/slug/timestamps/endpoints.self/source.history) produces
 *    a valid `ContentItem` that the schema also accepts.
 * 3. The summary returned to admin (the API response) is a valid
 *    `ContentItemSummary`.
 *
 * The actual server-side materialization lives in `honua-server`; this helper
 * is the portal's testable proxy that exercises the round-trip without
 * standing up the server.
 */

import addFormats from "ajv-formats";
import Ajv2020 from "ajv/dist/2020.js";
import { describe, expect, it } from "vitest";

import {
  type ContentItem,
  type ContentItemSummary,
  type PublishHandoff,
  type ServiceLink,
  summarize,
} from "../../contracts/content-item.js";
import { readFixture } from "../fixtures.js";

const contentItemSchema = readFixture("../../schemas/content-item-v1.json");
const catalogApiSchema = readFixture("../../schemas/catalog-api-v1.json");
const publishHandoffSchema = readFixture("../../schemas/publish-handoff-v1.json");

const ajv = new Ajv2020({ allErrors: true, strict: false });
addFormats(ajv);
ajv.addSchema(contentItemSchema as object);
ajv.addSchema(catalogApiSchema as object);
const validateHandoff = ajv.compile(publishHandoffSchema as object);
const validateContentItem = ajv.compile(contentItemSchema as object);
const validateSummary = ajv.compile({
  $ref: "https://schemas.honua.io/content-item/v1.1.0/content-item.json#/$defs/ContentItemSummary",
});

interface ServerFillIns {
  readonly id: string;
  readonly slug: string | null;
  readonly portalSelfUrl: string;
  readonly now: string;
}

function materializePublishHandoff(payload: PublishHandoff, serverFillIns: ServerFillIns): ContentItem {
  return {
    id: serverFillIns.id,
    slug: serverFillIns.slug,
    type: payload.type,
    title: payload.title,
    summary: payload.summary,
    description: payload.description ?? "",
    tags: payload.tags ?? [],
    owner: payload.owner,
    timestamps: {
      created: serverFillIns.now,
      modified: serverFillIns.now,
      published: serverFillIns.now,
      refreshed: null,
    },
    extent: payload.extent,
    nativeCrs: payload.nativeCrs,
    license: payload.license,
    attribution: payload.attribution,
    source: {
      kind: payload.source.kind,
      sourceId: payload.source.sourceId,
      jobId: payload.source.jobId,
      publishedBy: payload.source.publishedBy,
      history: [
        {
          at: serverFillIns.now,
          kind: payload.source.kind,
          actor: payload.source.publishedBy ?? "system",
        },
      ],
    },
    target: payload.target,
    endpoints: {
      self: portalLink(serverFillIns.portalSelfUrl),
      geoservices: payload.endpoints.geoservices,
      ogcFeatures: payload.endpoints.ogcFeatures,
      stac: payload.endpoints.stac,
      tiles: payload.endpoints.tiles,
    },
    preview: payload.preview,
    capabilities: payload.capabilities,
    dependencies: payload.dependencies,
    access: payload.access,
    extensions: payload.extensions ?? {},
  };
}

function portalLink(accessURL: string): ServiceLink {
  return {
    accessURL,
    format: "Honua:Portal:v1",
    mediaType: "text/html",
    describedBy: null,
    describedByType: null,
    conformsTo: ["https://schemas.honua.io/content-item/v1"],
  };
}

describe("publish-handoff parity", () => {
  const handoff = readFixture<PublishHandoff>("publish-handoff.json");

  it("the fixture handoff validates against publish-handoff-v1.json", () => {
    const ok = validateHandoff(handoff);
    expect(validateHandoff.errors ?? []).toEqual([]);
    expect(ok).toBe(true);
  });

  it("server materialization produces a valid ContentItem", () => {
    const item = materializePublishHandoff(handoff, {
      id: "01HXY3ZK7N1J2Q9V8M0FQ2PWAB",
      slug: "city-parcels-2026",
      portalSelfUrl: "https://portal.honua.example/items/01HXY3ZK7N1J2Q9V8M0FQ2PWAB",
      now: "2026-05-06T03:00:00Z",
    });
    const ok = validateContentItem(item);
    expect(validateContentItem.errors ?? []).toEqual([]);
    expect(ok).toBe(true);
  });

  it("the API response summary validates against ContentItemSummary", () => {
    const item = materializePublishHandoff(handoff, {
      id: "01HXY3ZK7N1J2Q9V8M0FQ2PWAB",
      slug: "city-parcels-2026",
      portalSelfUrl: "https://portal.honua.example/items/01HXY3ZK7N1J2Q9V8M0FQ2PWAB",
      now: "2026-05-06T03:00:00Z",
    });
    const summaryView: ContentItemSummary = summarize(item);
    const ok = validateSummary(summaryView);
    expect(validateSummary.errors ?? []).toEqual([]);
    expect(ok).toBe(true);
  });

  it("rejects handoff payloads with disallowed source.kind values", () => {
    const broken = { ...handoff, source: { ...handoff.source, kind: "manual" } };
    expect(validateHandoff(broken)).toBe(false);
  });

  it("carries extensions['honua-portal-viewer'] through admin handoff so the WMS unsupported override survives", () => {
    const handoffWithOverride: PublishHandoff = {
      ...handoff,
      extensions: {
        "honua-portal-viewer": {
          supported: false,
          reason: "WMS rendering is not implemented in the portal viewer v1; open in an external WMS client.",
        },
      },
    };
    expect(validateHandoff(handoffWithOverride)).toBe(true);

    const item = materializePublishHandoff(handoffWithOverride, {
      id: "01HXY3ZK7N1J2Q9V8M0FQ2PWAM",
      slug: "legacy-wms-imagery",
      portalSelfUrl: "https://portal.honua.example/items/01HXY3ZK7N1J2Q9V8M0FQ2PWAM",
      now: "2026-05-06T03:00:00Z",
    });
    expect(validateContentItem(item)).toBe(true);
    expect(item.extensions["honua-portal-viewer"]).toMatchObject({ supported: false });

    const summaryView = summarize(item);
    expect(summaryView.viewerSupport).toEqual({
      supported: false,
      reason: handoffWithOverride.extensions!["honua-portal-viewer"]!["reason"],
    });
    expect(validateSummary(summaryView)).toBe(true);
  });

  it("rejects handoff payloads with target.type that disagrees with type", () => {
    const broken = {
      ...handoff,
      type: "layer" as const,
    };
    expect(validateHandoff(broken)).toBe(false);

    const item = materializePublishHandoff(broken as PublishHandoff, {
      id: "01HXY3ZK7N1J2Q9V8M0FQ2PWAB",
      slug: null,
      portalSelfUrl: "https://portal.honua.example/items/01HXY3ZK7N1J2Q9V8M0FQ2PWAB",
      now: "2026-05-06T03:00:00Z",
    });
    expect(validateContentItem(item)).toBe(false);
  });
});
