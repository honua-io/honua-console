import type { Scope, Session } from "./types";

const OPERATOR_SCOPES: ReadonlySet<Scope> = new Set(["operator", "admin"]);
const BUILDER_SCOPES: ReadonlySet<Scope> = new Set(["member", "operator", "admin"]);

export function isAuthenticated(session: Session): boolean {
  return session.status === "authenticated";
}

export function hasScope(session: Session, scope: Scope): boolean {
  return session.status === "authenticated" && session.scopes.includes(scope);
}

export function hasAnyScope(session: Session, scopes: ReadonlyArray<Scope>): boolean {
  if (session.status !== "authenticated") return false;
  return session.scopes.some((s) => scopes.includes(s));
}

/**
 * Single seam used by the shell + Operate route guard to decide whether
 * operator-only UI should render. honua-console#3 owns the rule; this file is
 * the only place it is encoded so follow-on tickets compose the helper rather
 * than re-deriving scope inspection.
 */
export function canSeeOperatorLinks(session: Session): boolean {
  if (session.status !== "authenticated") return false;
  return session.scopes.some((s) => OPERATOR_SCOPES.has(s));
}

/**
 * Builder areas (Studio, Catalog, Saved Maps) are visible to any authenticated
 * workspace member. honua-console#3 will refine the precise scope set; this
 * helper keeps the placeholder gate consistent until then.
 */
export function canSeeBuilderLinks(session: Session): boolean {
  if (session.status !== "authenticated") return false;
  return session.scopes.some((s) => BUILDER_SCOPES.has(s));
}
