import { useEffect } from "react";

import { RequireCapability } from "../session/RequireCapability";
import { ResourceState } from "../surfaces/ResourceState";
import { emitPendingBindingSmoke } from "../telemetry/smoke";

const SHARE_PENDING_WAITING: ReadonlyArray<string> = Object.freeze(["honua-sdk-js#225"]);

function SharePendingBinding(): JSX.Element {
  useEffect(() => {
    emitPendingBindingSmoke({
      surface: "share.policy.load",
      sdkSubpath: "control-plane",
      waitingFor: SHARE_PENDING_WAITING,
    });
  }, []);

  return (
    <ResourceState
      kind="pending-binding"
      waitingFor={SHARE_PENDING_WAITING}
      message="Sharing list view is wired once the SDK publishes saved-map projections."
    />
  );
}

export function SharePage(): JSX.Element {
  return (
    <RequireCapability of="sharing:read">
      <h1>Sharing policies</h1>
      <p>
        Use the sharing mutation hook (`useShareMutate`) to update a saved map's policy. The detail
        list view will plug in here once `honua-sdk-js#225` publishes the saved-map list projection.
      </p>
      <SharePendingBinding />
    </RequireCapability>
  );
}
