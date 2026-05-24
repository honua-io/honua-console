import { RequireCapability } from "../session/RequireCapability";
import { ResourceState } from "../surfaces/ResourceState";
import { renderSurface } from "../surfaces/render";
import { useContentItemList } from "../features/catalog/useContentItemList";

function CatalogItemsBody(): JSX.Element {
  const surface = useContentItemList();
  return <>{renderSurface(surface, () => <ResourceState kind="empty" />)}</>;
}

export function CatalogItemsPage(): JSX.Element {
  return (
    <RequireCapability of="catalog:read">
      <h1>Catalog content items</h1>
      <CatalogItemsBody />
    </RequireCapability>
  );
}
