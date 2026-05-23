// Console area registry. The single deployable artifact serves all four
// areas from the same origin (see ADR-0001). The runtime list lives in
// areas.json so vite.config.ts, scripts/write-build-metadata.mjs, and the
// build-metadata test consume the same source as the React router.

import areasJson from "./areas.json";

export type ConsoleArea = "studio" | "catalog" | "operate" | "share";

export const CONSOLE_AREAS = areasJson as readonly ConsoleArea[];

export interface AreaDescriptor {
  id: ConsoleArea;
  label: string;
  summary: string;
  path: `/${ConsoleArea}`;
}

export const AREA_DESCRIPTORS: Record<ConsoleArea, AreaDescriptor> = {
  studio: {
    id: "studio",
    label: "Studio",
    summary: "AI-assisted spatial query, analysis, maps, dashboards, reports, and apps.",
    path: "/studio",
  },
  catalog: {
    id: "catalog",
    label: "Catalog",
    summary: "Data, layers, services, saved maps, dashboards, reports, generated apps.",
    path: "/catalog",
  },
  operate: {
    id: "operate",
    label: "Operate",
    summary: "Publishing, jobs, identity, connectors, deployment health, runtime admin.",
    path: "/operate",
  },
  share: {
    id: "share",
    label: "Share",
    summary: "Public links, embeds, open-data pages, exports, external publishing.",
    path: "/share",
  },
};
