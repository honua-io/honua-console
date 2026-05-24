import type { ReactNode } from "react";

import type { LoadSurface } from "./LoadSurface";
import { ResourceState } from "./ResourceState";

/**
 * Render helper: given a `LoadSurface<T>` and a render callback for the `ok`
 * case, return the right node. Centralizes the four non-`ok` branches so
 * pages don't repeat the same `switch (surface.status)` block.
 */
export function renderSurface<T>(
  surface: LoadSurface<T>,
  renderOk: (value: T) => ReactNode,
): ReactNode {
  switch (surface.status) {
    case "ok":
      return renderOk(surface.value);
    case "missing":
      return <ResourceState kind="missing" />;
    case "unauthorized":
      return <ResourceState kind="unauthorized" />;
    case "unsupported":
      return <ResourceState kind="unsupported" reason={surface.reason} code={surface.code} />;
    case "pending-binding":
      return <ResourceState kind="pending-binding" waitingFor={surface.waitingFor} />;
    default: {
      const exhaustive: never = surface;
      throw new Error(`unhandled LoadSurface status: ${JSON.stringify(exhaustive)}`);
    }
  }
}
