import { useMemo } from "react";

import { useGeneratedAppPreview } from "../features/studio/useGeneratedAppPreview";
import { RequireCapability } from "../session/RequireCapability";
import type {
  HonuaGeneratedAppLoadOptions,
  HonuaGeneratedAppPreviewInput,
} from "../sdk/generated-app";
import { renderSurface } from "../surfaces/render";

/**
 * Studio generated-app preview page. Requires a manifest input and load
 * options to be passed in. The page-level wiring is intentionally minimal —
 * Studio app-builder ports (`honua-console#5`) plug in the manifest source.
 */
function StudioPreviewBody(props: {
  readonly input?: HonuaGeneratedAppPreviewInput;
  readonly options?: HonuaGeneratedAppLoadOptions;
}): JSX.Element {
  const input = useMemo(() => props.input, [props.input]);
  const options = useMemo(() => props.options, [props.options]);
  const surface = useGeneratedAppPreview(input, options);
  return (
    <>
      {renderSurface(surface, (result) => (
        <pre data-result-status={result.status}>{JSON.stringify(result, null, 2)}</pre>
      ))}
    </>
  );
}

export function StudioPreviewPage(): JSX.Element {
  return (
    <RequireCapability of="studio:preview">
      <h1>Studio generated-app preview</h1>
      <p>
        Pass a manifest input + load options from the Studio app-builder port (`honua-console#5`).
        Until that wiring lands, this view stays in <code>pending-binding</code>.
      </p>
      <StudioPreviewBody />
    </RequireCapability>
  );
}
