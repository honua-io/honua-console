import { useMemo } from "react";

import { useProvenance, type ProvenanceLoader } from "../features/operate/useProvenance";
import { RequireCapability } from "../session/RequireCapability";
import { renderSurface } from "../surfaces/render";

/**
 * Placeholder loader: the Operate provenance API is server-owned. Until that
 * loader is wired in, this page passes `undefined` so the surface stays in
 * `pending-binding`.
 */
const useNullLoader = (): ProvenanceLoader | undefined => undefined;

function OperateProvenanceBody(): JSX.Element {
  const loader = useNullLoader();
  const surface = useProvenance(useMemo(() => loader, [loader]));
  return (
    <>
      {renderSurface(surface, (records) => (
        <ul>
          {records.map((record, idx) => (
            <li key={`${record.step}-${idx}`}>
              {record.step}
              {record.tool ? <span> · {record.tool}</span> : null}
            </li>
          ))}
        </ul>
      ))}
    </>
  );
}

export function OperateProvenancePage(): JSX.Element {
  return (
    <RequireCapability of="operate:provenance:read">
      <h1>Provenance</h1>
      <OperateProvenanceBody />
    </RequireCapability>
  );
}
