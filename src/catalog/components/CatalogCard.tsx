import { Link } from "react-router-dom";
import type { ContentItemSummary } from "../../contracts/content-item.js";
import { Pill } from "../../ui/Pill.js";
import { Thumbnail } from "../../ui/Thumbnail.js";
import { TypePill } from "../../ui/TypePill.js";
import { VisibilityPill } from "../../ui/VisibilityPill.js";
import { getOpenAction } from "../openability.js";
import { formatDate } from "./format.js";

export interface CatalogCardProps {
  readonly item: ContentItemSummary;
}

export function CatalogCard({ item }: CatalogCardProps) {
  const action = getOpenAction(item);
  const detailHref = `/catalog/${encodeURIComponent(item.slug ?? item.id)}`;

  return (
    <article className="catalog-card" data-item-type={item.type} data-item-id={item.id}>
      <Link to={detailHref} className="catalog-card__thumb">
        <Thumbnail src={item.preview.thumbnail} alt={item.title} type={item.type} />
      </Link>
      <div className="catalog-card__body">
        <div className="catalog-card__pills">
          <TypePill type={item.type} />
          <VisibilityPill sharing={item.sharing} />
          {item.openData ? <Pill tone="success">Open data</Pill> : null}
        </div>
        <h3 className="catalog-card__title">
          <Link to={detailHref}>{item.title}</Link>
        </h3>
        <p className="catalog-card__summary">{item.summary}</p>
        <dl className="catalog-card__meta">
          <div>
            <dt>Owner</dt>
            <dd>{item.owner.name}</dd>
          </div>
          <div>
            <dt>Modified</dt>
            <dd>
              <time dateTime={item.modified}>{formatDate(item.modified)}</time>
            </dd>
          </div>
        </dl>
        <div className="catalog-card__action">
          <ActionPill action={action} />
        </div>
      </div>
    </article>
  );
}

function ActionPill({ action }: { action: ReturnType<typeof getOpenAction> }) {
  if (action.kind === "open-in-map") {
    return (
      <Pill tone="info" title={`${action.label} (action)`}>
        {action.label}
      </Pill>
    );
  }
  if (action.kind === "open-external") {
    return (
      <Pill tone="info" title={action.label}>
        {action.label}
      </Pill>
    );
  }
  return (
    <Pill tone="muted" title={action.reason ?? "Unsupported"}>
      {action.label}
    </Pill>
  );
}
