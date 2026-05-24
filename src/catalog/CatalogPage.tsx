import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { useSearchParams } from "react-router-dom";

import type {
  ContentItemSummary,
  ItemType,
  ListItemsResponse,
  Sharing,
  SortOption,
} from "../contracts/content-item.js";
import { CatalogError } from "../contracts/content-item.js";
import { EmptyState } from "../ui/EmptyState.js";
import { useCatalogClient } from "./CatalogContext.js";
import { CatalogCard } from "./components/CatalogCard.js";
import { Facets } from "./components/Facets.js";
import { SearchBar } from "./components/SearchBar.js";
import { SortSelector } from "./components/SortSelector.js";
import {
  type CatalogSearchState,
  DEFAULT_SEARCH_STATE,
  readSearchParams,
  toListItemsRequest,
  writeSearchParams,
} from "./searchParams.js";

type LoadState =
  | { kind: "loading" }
  | { kind: "ready"; response: ListItemsResponse }
  | { kind: "loaded-more"; items: readonly ContentItemSummary[]; nextCursor: string | null }
  | { kind: "error"; error: CatalogError | Error };

interface CatalogPageProps {
  currentUserId?: string | null;
  currentUserName?: string | null;
}

export function CatalogPage({ currentUserId = null, currentUserName = null }: CatalogPageProps) {
  const client = useCatalogClient();
  const [searchParams, setSearchParams] = useSearchParams();
  const state = useMemo(() => readSearchParams(searchParams), [searchParams]);

  const [loadState, setLoadState] = useState<LoadState>({ kind: "loading" });
  const [accumulatedItems, setAccumulatedItems] = useState<readonly ContentItemSummary[]>([]);
  const [nextCursor, setNextCursor] = useState<string | null>(null);
  const [loadingMore, setLoadingMore] = useState(false);
  const requestSeq = useRef(0);

  useEffect(() => {
    const seq = ++requestSeq.current;
    setLoadState({ kind: "loading" });
    setAccumulatedItems([]);
    setNextCursor(null);
    setLoadingMore(false);
    const baseRequest = { ...toListItemsRequest(state), cursor: null };
    client
      .listItems(baseRequest)
      .then((response) => {
        if (requestSeq.current !== seq) return;
        setLoadState({ kind: "ready", response });
        setAccumulatedItems(response.items);
        setNextCursor(response.nextCursor);
      })
      .catch((error: unknown) => {
        if (requestSeq.current !== seq) return;
        setLoadState({ kind: "error", error: toError(error) });
      });
  }, [client, state]);

  const replaceState = useCallback(
    (mutator: (prev: CatalogSearchState) => CatalogSearchState) => {
      setSearchParams(
        (current) => {
          const prev = readSearchParams(current);
          const next = { ...mutator(prev), cursor: null };
          return writeSearchParams(next);
        },
        { replace: true },
      );
    },
    [setSearchParams],
  );

  const handleSearch = useCallback(
    (q: string) => {
      replaceState((prev) => ({
        ...prev,
        q,
        sort: q ? "relevance" : prev.sort === "relevance" ? "modified-desc" : prev.sort,
      }));
    },
    [replaceState],
  );

  const handleFacetChange = useCallback(
    (next: {
      type?: ItemType | null;
      tag?: string | null;
      owner?: string | null;
      visibility?: Sharing | null;
    }) => {
      replaceState((prev) => ({
        ...prev,
        type: "type" in next ? (next.type ?? null) : prev.type,
        tag: "tag" in next ? (next.tag ?? null) : prev.tag,
        owner: "owner" in next ? (next.owner ?? null) : prev.owner,
        visibility: "visibility" in next ? (next.visibility ?? null) : prev.visibility,
      }));
    },
    [replaceState],
  );

  const handleSortChange = useCallback(
    (sort: SortOption) => {
      replaceState((prev) => ({ ...prev, sort }));
    },
    [replaceState],
  );

  const handleClearFilters = useCallback(() => {
    setSearchParams(writeSearchParams(DEFAULT_SEARCH_STATE), { replace: true });
  }, [setSearchParams]);

  const handleScopeChange = useCallback(
    (owner: string | null) => {
      replaceState((prev) => ({ ...prev, owner }));
    },
    [replaceState],
  );

  const handleLoadMore = useCallback(async () => {
    if (!nextCursor || loadingMore) return;
    const seq = requestSeq.current;
    setLoadingMore(true);
    try {
      const more = await client.listItems({
        ...toListItemsRequest(state),
        cursor: nextCursor,
      });
      if (requestSeq.current !== seq) return;
      setAccumulatedItems((prev) => [...prev, ...more.items]);
      setNextCursor(more.nextCursor);
      setLoadState({ kind: "loaded-more", items: more.items, nextCursor: more.nextCursor });
    } catch (error) {
      if (requestSeq.current !== seq) return;
      setLoadState({ kind: "error", error: toError(error) });
    } finally {
      if (requestSeq.current === seq) setLoadingMore(false);
    }
  }, [client, nextCursor, loadingMore, state]);

  const items = accumulatedItems;

  return (
    <main className="catalog-page" data-testid="catalog-page">
      <header className="catalog-page__header">
        <h1 className="catalog-page__title">Catalog</h1>
        <p className="catalog-page__lede">
          Browse the published Honua catalog. Search by title, tag, or item type, then open a result to see metadata and
          viewer actions.
        </p>
      </header>

      <div className="catalog-page__toolbar">
        <SearchBar value={state.q} onChange={handleSearch} />
        <SortSelector value={state.sort} onChange={handleSortChange} relevanceEnabled={Boolean(state.q)} />
        <p className="catalog-page__count" aria-live="polite">
          {loadState.kind === "loading" ? "Loading…" : `${items.length} item${items.length === 1 ? "" : "s"}`}
          {hasFilters(state) ? (
            <button type="button" className="catalog-page__clear" onClick={handleClearFilters}>
              Clear filters
            </button>
          ) : null}
        </p>
      </div>
      {currentUserId ? (
        <CatalogScopeControl
          currentUserId={currentUserId}
          currentUserName={currentUserName}
          owner={state.owner}
          onChange={handleScopeChange}
        />
      ) : null}

      <div className="catalog-page__layout">
        <Facets
          items={items}
          selectedType={state.type}
          selectedTag={state.tag}
          selectedOwner={state.owner}
          selectedVisibility={state.visibility}
          onChange={handleFacetChange}
        />

        <section className="catalog-page__results" aria-live="polite">
          {renderResults({ loadState, items, nextCursor, loadingMore, handleLoadMore })}
        </section>
      </div>
    </main>
  );
}

function CatalogScopeControl({
  currentUserId,
  currentUserName,
  owner,
  onChange,
}: {
  currentUserId: string;
  currentUserName: string | null;
  owner: string | null;
  onChange: (owner: string | null) => void;
}) {
  const scope = owner === currentUserId ? "mine" : owner ? "other-owner" : "organization";
  const myContentLabel = currentUserName ? `${currentUserName}'s content` : "My Content";
  const scopeNote =
    scope === "mine" ? myContentLabel : scope === "other-owner" ? "Filtered by owner" : "All visible workspace items";

  return (
    <fieldset className="catalog-page__scope">
      <legend className="hc-visually-hidden">Catalog workspace scope</legend>
      <button
        type="button"
        className="hc-btn"
        data-active={scope === "organization"}
        data-testid="catalog-scope-organization"
        aria-pressed={scope === "organization"}
        onClick={() => onChange(null)}
      >
        Organization
      </button>
      <button
        type="button"
        className="hc-btn"
        data-active={scope === "mine"}
        data-testid="catalog-scope-my-content"
        aria-pressed={scope === "mine"}
        onClick={() => onChange(currentUserId)}
      >
        My Content
      </button>
      <span className="catalog-page__scope-note">{scopeNote}</span>
    </fieldset>
  );
}

function renderResults({
  loadState,
  items,
  nextCursor,
  loadingMore,
  handleLoadMore,
}: {
  loadState: LoadState;
  items: readonly ContentItemSummary[];
  nextCursor: string | null;
  loadingMore: boolean;
  handleLoadMore: () => void;
}) {
  if (loadState.kind === "loading" && items.length === 0) {
    return <EmptyState kind="loading" />;
  }
  if (loadState.kind === "error") {
    const error = loadState.error;
    if (error instanceof CatalogError && error.code === "unauthorized") {
      return <EmptyState kind="unauthorized" message={error.message} />;
    }
    return <EmptyState kind="error" message={error.message} />;
  }
  if (items.length === 0) {
    return <EmptyState kind="empty" />;
  }
  return (
    <>
      <ul className="catalog-page__grid" data-testid="catalog-grid">
        {items.map((item) => (
          <li key={item.id} className="catalog-page__cell">
            <CatalogCard item={item} />
          </li>
        ))}
      </ul>
      {nextCursor ? (
        <div className="catalog-page__pagination">
          <button type="button" className="catalog-page__more" onClick={handleLoadMore} disabled={loadingMore}>
            {loadingMore ? "Loading more…" : "Load more"}
          </button>
        </div>
      ) : null}
    </>
  );
}

function hasFilters(state: CatalogSearchState): boolean {
  return Boolean(state.q || state.type || state.tag || state.owner || state.visibility);
}

function toError(error: unknown): CatalogError | Error {
  if (error instanceof CatalogError) return error;
  if (error instanceof Error) return error;
  return new Error(String(error));
}
