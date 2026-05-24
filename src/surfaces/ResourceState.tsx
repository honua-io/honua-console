import type { ReactNode } from "react";

import type { LoadSurfaceStatus } from "./LoadSurface";

export type ResourceStateKind = LoadSurfaceStatus | "loading" | "error" | "empty";

export interface ResourceStateProps {
  readonly kind: ResourceStateKind;
  readonly title?: string;
  readonly message?: string;
  readonly reason?: string;
  readonly code?: string;
  readonly waitingFor?: ReadonlyArray<string>;
  readonly actions?: ReactNode;
}

const DEFAULTS: Record<ResourceStateKind, { title: string; message: string }> = {
  ok: { title: "", message: "" },
  loading: { title: "Loading…", message: "" },
  empty: {
    title: "Nothing to show",
    message: "Adjust the search or filters, or try a different sort.",
  },
  missing: {
    title: "Item not found",
    message:
      "The item id or slug you opened is not in the catalog. It may have been deleted or never existed.",
  },
  unauthorized: {
    title: "You don't have access",
    message:
      "Ask the owner to share this item with you, or sign in with an account that has the required role.",
  },
  unsupported: {
    title: "Not viewable in Console yet",
    message:
      "This item exists, but Console does not yet render this service metadata or package binding.",
  },
  "pending-binding": {
    title: "Coming soon",
    message:
      "This surface is waiting on a shared contract publish. The Console placeholder will be replaced when the SDK lands.",
  },
  error: {
    title: "Couldn't load",
    message: "A network or server error stopped the request. Try again, then check status.",
  },
};

/**
 * Shared empty/error surface used by every SDK-backed loader in Console.
 *
 * Catalog, Studio, Share, and Operate all render this component for the four
 * non-`ok` `LoadSurface` states (`missing`, `unauthorized`, `unsupported`,
 * `pending-binding`) plus the local `loading`/`error`/`empty` cases. Keeping
 * a single component is the implementation of the project constraint that
 * Console must have consistent error and empty-state surfaces.
 */
export function ResourceState({
  kind,
  title,
  message,
  reason,
  code,
  waitingFor,
  actions,
}: ResourceStateProps): JSX.Element {
  const fallback = DEFAULTS[kind];
  const resolvedMessage = message ?? reason ?? fallback.message;
  return (
    <section
      className={`resource-state resource-state--${kind}`}
      role={kind === "loading" ? "status" : "region"}
      data-kind={kind}
      data-code={code ?? undefined}
    >
      <h2 className="resource-state__title">{title ?? fallback.title}</h2>
      {resolvedMessage ? <p className="resource-state__message">{resolvedMessage}</p> : null}
      {waitingFor && waitingFor.length > 0 ? (
        <p className="resource-state__waiting" data-waiting-for={waitingFor.join(",")}>
          Waiting on: {waitingFor.join(", ")}
        </p>
      ) : null}
      {actions ? <div className="resource-state__actions">{actions}</div> : null}
    </section>
  );
}

/**
 * Convenience renderer: feed a `LoadSurface` value directly and render the
 * non-`ok` state. `ok` returns `null` so the caller can decide what to render.
 */
export function ResourceStateFor(props: {
  readonly status: LoadSurfaceStatus;
  readonly reason?: string;
  readonly code?: string;
  readonly waitingFor?: ReadonlyArray<string>;
}): JSX.Element | null {
  if (props.status === "ok") return null;
  return (
    <ResourceState
      kind={props.status}
      {...(props.reason !== undefined ? { reason: props.reason } : {})}
      {...(props.code !== undefined ? { code: props.code } : {})}
      {...(props.waitingFor !== undefined ? { waitingFor: props.waitingFor } : {})}
    />
  );
}
