import { canSeeOperatorLinks, isAuthenticated } from "../auth/permissions";
import type { Session } from "../auth/types";

export interface NavItem {
  id: string;
  label: string;
  to: string;
  description: string;
  area: "studio" | "catalog" | "operate" | "share" | "home";
  visible: (session: Session) => boolean;
}

/**
 * Top-level Console navigation aligned with ADR-0001 workflow areas:
 * Studio (builders), Catalog (content), Operate (operators), Share (external).
 * Operate visibility is operator-only; builder areas are member-visible.
 */
export const NAV_ITEMS: ReadonlyArray<NavItem> = [
  {
    id: "home",
    label: "Home",
    to: "/",
    description: "Workspace overview",
    area: "home",
    visible: isAuthenticated,
  },
  {
    id: "studio",
    label: "Studio",
    to: "/studio",
    description: "AI-assisted spatial query, analysis, maps, dashboards, reports, and apps",
    area: "studio",
    visible: isAuthenticated,
  },
  {
    id: "catalog",
    label: "Catalog",
    to: "/catalog",
    description: "Data, layers, services, saved maps, dashboards, reports, generated apps",
    area: "catalog",
    visible: isAuthenticated,
  },
  {
    id: "operate",
    label: "Operate",
    to: "/operate",
    description: "Publishing, jobs, identity, runtime administration",
    area: "operate",
    visible: canSeeOperatorLinks,
  },
  {
    id: "share",
    label: "Share",
    to: "/share",
    description: "Public links, embeds, open data, exports",
    area: "share",
    visible: isAuthenticated,
  },
];

export function visibleNavItems(session: Session): ReadonlyArray<NavItem> {
  return NAV_ITEMS.filter((item) => item.visible(session));
}
