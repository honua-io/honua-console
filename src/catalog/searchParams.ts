/**
 * URL <-> ListItemsRequest mapping. Catalog list state is owned by the URL —
 * back/forward navigation, deep-linking, and shareable searches all work
 * without a separate global store. The user-facing key is `visibility`; the
 * wire enum stays `sharing` to match the contract.
 */

import {
  ITEM_TYPES,
  type ItemType,
  type ListItemsRequest,
  SHARING_LEVELS,
  SORT_OPTIONS,
  type Sharing,
  type SortOption,
} from "../contracts/content-item.js";

export interface CatalogSearchState {
  readonly q: string;
  readonly type: ItemType | null;
  readonly tag: string | null;
  readonly owner: string | null;
  readonly visibility: Sharing | null;
  readonly sort: SortOption;
  readonly cursor: string | null;
}

export const DEFAULT_SEARCH_STATE: CatalogSearchState = {
  q: "",
  type: null,
  tag: null,
  owner: null,
  visibility: null,
  sort: "modified-desc",
  cursor: null,
};

export function readSearchParams(params: URLSearchParams): CatalogSearchState {
  const q = (params.get("q") ?? "").trim();
  return {
    q,
    type: pickEnum(params.get("type"), ITEM_TYPES),
    tag: emptyToNull(params.get("tag")),
    owner: emptyToNull(params.get("owner")),
    visibility: pickEnum(params.get("visibility"), SHARING_LEVELS),
    sort: pickEnum(params.get("sort"), SORT_OPTIONS) ?? (q !== "" ? "relevance" : "modified-desc"),
    cursor: emptyToNull(params.get("cursor")),
  };
}

export function writeSearchParams(state: CatalogSearchState): URLSearchParams {
  const params = new URLSearchParams();
  if (state.q) params.set("q", state.q);
  if (state.type) params.set("type", state.type);
  if (state.tag) params.set("tag", state.tag);
  if (state.owner) params.set("owner", state.owner);
  if (state.visibility) params.set("visibility", state.visibility);
  const defaultSort: SortOption = state.q ? "relevance" : "modified-desc";
  if (state.sort && state.sort !== defaultSort) params.set("sort", state.sort);
  if (state.cursor) params.set("cursor", state.cursor);
  return params;
}

export function toListItemsRequest(state: CatalogSearchState): ListItemsRequest {
  const request: Record<string, unknown> = {};
  if (state.q) request["q"] = state.q;
  if (state.type) request["type"] = state.type;
  if (state.tag) request["tag"] = state.tag;
  if (state.owner) request["owner"] = state.owner;
  if (state.visibility) request["sharing"] = state.visibility;
  if (state.sort) request["sort"] = state.sort;
  if (state.cursor) request["cursor"] = state.cursor;
  return request as ListItemsRequest;
}

function emptyToNull(value: string | null): string | null {
  if (value === null) return null;
  const trimmed = value.trim();
  return trimmed === "" ? null : trimmed;
}

function pickEnum<T extends string>(value: string | null, allowed: readonly T[]): T | null {
  if (value === null) return null;
  return (allowed as readonly string[]).includes(value) ? (value as T) : null;
}
