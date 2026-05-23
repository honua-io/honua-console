/**
 * Minimal transitional CatalogClient surface for Honua Console. Studio only
 * needs `getItem(id)` to resolve the proof source, so the Console scaffold
 * ships a fixture-backed client and a stub for the real HTTP path.
 *
 * Will be replaced by the SDK's browser-safe catalog projection once
 * honua-sdk-js#225 lands. See docs/studio/PORT.md.
 */

import { CatalogError, type ContentItem } from "./content-item.js";

export interface CatalogClient {
  getItem(id: string): Promise<ContentItem>;
}

export interface FixtureCatalogData {
  readonly items: ReadonlyMap<string, ContentItem>;
}

export class FixtureCatalogClient implements CatalogClient {
  private readonly items: Map<string, ContentItem>;

  constructor(data: FixtureCatalogData) {
    this.items = new Map(data.items);
  }

  async getItem(idOrSlug: string): Promise<ContentItem> {
    const direct = this.items.get(idOrSlug);
    if (direct) return direct;
    for (const candidate of this.items.values()) {
      if (candidate.slug !== idOrSlug) continue;
      return candidate;
    }
    throw new CatalogError("missing", `no item with id ${idOrSlug}`);
  }
}
