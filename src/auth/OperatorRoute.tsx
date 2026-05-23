import type { ReactNode } from "react";

import { Forbidden } from "../shell/Forbidden";
import { ProtectedRoute } from "./ProtectedRoute";
import { useSession } from "./SessionContext";
import { canSeeOperatorLinks } from "./permissions";

interface OperatorRouteProps {
  children: ReactNode;
}

/**
 * Inner guard composed by `OperateShell` in the router. Requires an
 * authenticated session (via ProtectedRoute) and operator/admin scope.
 *
 * Operator gating is one rule — `canSeeOperatorLinks` from
 * `auth/permissions.ts`. New operator-only surfaces must compose this
 * guard rather than inspect scopes inline.
 */
export function OperatorRoute({ children }: OperatorRouteProps): JSX.Element {
  return (
    <ProtectedRoute>
      <OperatorScopeGate>{children}</OperatorScopeGate>
    </ProtectedRoute>
  );
}

function OperatorScopeGate({ children }: OperatorRouteProps): JSX.Element {
  const { session } = useSession();
  if (!canSeeOperatorLinks(session)) {
    return (
      <Forbidden reason="This area is reserved for operator and admin scopes. Ask a workspace operator to grant access, then refresh." />
    );
  }
  return <>{children}</>;
}
