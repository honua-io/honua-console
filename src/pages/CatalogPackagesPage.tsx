import { useControlPlane } from "../shell/ControlPlaneProvider";
import { usePackageList } from "../features/catalog/usePackageList";
import { RequireCapability } from "../session/RequireCapability";
import { renderSurface } from "../surfaces/render";

function CatalogPackagesBody(): JSX.Element {
  const controlPlane = useControlPlane();
  const surface = usePackageList(controlPlane);
  return (
    <>
      {renderSurface(surface, (items) =>
        items.length === 0 ? (
          <p>No map packages published yet.</p>
        ) : (
          <ul>
            {items.map((item) => (
              <li key={item.id}>
                {item.title ?? item.id}
                {item.status ? <span> · {item.status}</span> : null}
              </li>
            ))}
          </ul>
        ),
      )}
    </>
  );
}

export function CatalogPackagesPage(): JSX.Element {
  return (
    <RequireCapability of="map-packages:read">
      <h1>Map packages</h1>
      <CatalogPackagesBody />
    </RequireCapability>
  );
}
