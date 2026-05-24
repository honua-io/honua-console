import { canSeeOperatorLinks, isAuthenticated } from "../auth/permissions";
import type { Session } from "../auth/types";

export interface NavItem {
  /** Stable id used by tests and telemetry. */
  id: string;
  /** Display label rendered in the side nav and skip-target list. */
  label: string;
  /** React Router path. */
  to: string;
  /** Short description used as title attribute / tooltip. */
  description: string;
  /**
   * Visibility predicate. Operator-only items return false for non-operators
   * so they are filtered out at the data layer rather than hidden via CSS.
   * Per design constraint: "Operator nav items are filtered out at the nav
   * definition layer, not just hidden via CSS."
   */
  visible: (session: Session) => boolean;
}

/**
 * Top-level Console navigation. Routes are grouped per the Console IA: Home /
 * Catalog / Share. Studio and Operate items land with honua-console#5 and #6;
 * this definition is the single place to adjust ordering, scope, or add new
 * sections.
 */
export const NAV_ITEMS: ReadonlyArray<NavItem> = [
  {
    id: "home",
    label: "Home",
    to: "/",
    description: "Workspace overview and quickstart",
    visible: isAuthenticated,
  },
  {
    id: "catalog",
    label: "Catalog",
    to: "/catalog",
    description: "Browse published Honua content",
    visible: isAuthenticated,
  },
  {
    id: "maps",
    label: "Maps",
    to: "/catalog/maps",
    description: "Saved web maps you can open and edit",
    visible: isAuthenticated,
  },
  {
    id: "data",
    label: "Data",
    to: "/data",
    description: "Datasets, layers, and tables",
    visible: isAuthenticated,
  },
  {
    id: "groups",
    label: "Groups",
    to: "/groups",
    description: "Workspace groups and shared collections",
    visible: isAuthenticated,
  },
  {
    id: "public",
    label: "Public",
    to: "/share/public",
    description: "Open data and public-facing items",
    visible: () => true,
  },
];

export function visibleNavItems(session: Session): ReadonlyArray<NavItem> {
  return NAV_ITEMS.filter((item) => item.visible(session));
}

/**
 * Operator/admin link-back items live in the user menu, not the side nav, so
 * they are kept separate. Same predicate model.
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
    label: "Open admin",
    description: "Switch to the operator workspace",
    visible: canSeeOperatorLinks,
  },
];

export function visibleOperatorLinks(session: Session): ReadonlyArray<OperatorLink> {
  return OPERATOR_LINKS.filter((link) => link.visible(session));
}
