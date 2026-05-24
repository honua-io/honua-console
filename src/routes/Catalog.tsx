import { useSession } from "../auth/SessionContext";
import { CatalogClientProvider } from "../catalog/CatalogContext";
import { CatalogPage } from "../catalog/CatalogPage";
import { getDefaultCatalogClient } from "../catalog/default-client";

export default function Catalog(): JSX.Element {
  const { session } = useSession();
  const currentUser = session.status === "authenticated" ? session.user : null;

  return (
    <CatalogClientProvider client={getDefaultCatalogClient()}>
      <CatalogPage currentUserId={currentUser?.id ?? null} currentUserName={currentUser?.displayName ?? null} />
    </CatalogClientProvider>
  );
}
