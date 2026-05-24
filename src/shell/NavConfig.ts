import { canSeeBuilderLinks, canSeeOperatorLinks, isAuthenticated } from "../auth/permissions";
import type { Session } from "../auth/types";

export type NavSection = "studio" | "catalog" | "operate" | "share";

export interface NavItem {
  /** Stable id used by tests, telemetry, and active-state styling. */
  id: string;
  /** Top-level area the item belongs to. */
  section: NavSection;
  /** Display label rendered in the side nav. */
  label: string;
  /** React Router path. */
  to: string;
  /** Short description used as title attribute / tooltip. */
  description: string;
  /**
   * Visibility predicate. Operator-only items return false for non-operators
   * so they are filtered out at the data layer rather than hidden via CSS.
   */
  visible: (session: Session) => boolean;
}

export interface NavGroup {
  section: NavSection;
  label: string;
  description: string;
  items: ReadonlyArray<NavItem>;
}

const HOME_ITEM: NavItem = {
  id: "home",
  section: "studio",
  label: "Home",
  to: "/",
  description: "Workspace overview and quickstart",
  visible: isAuthenticated,
};

/**
 * Top-level Console navigation, grouped by the four ADR-0001 areas.
 *
 * honua-console#3 owns IA decisions. This list ships flat (one item per area)
 * so follow-on tickets only need to extend `NAV_ITEMS` — not invent grouping.
 */
export const NAV_ITEMS: ReadonlyArray<NavItem> = [
  HOME_ITEM,
  {
    id: "studio",
    section: "studio",
    label: "Studio",
    to: "/studio",
    description: "AI-assisted maps, dashboards, reports, and apps",
    visible: canSeeBuilderLinks,
  },
  {
    id: "catalog",
    section: "catalog",
    label: "Catalog",
    to: "/catalog",
    description: "Browse data, layers, services, and saved content",
    visible: canSeeBuilderLinks,
  },
  {
    id: "operate",
    section: "operate",
    label: "Operate",
    to: "/operate",
    description: "Publish, monitor, and administer the Honua runtime",
    visible: canSeeOperatorLinks,
  },
  {
    id: "share",
    section: "share",
    label: "Share",
    to: "/share",
    description: "Public links, embeds, exports, and open-data pages",
    visible: isAuthenticated,
  },
];

const SECTION_LABELS: Record<NavSection, { label: string; description: string }> = {
  studio: { label: "Studio", description: "Create" },
  catalog: { label: "Catalog", description: "Discover" },
  operate: { label: "Operate", description: "Administer" },
  share: { label: "Share", description: "Publish" },
};

export function visibleNavItems(session: Session): ReadonlyArray<NavItem> {
  return NAV_ITEMS.filter((item) => item.visible(session));
}

export function visibleNavGroups(session: Session): ReadonlyArray<NavGroup> {
  const visible = visibleNavItems(session);
  return (Object.keys(SECTION_LABELS) as NavSection[])
    .map((section) => ({
      section,
      label: SECTION_LABELS[section].label,
      description: SECTION_LABELS[section].description,
      items: visible.filter((item) => item.section === section),
    }))
    .filter((group) => group.items.length > 0);
}

/**
 * Operator/admin link-back items live in the user menu, not the side nav, so
 * they are kept separate. Same predicate model.
 *
 * `open-admin` is the bridge to the legacy Blazor admin during the Operate
 * port (honua-console#6). It will retire once `/operate` ports legacy
 * functionality.
 */
export interface OperatorLink {
  id: string;
  label: string;
  description: string;
  visible: (session: Session) => boolean;
}

export const OPERATOR_LINKS: ReadonlyArray<OperatorLink> = [
  {
    id: "open-admin",
    label: "Open legacy admin",
    description: "Open the transitional admin workspace",
    visible: canSeeOperatorLinks,
  },
];

export function visibleOperatorLinks(session: Session): ReadonlyArray<OperatorLink> {
  return OPERATOR_LINKS.filter((link) => link.visible(session));
}
