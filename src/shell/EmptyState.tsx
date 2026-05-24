import type { ReactNode } from "react";

import "./shell.css";

interface EmptyStateProps {
  /** Section heading; use sentence case. */
  title: string;
  /** One short paragraph describing why this is empty and what to do next. */
  description: string;
  /** Primary action (e.g., a link to the catalog). Optional. */
  primaryAction?: ReactNode;
  /** Secondary action; usually a link to docs or a related route. Optional. */
  secondaryAction?: ReactNode;
  /**
   * Tone hint. "default" for an expected empty state; "warning" for a
   * permission/feature gate the user can resolve by talking to an operator.
   */
  tone?: "default" | "warning";
  /** Extra slot, e.g. a feature-coming-soon note. */
  children?: ReactNode;
}

export function EmptyState({
  title,
  description,
  primaryAction,
  secondaryAction,
  tone = "default",
  children,
}: EmptyStateProps): JSX.Element {
  return (
    <section className="hc-empty" data-tone={tone} aria-labelledby="hc-empty-title">
      <div className="hc-empty__inner">
        <h2 id="hc-empty-title" className="hc-empty__title">
          {title}
        </h2>
        <p className="hc-empty__description">{description}</p>
        {(primaryAction || secondaryAction) && (
          <div className="hc-empty__actions">
            {primaryAction}
            {secondaryAction}
          </div>
        )}
        {children && <div className="hc-empty__extra">{children}</div>}
      </div>
    </section>
  );
}
