import { useMemo } from "react";

import { useSession } from "../auth/SessionContext";
import { canSeeOperatorLinks } from "../auth/permissions";

/**
 * Build a fully-qualified URL into the operator admin app for the requested
 * path, but only when the active session has operator/admin scope. Returns
 * `null` for non-operators (and when no admin base URL has been configured),
 * so call sites can render conditionally without re-checking permissions.
 *
 * This is the single seam for portal -> admin contextual links. Subsequent
 * tickets (item detail in #12 in particular) pass a target path; this slice
 * uses the empty path to surface a top-level "Open admin" link in the user
 * menu.
 */
export function useAdminLink(targetPath = ""): string | null {
  const { session } = useSession();
  return useMemo(() => buildAdminLink(session, targetPath), [session, targetPath]);
}

export function buildAdminLink(
  session: import("../auth/types").Session,
  targetPath: string,
  baseUrl: string | undefined = (import.meta.env.VITE_ADMIN_BASE_URL as string | undefined)?.trim() || undefined,
): string | null {
  if (!canSeeOperatorLinks(session)) return null;
  if (!baseUrl) return null;
  const trimmedBase = baseUrl.replace(/\/+$/, "");
  if (!targetPath) return trimmedBase || null;
  const trimmedPath = targetPath.startsWith("/") ? targetPath : `/${targetPath}`;
  return `${trimmedBase}${trimmedPath}`;
}
