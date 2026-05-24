/**
 * Defensive dependency walker for share/embed/open-data review.
 *
 * The portal walker is *defensive UI*. The server is authoritative on
 * permission. Per the contract:
 *
 * - Every `403` becomes `unauthorized` for that dependency — never assumed
 *   safe and never silently dropped.
 * - Every `404` becomes `missing` — surfaced so reviewers see broken refs.
 * - Every item whose `target.type` (or registered viewer support flag) is
 *   not renderable in the portal viewer becomes `unsupported` — surfaced
 *   with an explicit "open in external client" affordance, never silently
 *   dropped.
 *
 * The walker is bounded to the same caps as the server endpoint (depth ≤ 5,
 * total nodes ≤ 200) and reports `truncated` so the share dialog can warn
 * "this share has more dependencies than were displayed." The node cap
 * applies to the **total** of all four output buckets, not just successful
 * nodes — a large broken fan-out cannot grow `missing` / `unauthorized` /
 * `unsupported` past the documented limit.
 */

import {
  CatalogError,
  type ContentItem,
  type Dependency,
  type DependencyNode,
  type GetDependenciesResponse,
  summarize,
} from "../contracts/content-item.js";
import type { CatalogClient } from "./client.js";

export interface DependencyClosureOptions {
  readonly depth?: number;
  readonly limit?: number;
  readonly isUnsupported?: (item: ContentItem) => boolean;
}

const DEFAULT_DEPTH = 3;
const MAX_DEPTH = 5;
const DEFAULT_NODE_LIMIT = 50;
const MAX_NODE_LIMIT = 200;

/**
 * Walk the dependency closure of `root` defensively, fetching each dependent
 * item through the supplied {@link CatalogClient}. Cycles terminate; missing,
 * unauthorized, and unsupported branches are categorized rather than thrown.
 */
export async function getDependencyClosure(
  root: ContentItem,
  client: CatalogClient,
  options: DependencyClosureOptions = {},
): Promise<GetDependenciesResponse> {
  const maxDepth = clamp(options.depth ?? DEFAULT_DEPTH, 1, MAX_DEPTH);
  const maxNodes = clamp(options.limit ?? DEFAULT_NODE_LIMIT, 1, MAX_NODE_LIMIT);
  const isUnsupported = options.isUnsupported ?? isUnsupportedByExtension;

  const visited = new Set<string>([root.id]);
  const queue: Array<{ dependency: Dependency; depth: number }> = root.dependencies.map((d) => ({
    dependency: d,
    depth: 1,
  }));

  const nodes: DependencyNode[] = [];
  const missing: Dependency[] = [];
  const unauthorized: Dependency[] = [];
  const unsupported: Dependency[] = [];
  let truncated = false;
  let processed = 0;

  while (queue.length > 0) {
    const head = queue.shift();
    if (!head) break;
    const { dependency, depth } = head;
    if (visited.has(dependency.id)) continue;
    visited.add(dependency.id);

    let item: ContentItem | null = null;
    try {
      item = await client.getItem(dependency.id);
    } catch (err) {
      if (!(err instanceof CatalogError)) throw err;
      if (err.code === "missing") missing.push(dependency);
      else if (err.code === "unauthorized") unauthorized.push(dependency);
      else if (err.code === "unsupported") unsupported.push(dependency);
      else throw err;
    }

    if (item && isUnsupported(item)) {
      unsupported.push(dependency);
      item = null;
    }

    if (item) {
      nodes.push({
        id: dependency.id,
        type: dependency.type,
        role: dependency.role,
        depth,
        summary: summarize(item),
      });
    }

    processed += 1;

    if (item) {
      if (depth < maxDepth) {
        for (const child of item.dependencies) {
          queue.push({ dependency: child, depth: depth + 1 });
        }
      } else if (item.dependencies.length > 0) {
        truncated = true;
      }
    }

    if (processed >= maxNodes) {
      if (queue.length > 0) truncated = true;
      break;
    }
  }

  return { nodes, missing, unauthorized, unsupported, truncated };
}

function clamp(value: number, min: number, max: number): number {
  if (!Number.isFinite(value)) return min;
  return Math.max(min, Math.min(max, Math.trunc(value)));
}

/**
 * Default predicate: an item is `unsupported` for the portal viewer when its
 * `extensions["honua-portal-viewer"].supported === false`. Consumers can
 * compose this with stricter checks (e.g. fixture-driven id sets or
 * capability-based gating).
 */
export function isUnsupportedByExtension(item: ContentItem): boolean {
  const ext = item.extensions["honua-portal-viewer"];
  if (!ext) return false;
  return ext["supported"] === false;
}
