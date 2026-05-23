import { useMemo } from "react";

import { useSession } from "../auth/SessionContext";
import { GeneratedAppLifecycleClientProvider } from "../studio/generated-apps/GeneratedAppLifecycleContext";
import { GeneratedAppPreviewPage } from "../studio/generated-apps/GeneratedAppPreviewPage";
import { getDefaultGeneratedAppLifecycleClient } from "../studio/generated-apps/default-client";

export default function StudioGeneratedAppPreview(): JSX.Element {
  const { session } = useSession();
  const client = useMemo(() => getDefaultGeneratedAppLifecycleClient(session), [session]);

  return (
    <GeneratedAppLifecycleClientProvider client={client}>
      <GeneratedAppPreviewPage />
    </GeneratedAppLifecycleClientProvider>
  );
}
