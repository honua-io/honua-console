import mapItem from "../../fixtures/catalog/proof-source-map.json";
import serviceItem from "../../fixtures/catalog/proof-source-service.json";

import { FixtureCatalogClient, type FixtureCatalogData } from "./catalog-client.js";
import type { ContentItem } from "./content-item.js";

const FIXTURE_ITEMS = [serviceItem, mapItem] as unknown as readonly ContentItem[];

let defaultClient: FixtureCatalogClient | null = null;

export function getDefaultCatalogClient(): FixtureCatalogClient {
  defaultClient ??= new FixtureCatalogClient(buildFixtureCatalogData());
  return defaultClient;
}

function buildFixtureCatalogData(): FixtureCatalogData {
  const items = new Map<string, ContentItem>();
  for (const item of FIXTURE_ITEMS) {
    items.set(item.id, item);
  }
  return { items };
}
