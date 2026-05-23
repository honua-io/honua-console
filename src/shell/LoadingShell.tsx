import "./shell.css";

interface LoadingShellProps {
  label?: string;
  variant?: "page" | "inline";
}

export function LoadingShell({ label = "Loading", variant = "page" }: LoadingShellProps): JSX.Element {
  return (
    <output className={`hc-loading hc-loading--${variant}`} aria-live="polite">
      <span className="hc-loading__dot" aria-hidden="true" />
      <span className="hc-loading__dot" aria-hidden="true" />
      <span className="hc-loading__dot" aria-hidden="true" />
      <span className="hc-visually-hidden">{label}</span>
    </output>
  );
}
