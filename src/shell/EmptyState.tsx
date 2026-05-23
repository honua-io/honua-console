import type { ReactNode } from "react";

import "./shell.css";

interface EmptyStateProps {
  title: string;
  description: string;
  primaryAction?: ReactNode;
  secondaryAction?: ReactNode;
  tone?: "default" | "warning";
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
