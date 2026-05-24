import { CatalogClientProvider } from "../catalog/CatalogContext";
import { ItemDetailPage } from "../catalog/ItemDetailPage";
import { getDefaultCatalogClient } from "../catalog/default-client";

export default function CatalogItem(): JSX.Element {
  return (
    <CatalogClientProvider client={getDefaultCatalogClient()}>
      <ItemDetailPage />
    </CatalogClientProvider>
  );
}
