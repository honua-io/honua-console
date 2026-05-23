import { Link } from "react-router-dom";

import { EmptyState } from "../shell/EmptyState";

export type ConsoleArea = "catalog" | "operate" | "share";

interface AreaCopy {
  readonly title: string;
  readonly description: string;
  readonly ticket: { readonly id: string; readonly url: string };
}

const AREA_COPY: Record<ConsoleArea, AreaCopy> = {
  catalog: {
    title: "Catalog ports in a follow-up ticket",
    description:
      "Catalog browsing, viewer, saved maps, share, embed, and open data port from honua-portal once honua-console#4 lands.",
    ticket: { id: "honua-console#4", url: "https://github.com/honua-io/honua-console/issues/4" },
  },
  operate: {
    title: "Operate ports in a follow-up ticket",
    description:
      "Publishing, jobs, identity, and runtime administration land via the legacy Admin transition tracked by honua-console#6.",
    ticket: { id: "honua-console#6", url: "https://github.com/honua-io/honua-console/issues/6" },
  },
  share: {
    title: "Share ports in a follow-up ticket",
    description: "Public links, embeds, open data, and exports port alongside Catalog when honua-console#4 lands.",
    ticket: { id: "honua-console#4", url: "https://github.com/honua-io/honua-console/issues/4" },
  },
};

interface AreaPlaceholderProps {
  readonly area: ConsoleArea;
}

export default function AreaPlaceholder({ area }: AreaPlaceholderProps): JSX.Element {
  const copy = AREA_COPY[area];
  return (
    <div className="hc-page" data-testid={`area-placeholder-${area}`}>
      <EmptyState
        title={copy.title}
        description={copy.description}
        primaryAction={
          <a
            className="hc-btn hc-btn--primary"
            href={copy.ticket.url}
            target="_blank"
            rel="noreferrer"
            data-testid={`area-placeholder-${area}-ticket`}
          >
            Track {copy.ticket.id}
          </a>
        }
        secondaryAction={
          <Link to="/" className="hc-btn">
            Back to home
          </Link>
        }
      />
    </div>
  );
}
