import { StudioProofPage } from "../studio/proof/StudioProofPage";
import { CatalogClientProvider } from "../transitional/CatalogContext";
import { getDefaultCatalogClient } from "../transitional/default-catalog-client";

import "../studio/proof/proof.css";

export default function StudioProof(): JSX.Element {
  return (
    <CatalogClientProvider client={getDefaultCatalogClient()}>
      <StudioProofPage />
    </CatalogClientProvider>
  );
}
