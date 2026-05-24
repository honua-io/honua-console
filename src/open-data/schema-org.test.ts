import { describe, expect, it } from "vitest";

import { readFixture } from "../catalog/fixtures.js";
import type { ContentItem } from "../contracts/content-item.js";
import { buildDatasetJsonLd, serializeJsonLd } from "./schema-org.js";

describe("buildDatasetJsonLd", () => {
  it("projects public open-data content items into Schema.org Dataset JSON-LD", () => {
    const item = readFixture<ContentItem>("service.json");

    const jsonLd = buildDatasetJsonLd(item);

    expect(jsonLd).toMatchObject({
      "@context": "https://schema.org",
      "@type": "Dataset",
      "@id": "https://portal.honua.example/items/01HXY3ZK7N1J2Q9V8M0FQ2PWAB",
      identifier: "01HXY3ZK7N1J2Q9V8M0FQ2PWAB",
      name: "City Parcels 2026",
      description: item.description,
      url: item.endpoints.self.accessURL,
      keywords: ["parcels", "land-use", "assessor"],
      creator: { "@type": "Organization", identifier: "org_honua", name: "City of Honua" },
      publisher: { "@type": "Organization", identifier: "org_honua", name: "City of Honua" },
      dateCreated: "2025-11-04T12:00:00Z",
      dateModified: "2026-04-30T09:11:32Z",
      datePublished: "2026-01-10T17:42:00Z",
      version: "city-parcels-2026.2026-05-06",
      releaseNotes:
        "Updated parcel attributes from the latest assessor extract. No schema change or row-level diff is published in the Beta portal.",
      license: "https://creativecommons.org/licenses/by/4.0/",
      creditText: "City of Honua, Department of Planning",
      isAccessibleForFree: true,
      spatialCoverage: {
        "@type": "Place",
        geo: { "@type": "GeoShape", box: "21.2 -158.3 21.8 -157.6" },
      },
    });
    expect(jsonLd?.isRelatedTo).toEqual(
      expect.arrayContaining([
        expect.objectContaining({
          identifier: "01HXY3ZK7N1J2Q9V8M0FQ2PWAC",
          name: "City Parcels — Active",
          about: "Companion layer",
        }),
      ]),
    );
    expect(jsonLd?.distribution).toEqual([
      {
        "@type": "DataDownload",
        name: "GeoServices endpoint",
        contentUrl: "https://api.honua.example/arcgis/rest/services/city/parcels/FeatureServer",
        encodingFormat: "application/json",
        conformsTo: ["https://developers.arcgis.com/rest/services-reference/feature-service.htm"],
        subjectOf: {
          "@type": "CreativeWork",
          url: "https://api.honua.example/arcgis/rest/services/city/parcels/FeatureServer?f=help",
          encodingFormat: "text/html",
        },
      },
      {
        "@type": "DataDownload",
        name: "OGC API Features endpoint",
        contentUrl: "https://api.honua.example/ogc/features/collections/city-parcels",
        encodingFormat: "application/geo+json",
        conformsTo: [
          "http://www.opengis.net/spec/ogcapi-features-1/1.0/conf/core",
          "http://www.opengis.net/spec/ogcapi-features-1/1.0/conf/oas30",
          "http://www.opengis.net/spec/ogcapi-features-1/1.0/conf/geojson",
        ],
        subjectOf: {
          "@type": "CreativeWork",
          url: "https://api.honua.example/ogc/features/api",
          encodingFormat: "application/vnd.oai.openapi+json;version=3.0",
        },
      },
    ]);
  });

  it("adds a document target as a data download when it is open data", () => {
    const item = readFixture<ContentItem>("document.json");

    const jsonLd = buildDatasetJsonLd(item);

    expect(jsonLd?.distribution).toEqual([
      {
        "@type": "DataDownload",
        name: "City Parcels Data Dictionary (PDF)",
        contentUrl: "https://docs.honua.example/parcels-data-dictionary.pdf",
        encodingFormat: "application/pdf",
      },
    ]);
  });

  it("does not emit Schema.org metadata for non-open-data items", () => {
    const item = readFixture<ContentItem>("map.json");

    expect(buildDatasetJsonLd(item)).toBeNull();
  });

  it("escapes script-closing input during serialization", () => {
    const item = readFixture<ContentItem>("service.json");
    const jsonLd = buildDatasetJsonLd({
      ...item,
      title: "</script><script>alert(1)</script>",
    });

    expect(serializeJsonLd(jsonLd!)).not.toContain("</script>");
    expect(serializeJsonLd(jsonLd!)).toContain("\\u003c/script>");
  });
});
