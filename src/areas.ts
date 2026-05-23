// Console area registry. The single deployable artifact serves all four
// areas from the same origin (see ADR-0001). This list is referenced by the
// router, the area placeholders, and the deploy-bundle smoke tests so that
// "supported areas" has exactly one source of truth.

export const CONSOLE_AREAS = ["studio", "catalog", "operate", "share"] as const;

export type ConsoleArea = (typeof CONSOLE_AREAS)[number];

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
