import type { Sharing } from "../transitional/content-item.js";

import { Pill, type PillTone } from "./Pill.js";

const LABELS: Record<Sharing, string> = {
  private: "Private",
  org: "Organization",
  group: "Group",
  "public-link": "Anyone with link",
  public: "Public",
};

const TONES: Record<Sharing, PillTone> = {
  private: "danger",
  org: "info",
  group: "info",
  "public-link": "warning",
  public: "success",
};

export interface VisibilityPillProps {
  readonly sharing: Sharing;
}

export function VisibilityPill({ sharing }: VisibilityPillProps) {
  return (
    <Pill tone={TONES[sharing]} title={`Visibility: ${LABELS[sharing]}`}>
      <span className="pill__dot" aria-hidden="true" />
      {LABELS[sharing]}
    </Pill>
  );
}
