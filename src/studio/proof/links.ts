import type { ContentItem, ContentItemSummary } from "../../transitional/content-item.js";

type StudioProofSource = "catalog-item" | "saved-map";

export type StudioProofSourceItem = Pick<ContentItem | ContentItemSummary, "id" | "type">;

export function studioProofHref(item: StudioProofSourceItem): string {
  const source: StudioProofSource = item.type === "map" ? "saved-map" : "catalog-item";
  const params = new URLSearchParams({
    source,
    itemId: item.id,
  });
  return `/studio/proof?${params.toString()}`;
}
