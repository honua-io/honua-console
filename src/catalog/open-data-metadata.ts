import type { ContentItem, HistoryEvent, ItemType } from "../contracts/content-item.js";

export const OPEN_DATA_METADATA_EXTENSION = "honua:openData";

export type RelatedItemRelationship =
  | "derived-from"
  | "supersedes"
  | "superseded-by"
  | "companion-layer"
  | "companion-table"
  | "source-dataset"
  | "related-map"
  | "related-app";

export interface RelatedItemReference {
  readonly id: string;
  readonly type?: ItemType;
  readonly relationship: RelatedItemRelationship;
  readonly title?: string;
  readonly note?: string;
}

export interface RevisionMetadata {
  readonly version?: string;
  readonly sourceVersion?: string;
  readonly updateNotes?: string;
  readonly changeSummary?: string;
}

export interface RevisionContext {
  readonly publishedAt: string | null;
  readonly modifiedAt: string;
  readonly refreshedAt: string | null;
  readonly sourceIdentifier: string | null;
  readonly version: string | null;
  readonly sourceVersion: string | null;
  readonly updateNotes: string | null;
  readonly changeSummary: string | null;
  readonly history: readonly HistoryEvent[];
  readonly supportsFeatureDiffs: false;
}

export function getRelatedItemReferences(item: ContentItem): RelatedItemReference[] {
  const extension = openDataMetadataExtension(item);
  const relatedItems = extension?.["relatedItems"];
  if (!Array.isArray(relatedItems)) return [];
  return relatedItems.flatMap((entry) => {
    if (!isRecord(entry)) return [];
    const id = stringValue(entry["id"]);
    const relationship = relationshipValue(entry["relationship"]);
    if (!id || !relationship) return [];
    const type = itemTypeValue(entry["type"]);
    const title = stringValue(entry["title"]);
    const note = stringValue(entry["note"]);
    return [
      {
        id,
        relationship,
        ...(type ? { type } : {}),
        ...(title ? { title } : {}),
        ...(note ? { note } : {}),
      },
    ];
  });
}

export function relatedItemRelationshipLabel(relationship: RelatedItemRelationship): string {
  switch (relationship) {
    case "derived-from":
      return "Derived from";
    case "supersedes":
      return "Supersedes";
    case "superseded-by":
      return "Superseded by";
    case "companion-layer":
      return "Companion layer";
    case "companion-table":
      return "Companion table";
    case "source-dataset":
      return "Source dataset";
    case "related-map":
      return "Related map";
    case "related-app":
      return "Related app";
  }
}

export function getRevisionContext(item: ContentItem): RevisionContext {
  const extension = openDataMetadataExtension(item);
  const revision = isRecord(extension?.["revision"]) ? extension["revision"] : null;
  return {
    publishedAt: item.timestamps.published,
    modifiedAt: item.timestamps.modified,
    refreshedAt: item.timestamps.refreshed,
    sourceIdentifier: item.source.sourceId,
    version: stringValue(revision?.["version"]),
    sourceVersion: stringValue(revision?.["sourceVersion"]),
    updateNotes: stringValue(revision?.["updateNotes"]),
    changeSummary: stringValue(revision?.["changeSummary"]),
    history: item.source.history,
    supportsFeatureDiffs: false,
  };
}

function openDataMetadataExtension(item: ContentItem): Readonly<Record<string, unknown>> | null {
  const extension = item.extensions[OPEN_DATA_METADATA_EXTENSION];
  return isRecord(extension) ? extension : null;
}

function relationshipValue(value: unknown): RelatedItemRelationship | null {
  if (
    value === "derived-from" ||
    value === "supersedes" ||
    value === "superseded-by" ||
    value === "companion-layer" ||
    value === "companion-table" ||
    value === "source-dataset" ||
    value === "related-map" ||
    value === "related-app"
  ) {
    return value;
  }
  return null;
}

function itemTypeValue(value: unknown): ItemType | null {
  if (
    value === "service" ||
    value === "layer" ||
    value === "map" ||
    value === "scene" ||
    value === "app" ||
    value === "document" ||
    value === "external-url"
  ) {
    return value;
  }
  return null;
}

function stringValue(value: unknown): string | null {
  return typeof value === "string" && value.trim().length > 0 ? value : null;
}

function isRecord(value: unknown): value is Readonly<Record<string, unknown>> {
  return !!value && typeof value === "object" && !Array.isArray(value);
}
