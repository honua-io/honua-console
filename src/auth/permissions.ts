import type { Scope, Session } from "./types";

const OPERATOR_SCOPES: ReadonlySet<Scope> = new Set(["operator", "admin"]);

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
 * The single seam used by the shell + admin link-back to decide whether
 * operator-only UI should render. Future tickets must call this helper
 * (or compose it) rather than re-deriving the rule.
 */
export function canSeeOperatorLinks(session: Session): boolean {
  if (session.status !== "authenticated") return false;
  return session.scopes.some((s) => OPERATOR_SCOPES.has(s));
}
