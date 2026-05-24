import { useEffect, useState } from "react";
import { Link, useParams } from "react-router-dom";

import { useCatalogClient } from "../catalog/CatalogContext.js";
import { RelatedItemsSection } from "../catalog/RelatedItemsSection.js";
import { RevisionContextSection } from "../catalog/RevisionContextSection.js";
import { ExtentPreview } from "../catalog/components/ExtentPreview.js";
import { formatDate, paragraphs } from "../catalog/components/format.js";
import { CatalogError, type ContentItem } from "../contracts/content-item.js";
import { buildDatasetJsonLd, serializeJsonLd } from "../open-data/schema-org.js";
import { safeHttpUrl } from "../security/url.js";
import { EmptyState } from "../shell/EmptyState.js";
import { CopyRow } from "../ui/CopyRow.js";
import { EmptyCell } from "../ui/EmptyCell.js";
import { Pill } from "../ui/Pill.js";
import { Thumbnail } from "../ui/Thumbnail.js";
import { TypePill } from "../ui/TypePill.js";
import { collectPublicAccessRows, collectPublicApiExamples, isPublicOpenDataItem } from "./public-items.js";

type DetailState =
  | { kind: "loading" }
  | { kind: "ready"; item: ContentItem }
  | { kind: "not-public" }
  | { kind: "error"; error: CatalogError | Error };

export function OpenDataItemPage(): JSX.Element {
  const { idOrSlug } = useParams<{ idOrSlug: string }>();
  const client = useCatalogClient();
  const [state, setState] = useState<DetailState>({ kind: "loading" });

  useEffect(() => {
    if (!idOrSlug) {
      setState({ kind: "error", error: new CatalogError("missing", "no public item id provided") });
      return;
    }

    let cancelled = false;
    setState({ kind: "loading" });
    client
      .getItem(idOrSlug)
      .then((item) => {
        if (cancelled) return;
        setState(isPublicOpenDataItem(item) ? { kind: "ready", item } : { kind: "not-public" });
      })
      .catch((error: unknown) => {
        if (cancelled) return;
        setState({ kind: "error", error: toError(error) });
      });
    return () => {
      cancelled = true;
    };
  }, [client, idOrSlug]);

  if (state.kind === "loading") {
    return (
      <main className="item-page open-data-item-page" data-testid="open-data-item-page" data-state="loading">
        <BackToPublic />
        <EmptyState title="Loading public item" description="Fetching the public open-data metadata." />
      </main>
    );
  }

  if (state.kind === "not-public") {
    return <NotPublicItem />;
  }

  if (state.kind === "error") {
    if (
      state.error instanceof CatalogError &&
      (state.error.code === "missing" || state.error.code === "unauthorized" || state.error.code === "invalid")
    ) {
      return <NotPublicItem />;
    }
    return (
      <main className="item-page open-data-item-page" data-testid="open-data-item-page" data-state="error">
        <BackToPublic />
        <EmptyState
          tone="warning"
          title="Public item could not load"
          description="A catalog request failed before this public item could be loaded."
        />
      </main>
    );
  }

  return <OpenDataItemView item={state.item} />;
}

function OpenDataItemView({ item }: { item: ContentItem }): JSX.Element {
  const description = paragraphs(item.description);
  const licenseUrl = safeHttpUrl(item.license.url);
  const accessRows = collectPublicAccessRows(item);
  const apiExamples = collectPublicApiExamples(item);
  const datasetJsonLd = buildDatasetJsonLd(item);

  return (
    <main className="item-page open-data-item-page" data-testid="open-data-item-page" data-item-id={item.id}>
      {datasetJsonLd ? (
        <script type="application/ld+json" data-testid="dataset-json-ld">
          {serializeJsonLd(datasetJsonLd)}
        </script>
      ) : null}
      <BackToPublic />

      <header className="item-page__hero">
        <div className="item-page__hero-thumb">
          <Thumbnail src={item.preview.thumbnail ?? item.preview.image} alt={item.title} type={item.type} />
        </div>
        <div className="item-page__hero-body">
          <div className="item-page__pills">
            <TypePill type={item.type} />
            <Pill tone="success">Open data</Pill>
          </div>
          <h1 className="item-page__title">{item.title}</h1>
          <p className="item-page__owner">
            <span>{item.owner.name}</span>
            <span aria-hidden="true"> · </span>
            <span>
              Updated <time dateTime={item.timestamps.modified}>{formatDate(item.timestamps.modified)}</time>
            </span>
          </p>
          <p className="item-page__summary">{item.summary}</p>
        </div>
      </header>

      <section className="item-page__section" aria-labelledby="open-data-description">
        <h2 id="open-data-description">Description</h2>
        {description.length > 0 ? (
          description.map((paragraph) => <p key={paragraph}>{paragraph}</p>)
        ) : (
          <p className="item-page__empty">No description provided.</p>
        )}
      </section>

      {item.extent ? (
        <section className="item-page__section" aria-labelledby="open-data-preview">
          <h2 id="open-data-preview">Preview map</h2>
          <ExtentPreview extent={item.extent} title={`Preview extent of ${item.title}`} />
        </section>
      ) : null}

      <RelatedItemsSection item={item} headingId="open-data-related" />

      <section className="item-page__section" aria-labelledby="open-data-metadata">
        <h2 id="open-data-metadata">Metadata</h2>
        <dl className="item-page__metadata">
          <Row label="Publisher">{item.owner.name}</Row>
          <Row label="Contact">{item.owner.name}</Row>
          <Row label="License">
            {licenseUrl ? (
              <a href={licenseUrl} target="_blank" rel="noreferrer noopener">
                {item.license.name}
                {item.license.spdx ? <span className="item-page__license-spdx"> ({item.license.spdx})</span> : null}
              </a>
            ) : (
              <span>
                {item.license.name}
                {item.license.spdx ? <span className="item-page__license-spdx"> ({item.license.spdx})</span> : null}
              </span>
            )}
          </Row>
          <Row label="Attribution">
            <EmptyCell value={item.attribution}>{item.attribution}</EmptyCell>
          </Row>
          <Row label="Native CRS">
            <EmptyCell value={item.nativeCrs}>{item.nativeCrs}</EmptyCell>
          </Row>
          <Row label="Published">
            <EmptyCell value={item.timestamps.published}>
              {item.timestamps.published ? (
                <time dateTime={item.timestamps.published}>{formatDate(item.timestamps.published)}</time>
              ) : null}
            </EmptyCell>
          </Row>
          <Row label="Refreshed">
            <EmptyCell value={item.timestamps.refreshed}>
              {item.timestamps.refreshed ? (
                <time dateTime={item.timestamps.refreshed}>{formatDate(item.timestamps.refreshed)}</time>
              ) : null}
            </EmptyCell>
          </Row>
          <Row label="Tags">
            {item.tags.length > 0 ? (
              <ul className="item-page__taglist">
                {item.tags.map((tag) => (
                  <li key={tag}>
                    <Pill tone="muted">{tag}</Pill>
                  </li>
                ))}
              </ul>
            ) : (
              <EmptyCell value={null} />
            )}
          </Row>
        </dl>
      </section>

      <RevisionContextSection item={item} headingId="open-data-revisions" />

      <section className="item-page__section" aria-labelledby="open-data-access">
        <h2 id="open-data-access">Downloads and APIs</h2>
        {accessRows.length > 0 ? (
          <div className="item-page__endpoints">
            {accessRows.map((row) => (
              <CopyRow key={row.id} label={row.label} value={row.value} description={row.description} />
            ))}
          </div>
        ) : (
          <p className="item-page__empty">No public download or API endpoint is published for this item.</p>
        )}
      </section>

      <section className="item-page__section" aria-labelledby="open-data-examples">
        <h2 id="open-data-examples">API examples</h2>
        {apiExamples.length > 0 ? (
          <div className="open-data-examples">
            {apiExamples.map((example) => (
              <article key={example.id} className="open-data-example">
                <h3>{example.label}</h3>
                <p>{example.description}</p>
                <CopyRow label={`${example.label} command`} value={example.command} />
              </article>
            ))}
          </div>
        ) : (
          <p className="item-page__empty">No API examples are available for this item.</p>
        )}
      </section>
    </main>
  );
}

function Row({ label, children }: { label: string; children: React.ReactNode }): JSX.Element {
  return (
    <div className="item-page__row">
      <dt>{label}</dt>
      <dd>{children}</dd>
    </div>
  );
}

function BackToPublic(): JSX.Element {
  return (
    <p className="item-page__crumb">
      <Link to="/public">Back to public data</Link>
    </p>
  );
}

function NotPublicItem(): JSX.Element {
  return (
    <main className="item-page open-data-item-page" data-testid="open-data-item-page" data-state="not-public">
      <BackToPublic />
      <EmptyState
        title="Public item not found"
        description="This item is not public open data or does not exist."
        tone="warning"
      />
    </main>
  );
}

function toError(error: unknown): CatalogError | Error {
  if (error instanceof CatalogError) return error;
  if (error instanceof Error) return error;
  return new Error(String(error));
}
