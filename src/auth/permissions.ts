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

export function canSeeOperatorLinks(session: Session): boolean {
  if (session.status !== "authenticated") return false;
  return session.scopes.some((s) => OPERATOR_SCOPES.has(s));
}
