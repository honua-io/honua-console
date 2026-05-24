import type { ReactNode } from "react";

export type EmptyStateKind = "empty" | "unauthorized" | "unsupported" | "missing" | "error" | "loading";

export interface EmptyStateProps {
  readonly kind: EmptyStateKind;
  readonly title?: string;
  readonly message?: string;
  readonly actions?: ReactNode;
}

const DEFAULTS: Record<EmptyStateKind, { title: string; message: string }> = {
  empty: {
    title: "No matching items",
    message: "Adjust your search, clear a filter, or try a different sort.",
  },
  unauthorized: {
    title: "You don't have access to this item",
    message: "Ask the owner to share it with you, or sign in with an account that has permission.",
  },
  unsupported: {
    title: "Not viewable in the portal",
    message: "This item exists in the catalog but the portal viewer does not yet render its type or protocol.",
  },
  missing: {
    title: "Item not found",
    message: "The item id or slug you opened is not in the catalog. It may have been deleted or never existed.",
  },
  error: {
    title: "Couldn't load the catalog",
    message: "A network or server error stopped the request. Try again, then check status if the problem persists.",
  },
  loading: {
    title: "Loading…",
    message: "",
  },
};

export function EmptyState({ kind, title, message, actions }: EmptyStateProps) {
  const fallback = DEFAULTS[kind];
  return (
    <section className={`empty empty--${kind}`} role={kind === "loading" ? "status" : undefined} data-kind={kind}>
      <h2 className="empty__title">{title ?? fallback.title}</h2>
      {(message ?? fallback.message) ? <p className="empty__message">{message ?? fallback.message}</p> : null}
      {actions ? <div className="empty__actions">{actions}</div> : null}
    </section>
  );
}
