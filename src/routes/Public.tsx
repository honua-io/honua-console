import { CatalogClientProvider } from "../catalog/CatalogContext";
import { getDefaultCatalogClient } from "../catalog/default-client";
import { OpenDataCollectionPage } from "../open-data/OpenDataCollectionPage";

export default function Public(): JSX.Element {
  return (
    <CatalogClientProvider client={getDefaultCatalogClient()}>
      <OpenDataCollectionPage />
    </CatalogClientProvider>
  );
}
