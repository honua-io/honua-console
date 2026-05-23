import { useMemo } from "react";

import { useSession } from "../auth/SessionContext";
import { canSeeOperatorLinks } from "../auth/permissions";
import type { Session } from "../auth/types";
import { consoleEnv } from "../env";

/**
 * Build a fully-qualified URL into the legacy admin app for the requested
 * path, but only when the active session has operator/admin scope. Returns
 * `null` for non-operators (and when no admin base URL has been configured),
 * so call sites can render conditionally without re-checking permissions.
 *
 * This is the single seam for Console → legacy Admin contextual links during
 * the transition. honua-console#6 will retire this hook in favour of the
 * unified Operate surface; until then, every contextual link should route
 * through this helper.
 */
export function useAdminLink(targetPath = ""): string | null {
  const { session } = useSession();
  return useMemo(() => buildAdminLink(session, targetPath), [session, targetPath]);
}

export function buildAdminLink(
  session: Session,
  targetPath: string,
  baseUrl: string | undefined = consoleEnv.adminBaseUrl || undefined,
): string | null {
  if (!canSeeOperatorLinks(session)) return null;
  if (!baseUrl) return null;
  const trimmedBase = baseUrl.replace(/\/+$/, "");
  if (!targetPath) return trimmedBase || null;
  const trimmedPath = targetPath.startsWith("/") ? targetPath : `/${targetPath}`;
  return `${trimmedBase}${trimmedPath}`;
}
