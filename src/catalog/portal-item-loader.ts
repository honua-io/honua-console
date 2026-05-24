/**
 * Portal item loader. The Beta catalog (#12) and admin publish handoff
 * (#11) will replace this with a real fetch against the catalog API; for
 * the viewer MVP slice we only need a way to resolve `?item=<id>` to a
 * `PortalViewerItem`. Returning a typed result (rather than throwing)
 * keeps the missing-item surface symmetrical with future server errors.
 */

import layerItem from "../../fixtures/catalog/layer.json";
import mapItem from "../../fixtures/catalog/map.json";
import serviceItem from "../../fixtures/catalog/service.json";
import tileServiceItem from "../../fixtures/catalog/tile-service.json";
import type { ContentItem } from "../contracts/content-item.js";
import type { PortalViewerItem } from "../viewer/types.js";
import { SAMPLE_PORTAL_ITEM_ID, buildSamplePortalItem } from "./sample-portal-item.js";
import { buildSdkBackedPortalItem } from "./sdk-portal-item.js";

export type PortalItemLoadResult =
  | { status: "ok"; item: PortalViewerItem }
  | { status: "not-found"; itemId: string }
  | { status: "error"; itemId: string; message: string };

export const DEFAULT_PORTAL_ITEM_ID = SAMPLE_PORTAL_ITEM_ID;

const VIEWER_FIXTURE_ITEMS = [serviceItem, layerItem, tileServiceItem, mapItem] as unknown as readonly ContentItem[];

export function loadPortalItem(itemId?: string): PortalItemLoadResult {
  const resolvedId = itemId ?? DEFAULT_PORTAL_ITEM_ID;
  if (resolvedId === SAMPLE_PORTAL_ITEM_ID) {
    return { status: "ok", item: buildSamplePortalItem() };
  }
  const fixtureItem = VIEWER_FIXTURE_ITEMS.find((item) => item.id === resolvedId);
  if (fixtureItem) {
    return { status: "ok", item: buildSdkBackedPortalItem(fixtureItem) ?? buildFixturePortalItem(fixtureItem) };
  }
  return { status: "not-found", itemId: resolvedId };
}

export function listKnownPortalItemIds(): string[] {
  return [SAMPLE_PORTAL_ITEM_ID, ...VIEWER_FIXTURE_ITEMS.map((item) => item.id)];
}

function buildFixturePortalItem(item: ContentItem): PortalViewerItem {
  const sample = buildSamplePortalItem();
  const bounds: [number, number, number, number] | undefined = item.extent
    ? [item.extent.bbox[0], item.extent.bbox[1], item.extent.bbox[2], item.extent.bbox[3]]
    : undefined;
  const center: [number, number] = bounds
    ? [(bounds[0] + bounds[2]) / 2, (bounds[1] + bounds[3]) / 2]
    : sample.initialView.center;

  return {
    ...sample,
    metadata: {
      ...sample.metadata,
      id: item.id,
      title: item.title,
      summary: item.summary,
      description: item.description,
      owner: item.owner.name,
      organization: item.owner.kind === "org" ? item.owner.name : undefined,
      license: item.license.name,
      attribution: item.attribution ?? undefined,
      tags: [...item.tags],
      modified: item.timestamps.modified,
      serviceUrl: serviceUrlFor(item),
      itemUrl: `/catalog/${encodeURIComponent(item.slug ?? item.id)}`,
      coordinateSystem: item.nativeCrs ?? item.extent?.crs ?? sample.metadata.coordinateSystem,
    },
    initialView: {
      ...sample.initialView,
      center,
      ...(bounds ? { bounds } : {}),
    },
  };
}

function serviceUrlFor(item: ContentItem): string | undefined {
  if (item.target.type === "service") return item.target.serviceUrl;
  return (
    item.endpoints.geoservices?.accessURL ?? item.endpoints.ogcFeatures?.accessURL ?? item.endpoints.tiles?.accessURL
  );
}
