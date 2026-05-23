import type { ReactNode } from "react";

import { Forbidden } from "../shell/Forbidden";
import { useSession } from "./SessionContext";
import { canSeeOperatorLinks } from "./permissions";

interface OperatorRouteProps {
  children: ReactNode;
}

/**
 * Inner guard composed by `OperateShell` after the shared shell has rendered.
 * Requires an authenticated session (via the outer ProtectedRoute) and
 * operator/admin scope.
 *
 * Operator gating is one rule — `canSeeOperatorLinks` from
 * `auth/permissions.ts`. New operator-only surfaces must compose this
 * guard rather than inspect scopes inline.
 */
export function OperatorRoute({ children }: OperatorRouteProps): JSX.Element {
  const { session } = useSession();
  if (!canSeeOperatorLinks(session)) {
    return (
      <Forbidden reason="This area is reserved for operator and admin scopes. Ask a workspace operator to grant access, then refresh." />
    );
  }
  return <>{children}</>;
}
