import { useEffect, useRef, useState } from "react";

import type { ProofChartSpec } from "../proof/proofFixture.js";

interface ChartSpecViewProps {
  /** Optional widget-level chart spec carried on the proof draft. */
  chartSpec?: ProofChartSpec;
  /** Bucketed counts used by the deterministic CSS bar fallback. */
  fallback: Readonly<Record<string, number>>;
  /** Widget title forwarded to the Vega-Lite adapter for a11y context. */
  title: string;
  /** Optional widget id for stable data attributes in evidence selectors. */
  "data-widget-id"?: string;
}

/**
 * Render a chart for a Studio generated-dashboard widget. When a Vega-Lite
 * spec is present on the widget, the adapter mounts vega-embed inside an
 * empty container while the chunk loads, then renders the chart in place;
 * widgets without a Vega-Lite spec — and any vega-embed failure — fall back
 * to the deterministic CSS bar-chart (matches the Portal proof behavior).
 * ADR-0001 names Vega-Lite as the long-term chart spec layer for Console;
 * this is the minimum adapter that lets future dashboards/reports ship richer
 * charts without re-plumbing the widget runtime.
 */
export function ChartSpecView({ chartSpec, fallback, title, ...rest }: ChartSpecViewProps): JSX.Element {
  const containerRef = useRef<HTMLDivElement | null>(null);
  const [renderMode, setRenderMode] = useState<"vega-lite" | "css-bars">(() =>
    chartSpec?.kind === "vega-lite" && chartSpec.vegaLite ? "vega-lite" : "css-bars",
  );

  useEffect(() => {
    if (chartSpec?.kind !== "vega-lite" || !chartSpec.vegaLite) {
      setRenderMode("css-bars");
      return;
    }

    let cancelled = false;
    let view: { finalize: () => void } | undefined;

    const renderSpec = async () => {
      try {
        const { default: vegaEmbed } = await import("vega-embed");
        if (cancelled || !containerRef.current) return;
        const result = await vegaEmbed(containerRef.current, chartSpec.vegaLite as Record<string, unknown>, {
          actions: false,
          renderer: "svg",
        });
        if (cancelled) {
          result.finalize();
          return;
        }
        view = result;
        setRenderMode("vega-lite");
      } catch (error) {
        if (cancelled) return;
        console.warn("Studio chart adapter: vega-embed failed, falling back to CSS bars.", error);
        setRenderMode("css-bars");
      }
    };
    void renderSpec();

    return () => {
      cancelled = true;
      view?.finalize();
    };
  }, [chartSpec]);

  if (renderMode === "vega-lite" && chartSpec?.kind === "vega-lite") {
    return (
      <div
        ref={containerRef}
        className="abp-vega"
        data-chart-spec="vega-lite"
        aria-label={chartSpec.title ?? title}
        role="img"
        {...rest}
      />
    );
  }

  const maxCount = Math.max(1, ...Object.values(fallback));
  return (
    <div className="abp-bars" data-chart-spec="css-bars" {...rest}>
      {Object.entries(fallback).map(([label, count]) => (
        <div key={label} className="abp-bar">
          <span>{label}</span>
          <div>
            <span style={{ width: `${Math.max(8, (count / maxCount) * 100)}%` }} />
          </div>
          <strong>{count}</strong>
        </div>
      ))}
    </div>
  );
}
