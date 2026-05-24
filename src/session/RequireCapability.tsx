import type { ReactNode } from "react";

import { ResourceState } from "../surfaces/ResourceState";
import type { CapabilityName, EntitlementName } from "../sdk/session";
import { useCapability, useEntitlement, useSession } from "./SessionProvider";

export interface RequireCapabilityProps {
  readonly of?: CapabilityName | string;
  readonly entitlement?: EntitlementName | string;
  readonly children: ReactNode;
  readonly fallback?: ReactNode;
}

/**
 * Route / item guard backed by server-authored capability and entitlement
 * bundles. Renders `<ResourceState kind="unauthorized" />` when the gate fails
 * and `<ResourceState kind="loading" />` while the bootstrap is still
 * resolving.
 *
 * There is no Console-local role matrix: gates derive only from the bundle
 * returned by the SessionClient.
 */
export function RequireCapability(props: RequireCapabilityProps): JSX.Element {
  const { status } = useSession();
  const hasCap = useCapability(props.of ?? "");
  const hasEnt = useEntitlement(props.entitlement ?? "");

  if (status.kind === "loading") {
    return <ResourceState kind="loading" />;
  }
  if (status.kind === "error") {
    return <ResourceState kind="unsupported" reason={status.message} />;
  }
  if (status.kind === "anonymous") {
    return <>{props.fallback ?? <ResourceState kind="unauthorized" />}</>;
  }

  const capOk = !props.of || hasCap;
  const entOk = !props.entitlement || hasEnt;
  if (capOk && entOk) {
    return <>{props.children}</>;
  }
  return <>{props.fallback ?? <ResourceState kind="unauthorized" />}</>;
}
