import { useState } from "react";

export interface CopyRowProps {
  readonly label: string;
  readonly value: string;
  readonly description?: string;
}

export function CopyRow({ label, value, description }: CopyRowProps) {
  const [status, setStatus] = useState<"idle" | "copied" | "failed">("idle");

  const handleCopy = async () => {
    try {
      const clipboard = navigator.clipboard;
      if (!clipboard) throw new Error("clipboard unavailable");
      await clipboard.writeText(value);
      setStatus("copied");
      setTimeout(() => setStatus("idle"), 1500);
    } catch {
      setStatus("failed");
      setTimeout(() => setStatus("idle"), 2000);
    }
  };

  return (
    <div className="copy-row" data-status={status}>
      <div className="copy-row__heading">
        <span className="copy-row__label">{label}</span>
        {description ? <span className="copy-row__description">{description}</span> : null}
      </div>
      <code className="copy-row__value" title={value}>
        {value}
      </code>
      <button type="button" className="copy-row__button" onClick={handleCopy} aria-label={`Copy ${label}`}>
        {status === "copied" ? "Copied" : status === "failed" ? "Copy failed" : "Copy"}
      </button>
    </div>
  );
}
