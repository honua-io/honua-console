import {
  getRelatedItemReferences,
  getRevisionContext,
  relatedItemRelationshipLabel,
} from "../catalog/open-data-metadata.js";
import { type ContentItem, type Owner, SERVICE_LINK_SLOTS, type ServiceLink } from "../contracts/content-item.js";
import { safeHttpUrl } from "../security/url.js";

interface JsonLdObject extends Readonly<Record<string, JsonLdValue>> {}
type JsonLdValue = string | number | boolean | null | readonly JsonLdValue[] | JsonLdObject;

export type DatasetJsonLd = JsonLdObject & {
  readonly "@context": "https://schema.org";
  readonly "@type": "Dataset";
};

export function buildDatasetJsonLd(item: ContentItem): DatasetJsonLd | null {
  if (!item.access.openData || item.access.sharing !== "public") return null;

  const distributions = collectDistributions(item);
  const relatedWorks = collectRelatedWorks(item);
  const revision = getRevisionContext(item);
  const dataset: Record<string, JsonLdValue> = {
    "@context": "https://schema.org",
    "@type": "Dataset",
    "@id": item.endpoints.self.accessURL,
    identifier: item.id,
    name: item.title,
    description: item.description || item.summary,
    url: item.endpoints.self.accessURL,
    keywords: item.tags,
    creator: ownerParty(item.owner),
    publisher: ownerParty(item.owner),
    dateCreated: item.timestamps.created,
    dateModified: item.timestamps.modified,
    isAccessibleForFree: true,
    includedInDataCatalog: {
      "@type": "DataCatalog",
      name: "Honua Console",
      url: item.endpoints.self.accessURL,
    },
  };

  if (item.timestamps.published) dataset["datePublished"] = item.timestamps.published;
  if (revision.version) dataset["version"] = revision.version;
  if (revision.changeSummary) dataset["releaseNotes"] = revision.changeSummary;
  const licenseUrl = safeHttpUrl(item.license.url);
  if (licenseUrl) dataset["license"] = licenseUrl;
  else if (item.license.name || item.license.spdx) {
    dataset["license"] = compactObject({
      "@type": "CreativeWork",
      name: item.license.name,
      identifier: item.license.spdx,
    });
  }
  if (item.attribution) dataset["creditText"] = item.attribution;
  if (item.extent) {
    dataset["spatialCoverage"] = {
      "@type": "Place",
      geo: {
        "@type": "GeoShape",
        box: bboxToSchemaBox(item.extent.bbox),
      },
    };
  }
  if (distributions.length > 0) dataset["distribution"] = distributions;
  if (relatedWorks.length > 0) dataset["isRelatedTo"] = relatedWorks;

  return compactObject(dataset) as DatasetJsonLd;
}

export function serializeJsonLd(value: JsonLdObject): string {
  return JSON.stringify(value).replace(/</g, "\\u003c");
}

function ownerParty(owner: Owner): JsonLdObject {
  return {
    "@type": owner.kind === "user" ? "Person" : "Organization",
    identifier: owner.id,
    name: owner.name,
  };
}

function collectDistributions(item: ContentItem): JsonLdObject[] {
  const distributions = SERVICE_LINK_SLOTS.flatMap((slot) => {
    const link = item.endpoints[slot];
    return link ? [serviceLinkDistribution(slotLabel(slot), link)] : [];
  });

  if (item.target.type === "document") {
    const documentUrl = safeHttpUrl(item.target.url);
    if (documentUrl) {
      distributions.push(
        compactObject({
          "@type": "DataDownload",
          name: item.title,
          contentUrl: documentUrl,
          encodingFormat: item.target.mediaType,
        }),
      );
    }
  }

  return distributions;
}

function collectRelatedWorks(item: ContentItem): JsonLdObject[] {
  return getRelatedItemReferences(item).map((reference) =>
    compactObject({
      "@type": "CreativeWork",
      identifier: reference.id,
      name: reference.title,
      about: relatedItemRelationshipLabel(reference.relationship),
    }),
  );
}

function serviceLinkDistribution(name: string, link: ServiceLink): JsonLdObject {
  const accessURL = safeHttpUrl(link.accessURL);
  const describedBy = safeHttpUrl(link.describedBy);

  return compactObject({
    "@type": "DataDownload",
    name,
    contentUrl: accessURL ?? undefined,
    encodingFormat: link.mediaType ?? link.format,
    conformsTo: link.conformsTo.length > 0 ? link.conformsTo : undefined,
    subjectOf: describedBy
      ? compactObject({
          "@type": "CreativeWork",
          url: describedBy,
          encodingFormat: link.describedByType,
        })
      : undefined,
  });
}

function slotLabel(slot: (typeof SERVICE_LINK_SLOTS)[number]): string {
  switch (slot) {
    case "geoservices":
      return "GeoServices endpoint";
    case "ogcFeatures":
      return "OGC API Features endpoint";
    case "stac":
      return "STAC endpoint";
    case "tiles":
      return "Tile endpoint";
  }
}

function bboxToSchemaBox([minLon, minLat, maxLon, maxLat]: readonly [number, number, number, number]): string {
  return `${minLat} ${minLon} ${maxLat} ${maxLon}`;
}

function compactObject(input: Record<string, JsonLdValue | undefined>): JsonLdObject {
  const output: Record<string, JsonLdValue> = {};
  for (const [key, value] of Object.entries(input)) {
    if (value !== undefined && value !== null) output[key] = value;
  }
  return output;
}
