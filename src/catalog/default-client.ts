import appItem from "../../fixtures/catalog/app.json";
import depsFanoutItem from "../../fixtures/catalog/deps-fanout.json";
import documentItem from "../../fixtures/catalog/document.json";
import externalUrlItem from "../../fixtures/catalog/external-url.json";
import layerItem from "../../fixtures/catalog/layer.json";
import mapItem from "../../fixtures/catalog/map.json";
import sceneItem from "../../fixtures/catalog/scene.json";
import serviceHonuaApiItem from "../../fixtures/catalog/service-honua-api.json";
import serviceNoDocsItem from "../../fixtures/catalog/service-no-docs.json";
import serviceItem from "../../fixtures/catalog/service.json";
import tileServiceItem from "../../fixtures/catalog/tile-service.json";
import unauthorizedItem from "../../fixtures/catalog/unauthorized.json";
import unsupportedItem from "../../fixtures/catalog/unsupported.json";
import type { ContentItem } from "../contracts/content-item";
import { FixtureCatalogClient, type FixtureCatalogData } from "./client";

const FIXTURE_ITEMS = [
  serviceItem,
  layerItem,
  tileServiceItem,
  mapItem,
  sceneItem,
  appItem,
  documentItem,
  externalUrlItem,
  depsFanoutItem,
  unsupportedItem,
  unauthorizedItem,
  serviceNoDocsItem,
  serviceHonuaApiItem,
] as readonly ContentItem[];

const UNAUTHORIZED_FIXTURE_IDS = ["01HXY3ZK7N1J2Q9V8M0FQ2PWAN"] as const;
const UNSUPPORTED_FIXTURE_IDS = ["01HXY3ZK7N1J2Q9V8M0FQ2PWAM"] as const;

let defaultClient: FixtureCatalogClient | null = null;

export function getDefaultCatalogClient(): FixtureCatalogClient {
  defaultClient ??= new FixtureCatalogClient(buildFixtureCatalogData());
  return defaultClient;
}

function buildFixtureCatalogData(): FixtureCatalogData {
  const items = new Map<string, ContentItem>();
  const listOrder: string[] = [];
  const unauthorizedIds = new Set<string>(UNAUTHORIZED_FIXTURE_IDS);
  const nonListableIds = new Set<string>([depsFanoutItem.id]);

  for (const item of FIXTURE_ITEMS) {
    items.set(item.id, item);
    if (unauthorizedIds.has(item.id) || nonListableIds.has(item.id)) continue;
    listOrder.push(item.id);
  }

  return {
    items,
    listOrder,
    unauthorizedIds,
    unsupportedIds: new Set(UNSUPPORTED_FIXTURE_IDS),
  };
}
