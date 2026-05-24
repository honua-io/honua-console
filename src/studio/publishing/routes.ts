import type { StudioPublishTarget } from "./types.js";

export function publishReviewRoute(draftId: string): string {
  return `/studio/drafts/${encodeURIComponent(draftId)}/publish`;
}

export function studioDraftRoute(draftId: string): string {
  return `/studio/drafts/${encodeURIComponent(draftId)}`;
}

export function studioPreviewRoute(draftId: string): string {
  return `/studio/previews/${encodeURIComponent(draftId)}`;
}

export function catalogItemRoute(itemId: string): string {
  return `/catalog/${encodeURIComponent(itemId)}`;
}

export function shareRoute(itemId: string): string {
  return `/share/${encodeURIComponent(itemId)}`;
}

export function embedRoute(itemId: string): string {
  return `/embed/${encodeURIComponent(itemId)}`;
}

export function editInStudioRoute(itemId: string): string {
  return `/studio/items/${encodeURIComponent(itemId)}/edit`;
}

export function previewRoute(target: StudioPublishTarget, itemId: string): string {
  const encoded = encodeURIComponent(itemId);
  switch (target) {
    case "map":
      return `/maps/${encoded}`;
    case "dashboard":
      return `/dashboards/${encoded}`;
    case "report":
      return `/reports/${encoded}`;
    case "app":
      return `/apps/${encoded}/preview`;
  }
}

export function publishedItemRoutes(target: StudioPublishTarget, itemId: string) {
  const catalog = catalogItemRoute(itemId);
  return {
    canonical: catalog,
    catalog,
    preview: previewRoute(target, itemId),
    share: shareRoute(itemId),
    embed: embedRoute(itemId),
    editInStudio: editInStudioRoute(itemId)
  };
}
