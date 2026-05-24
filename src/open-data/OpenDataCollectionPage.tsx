import { useEffect, useMemo, useRef, useState } from "react";
import { Link, useSearchParams } from "react-router-dom";

import { useCatalogClient } from "../catalog/CatalogContext.js";
import { formatDate } from "../catalog/components/format.js";
import { CatalogError, type ContentItemSummary, type ListItemsResponse } from "../contracts/content-item.js";
import { EmptyState } from "../shell/EmptyState.js";
import { Pill } from "../ui/Pill.js";
import { Thumbnail } from "../ui/Thumbnail.js";
import { TypePill } from "../ui/TypePill.js";
import { formatPublicTypeList, isPublicOpenDataSummary, publicOpenDataPath } from "./public-items.js";

type CollectionState =
  | { kind: "loading" }
  | { kind: "ready"; response: ListItemsResponse }
  | { kind: "error"; error: CatalogError | Error };

export function OpenDataCollectionPage(): JSX.Element {
  const client = useCatalogClient();
  const [searchParams, setSearchParams] = useSearchParams();
  const query = searchParams.get("q")?.trim() ?? "";
  const [draft, setDraft] = useState(query);
  const [state, setState] = useState<CollectionState>({ kind: "loading" });
  const requestSeq = useRef(0);

  useEffect(() => {
    setDraft(query);
  }, [query]);

  useEffect(() => {
    const seq = ++requestSeq.current;
    setState({ kind: "loading" });
    client
      .listItems({
        sharing: "public",
        q: query || undefined,
        sort: query ? "relevance" : "modified-desc",
        limit: 100,
      })
      .then((response) => {
        if (requestSeq.current !== seq) return;
        setState({ kind: "ready", response });
      })
      .catch((error: unknown) => {
        if (requestSeq.current !== seq) return;
        setState({ kind: "error", error: toError(error) });
      });
  }, [client, query]);

  const items = useMemo(
    () => (state.kind === "ready" ? state.response.items.filter(isPublicOpenDataSummary) : []),
    [state],
  );

  const applySearch = (value: string) => {
    const next = value.trim();
    setSearchParams(next ? { q: next } : {}, { replace: true });
  };

  return (
    <main className="public-page" data-testid="public-open-data-page">
      <header className="public-page__header">
        <div>
          <h1 className="public-page__title">Public</h1>
          <p className="public-page__lede">
            Open datasets, services, layers, and documentation shared by the workspace.
          </p>
        </div>
        <p className="public-page__type-note">Showing public open-data {formatPublicTypeList()}.</p>
      </header>

      <form
        className="public-search"
        onSubmit={(event) => {
          event.preventDefault();
          applySearch(draft);
        }}
      >
        <label className="public-search__label" htmlFor="public-search-input">
          Search public data
        </label>
        <div className="public-search__controls">
          <input
            id="public-search-input"
            className="public-search__input"
            type="search"
            value={draft}
            onChange={(event) => setDraft(event.target.value)}
            placeholder="Search by title, tag, or summary"
          />
          <button type="submit" className="public-search__button">
            Search
          </button>
          {query ? (
            <button
              type="button"
              className="public-search__clear"
              onClick={() => {
                setDraft("");
                applySearch("");
              }}
            >
              Clear
            </button>
          ) : null}
        </div>
      </form>

      <section className="public-page__results" aria-live="polite">
        <p className="public-page__count">
          {state.kind === "loading" ? "Loading..." : `${items.length} public item${items.length === 1 ? "" : "s"}`}
        </p>
        {renderResults(state, items)}
      </section>
    </main>
  );
}

function renderResults(state: CollectionState, items: readonly ContentItemSummary[]): JSX.Element {
  if (state.kind === "loading") {
    return <EmptyState title="Loading public data" description="Fetching the public open-data collection." />;
  }

  if (state.kind === "error") {
    return (
      <EmptyState
        tone="warning"
        title="Public data could not load"
        description={state.error.message || "A catalog request failed before the public collection could be loaded."}
      />
    );
  }

  if (items.length === 0) {
    return (
      <EmptyState
        title="No public open-data items"
        description="No public service, layer, or document items match the current search."
      />
    );
  }

  return (
    <ul className="public-grid" data-testid="public-open-data-grid">
      {items.map((item) => (
        <li key={item.id} className="public-grid__cell">
          <OpenDataCard item={item} />
        </li>
      ))}
    </ul>
  );
}

function OpenDataCard({ item }: { item: ContentItemSummary }): JSX.Element {
  const href = publicOpenDataPath(item);
  return (
    <article className="public-card" data-item-id={item.id} data-item-type={item.type}>
      <Link to={href} className="public-card__thumb" aria-label={`Open ${item.title}`}>
        <Thumbnail src={item.preview.thumbnail} alt={item.title} type={item.type} />
      </Link>
      <div className="public-card__body">
        <div className="public-card__pills">
          <TypePill type={item.type} />
          <Pill tone="success">Open data</Pill>
        </div>
        <h2 className="public-card__title">
          <Link to={href}>{item.title}</Link>
        </h2>
        <p className="public-card__summary">{item.summary}</p>
        <dl className="public-card__meta">
          <div>
            <dt>Publisher</dt>
            <dd>{item.owner.name}</dd>
          </div>
          <div>
            <dt>Updated</dt>
            <dd>
              <time dateTime={item.modified}>{formatDate(item.modified)}</time>
            </dd>
          </div>
        </dl>
      </div>
    </article>
  );
}

function toError(error: unknown): CatalogError | Error {
  if (error instanceof CatalogError) return error;
  if (error instanceof Error) return error;
  return new Error(String(error));
}
