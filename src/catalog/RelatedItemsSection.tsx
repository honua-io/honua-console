import { useMemo } from "react";
import { Link } from "react-router-dom";

import type { ContentItem, ItemType } from "../contracts/content-item.js";
import { TypePill } from "../ui/TypePill.js";
import {
  type RelatedItemReference,
  getRelatedItemReferences,
  relatedItemRelationshipLabel,
} from "./open-data-metadata.js";

export interface RelatedItemsSectionProps {
  readonly item: ContentItem;
  readonly headingId: string;
}

export function RelatedItemsSection({ item, headingId }: RelatedItemsSectionProps): JSX.Element | null {
  const references = useMemo(() => getRelatedItemReferences(item), [item]);

  if (references.length === 0) return null;

  return (
    <section className="item-page__section" aria-labelledby={headingId}>
      <h2 id={headingId}>Related items</h2>
      <ul className="item-page__related-list">
        {references.map((reference) => (
          <li key={`${reference.relationship}-${reference.id}`} className="item-page__related-item">
            <div className="item-page__related-main">
              <span className="item-page__related-label">{relatedItemRelationshipLabel(reference.relationship)}</span>
              <Link to={relatedItemHref(reference)}>{reference.title ?? reference.id}</Link>
              {reference.type ? <TypePill type={reference.type} /> : null}
            </div>
            {reference.note ? <p>{reference.note}</p> : null}
          </li>
        ))}
      </ul>
    </section>
  );
}

function relatedItemHref(reference: RelatedItemReference): string {
  if (reference.type && isPublicDataType(reference.type)) {
    return `/public/items/${encodeURIComponent(reference.id)}`;
  }
  return `/catalog/${encodeURIComponent(reference.id)}`;
}

function isPublicDataType(type: ItemType): boolean {
  return type === "service" || type === "layer" || type === "document";
}
