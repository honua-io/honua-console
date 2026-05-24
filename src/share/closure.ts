/**
 * Dependency closure walker. Produces the flattened list of dependent
 * items reachable from a starting item, capped by depth and node count
 * so a malformed graph cannot wedge the share dialog.
 *
 * The walker is iterative (BFS) to keep traversal order stable and to
 * make depth/node caps trivial to enforce. The caps mirror the limits
 * recorded in #10 (`depth ≤ 5`, `nodes ≤ 200`) — keep them in sync if
 * #10 lands tighter limits.
 */

import type { ClosureItem, ClosureLookup, ClosureNode } from "./types.js";

export interface ClosureLimits {
  maxDepth: number;
  maxNodes: number;
}

export const DEFAULT_CLOSURE_LIMITS: ClosureLimits = {
  maxDepth: 5,
  maxNodes: 200,
};

export interface ClosureResult {
  nodes: ClosureNode[];
  truncated: boolean;
}

/**
 * BFS-walk the dependency graph rooted at `rootId`. The root itself is
 * not returned in `nodes` — only its dependencies and their transitive
 * closure. Cycles are handled by a visited-set.
 *
 * If a dep cannot be resolved via `lookup.get`, it is recorded as an
 * `unsupported` node so the dialog still surfaces it (e.g. inline style
 * refs that have no portal item id).
 */
export function getDependencyClosure(
  rootId: string,
  lookup: ClosureLookup,
  limits: ClosureLimits = DEFAULT_CLOSURE_LIMITS,
): ClosureResult {
  const visited = new Set<string>([rootId]);
  const result: ClosureNode[] = [];
  const queue: { id: string; depth: number; from: ClosureItem }[] = [];

  const root = lookup.get(rootId);
  if (!root) return { nodes: [], truncated: false };
  for (const dep of root.dependencies) {
    queue.push({ id: dep.id, depth: 1, from: root });
  }

  let truncated = false;

  while (queue.length > 0) {
    const next = queue.shift();
    if (!next) break;
    const { id, depth, from } = next;

    if (visited.has(id)) continue;
    visited.add(id);

    if (result.length >= limits.maxNodes) {
      truncated = true;
      break;
    }

    const item = lookup.get(id);
    const edge = from.dependencies.find((d) => d.id === id);

    if (!item) {
      result.push({
        id,
        type: edge?.type ?? "layer",
        role: edge?.role ?? "operationalLayer",
        access: "unsupported",
      });
      continue;
    }

    result.push({
      id: item.id,
      type: (edge?.type ?? (item.type === "map" ? "layer" : item.type)) as ClosureNode["type"],
      role: edge?.role ?? "operationalLayer",
      ...(item.title === undefined ? {} : { title: item.title }),
      ...(item.access === undefined ? {} : { access: item.access }),
    });

    if (depth >= limits.maxDepth) continue;
    for (const childDep of item.dependencies) {
      if (!visited.has(childDep.id)) {
        queue.push({ id: childDep.id, depth: depth + 1, from: item });
      }
    }
  }

  return { nodes: result, truncated };
}

/**
 * Convenience: build a `ClosureLookup` from an array of items. Useful
 * for fixtures, tests, and the server parity test.
 */
export function buildLookup(items: readonly ClosureItem[]): ClosureLookup {
  const map = new Map<string, ClosureItem>();
  for (const item of items) {
    map.set(item.id, item);
  }
  return {
    get(id) {
      return map.get(id);
    },
  };
}
