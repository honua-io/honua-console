import { useEffect, useState } from "react";
import { Link, useParams } from "react-router-dom";

import { EmptyState } from "../shell/EmptyState.js";
import { studioPublishingClient } from "../studio/publishing/fixtureClient.js";
import type { PublishedContentItem, StudioPublishTarget } from "../studio/publishing/types.js";
import { isStudioPublishingError } from "../studio/publishing/types.js";

type Surface =
  | "catalog"
  | "map-preview"
  | "dashboard-preview"
  | "report-preview"
  | "app-preview"
  | "share"
  | "embed"
  | "missing";

interface PublishedItemRoutePageProps {
  readonly surface: Surface;
}

const PREVIEW_SURFACE_TARGETS: Partial<Record<Surface, StudioPublishTarget>> = {
  "map-preview": "map",
  "dashboard-preview": "dashboard",
  "report-preview": "report",
  "app-preview": "app"
};

export function PublishedItemRoutePage({ surface }: PublishedItemRoutePageProps): JSX.Element {
  const { itemId = "" } = useParams();
  const [item, setItem] = useState<PublishedContentItem | null>(null);
  const [error, setError] = useState<{ readonly kind: "missing" | "server"; readonly message: string } | null>(null);

  useEffect(() => {
    if (surface === "missing") return;
    let cancelled = false;
    studioPublishingClient
      .getPublishedItem(itemId)
      .then((published) => {
        if (!cancelled) setItem(published);
      })
      .catch((reason: unknown) => {
        if (cancelled) return;
        setError({
          kind: isStudioPublishingError(reason) && reason.kind === "missing" ? "missing" : "server",
          message: reason instanceof Error ? reason.message : "Published item could not load."
        });
      });
    return () => {
      cancelled = true;
    };
  }, [itemId, surface]);

  if (surface === "missing") {
    return <EmptyState kind="missing" title="Route not found" description="This Console route is not available." />;
  }

  if (error) {
    return <EmptyState kind={error.kind} title="Published item unavailable" description={error.message} />;
  }

  if (!item) {
    return <EmptyState kind="missing" title="Loading published item" description="Resolving active version metadata." />;
  }

  const expectedTarget = PREVIEW_SURFACE_TARGETS[surface];
  if (expectedTarget && item.type !== expectedTarget) {
    return (
      <EmptyState
        kind="unsupported"
        title="Preview route does not match this item"
        description={`Open this ${item.type} item through ${item.routes.preview}.`}
      />
    );
  }

  return (
    <section className="page route-surface" data-testid={`route-${surface}`}>
      <header className="page__header">
        <p className="eyebrow">{surfaceLabel(surface)}</p>
        <h1>{item.title}</h1>
        <p>{item.summary}</p>
      </header>

      <section className="card">
        <h2>{surfaceHeading(surface)}</h2>
        <dl className="summary-list">
          <div>
            <dt>Item id</dt>
            <dd>{item.itemId}</dd>
          </div>
          <div>
            <dt>Version</dt>
            <dd>{item.version.versionId}</dd>
          </div>
          <div>
            <dt>Publication state</dt>
            <dd>{item.publicationState}</dd>
          </div>
          <div>
            <dt>Share</dt>
            <dd>{item.share.visibility}, embed {item.share.embedPolicy}</dd>
          </div>
        </dl>
        {surface === "catalog" ? <CatalogActions item={item} /> : null}
        {surface === "share" ? <ShareSummary item={item} /> : null}
        {surface === "embed" ? <EmbedSummary item={item} /> : null}
      </section>
    </section>
  );
}

function CatalogActions({ item }: { readonly item: PublishedContentItem }): JSX.Element {
  return (
    <nav className="card__actions" aria-label="Catalog item actions">
      <Link className="button button--secondary" to={item.routes.preview}>
        Preview
      </Link>
      <Link className="button button--secondary" to={item.routes.share}>
        Share
      </Link>
      <Link className="button button--secondary" to={item.routes.embed}>
        Embed
      </Link>
      <Link className="button" to={item.routes.editInStudio}>
        Edit in Studio
      </Link>
    </nav>
  );
}

function ShareSummary({ item }: { readonly item: PublishedContentItem }): JSX.Element {
  return (
    <div className="stack" data-testid="share-settings">
      <p>Visibility is represented through the fixture share contract as {item.share.visibility}.</p>
      <p>Public link: {item.share.publicLinkEnabled ? "Enabled" : "Disabled"}</p>
    </div>
  );
}

function EmbedSummary({ item }: { readonly item: PublishedContentItem }): JSX.Element {
  return (
    <div className="stack" data-testid="embed-settings">
      <p>Embed policy: {item.share.embedPolicy}</p>
      <p>Embeddable: {item.share.embedEnabled ? "Enabled" : "Disabled"}</p>
    </div>
  );
}

function surfaceLabel(surface: Surface): string {
  switch (surface) {
    case "catalog":
      return "Catalog detail";
    case "map-preview":
      return "Map preview";
    case "dashboard-preview":
      return "Dashboard preview";
    case "report-preview":
      return "Report preview";
    case "app-preview":
      return "App preview";
    case "share":
      return "Share";
    case "embed":
      return "Embed";
    case "missing":
      return "Missing";
  }
}

function surfaceHeading(surface: Surface): string {
  return surface === "catalog" ? "Published content item" : "Published route";
}
