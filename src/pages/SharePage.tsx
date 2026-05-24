import { RequireCapability } from "../session/RequireCapability";
import { ResourceState } from "../surfaces/ResourceState";

export function SharePage(): JSX.Element {
  return (
    <RequireCapability of="sharing:read" entitlement="sharing">
      <h1>Sharing policies</h1>
      <p>
        Use the sharing mutation hook (`useShareMutate`) to update a saved map's policy. The detail
        list view will plug in here once `honua-sdk-js#225` publishes the saved-map list projection.
      </p>
      <ResourceState
        kind="pending-binding"
        waitingFor={["honua-sdk-js#225"]}
        message="Sharing list view is wired once the SDK publishes saved-map projections."
      />
    </RequireCapability>
  );
}
