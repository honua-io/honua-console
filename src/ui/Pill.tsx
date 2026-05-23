import type { ReactNode } from "react";

export type PillTone = "neutral" | "info" | "warning" | "danger" | "success" | "muted";

export interface PillProps {
  readonly tone?: PillTone;
  readonly children: ReactNode;
  readonly title?: string;
}

export function Pill({ tone = "neutral", children, title }: PillProps) {
  return (
    <span className={`pill pill--${tone}`} data-tone={tone} title={title}>
      {children}
    </span>
  );
}
