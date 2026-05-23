/**
 * Canonical Console shell surfaces.
 *
 * Follow-on tickets must import these from `@/shell` rather than re-import
 * the underlying files. Adding alternates here is the project-pattern signal
 * that you've found a gap; do not silently fork one of these surfaces.
 */
export { AppShell } from "./AppShell";
export { EmptyState } from "./EmptyState";
export { ErrorBoundary } from "./ErrorBoundary";
export { Forbidden } from "./Forbidden";
export { LoadingShell } from "./LoadingShell";
export { UserMenu } from "./UserMenu";
export {
  NAV_ITEMS,
  OPERATOR_LINKS,
  visibleNavGroups,
  visibleNavItems,
  visibleOperatorLinks,
} from "./NavConfig";
export type { NavGroup, NavItem, NavSection, OperatorLink } from "./NavConfig";
