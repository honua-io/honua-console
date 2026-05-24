import type { ReactNode } from "react";

/**
 * Render a value or a consistent "—" placeholder when the value is missing.
 * Avoids "undefined" leaking into the DOM and keeps empty cells visible rather
 * than collapsing into zero-height rows.
 */
export interface EmptyCellProps {
  readonly value: string | number | null | undefined;
  readonly children?: ReactNode;
}

export function EmptyCell({ value, children }: EmptyCellProps) {
  if (value === null || value === undefined || value === "") {
    return (
      <span className="empty-cell" aria-label="not provided">
        —
      </span>
    );
  }
  return <>{children ?? value}</>;
}
