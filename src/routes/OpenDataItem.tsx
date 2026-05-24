import { CatalogClientProvider } from "../catalog/CatalogContext";
import { getDefaultCatalogClient } from "../catalog/default-client";
import { OpenDataItemPage } from "../open-data/OpenDataItemPage";

export default function OpenDataItem(): JSX.Element {
  return (
    <CatalogClientProvider client={getDefaultCatalogClient()}>
      <OpenDataItemPage />
    </CatalogClientProvider>
  );
}
