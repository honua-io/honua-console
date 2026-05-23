import type { JobProgress } from "@honua/sdk-js";
import type { AppPackage, BuilderIntent, BuilderPlan, OperatorTelemetry, PreviewHandle } from "@honua/sdk-js/operator";
import { OperatorWorkspace } from "@honua/sdk-js/operator";
import { useCallback, useEffect, useMemo, useState } from "react";
import type { CSSProperties, FormEvent } from "react";
import { Link, useSearchParams } from "react-router-dom";

import { EmptyState } from "../../shell/EmptyState.js";
import { Forbidden } from "../../shell/Forbidden.js";
import { useCatalogClient } from "../../transitional/CatalogContext.js";
import { CatalogError, type ContentItem } from "../../transitional/content-item.js";
import { ChartSpecView } from "../charts/ChartSpecView.js";
import {
  APP_BUILDER_PROOF_FIXTURES,
  APP_BUILDER_PROOF_PROMPT,
  type AppBuilderProofFixture,
  DEFAULT_APP_BUILDER_PROOF_FIXTURE,
  PROOF_THEME_TOKENS,
  PROOF_WIDGET_REGIONS,
  type ProofChartSpec,
  type ProofDraftSpec,
  type ProofDraftWidget,
  type ProofIncidentRow,
  type ProofPlanReview,
  type ProofPreviewMetadata,
  type ProofSource,
  type ProofThemeToken,
  type ProofWarning,
  type ProofWidgetRegion,
  createProofOperatorClient,
  fixtureLabel,
  isBlockingProofFixture,
  normalizeAppBuilderProofFixture,
  readProofPlanReview,
  readProofPreviewMetadata,
} from "./proofFixture.js";
import { emitStudioProofTelemetry } from "./telemetry.js";

const DEFAULT_SOURCE_ITEM_ID = "01HXY3ZK7N1J2Q9V8M0FQ2PWAD";
const PROOF_EDITOR_STORAGE_PREFIX = "honua.console.studio.proof.";
const DEFAULT_PROOF_DESCRIPTION = "Live operations summary generated from the selected catalog source.";
const DEFAULT_PROOF_REFRESH_SECONDS = 60;
const REFRESH_INTERVAL_OPTIONS = [30, 60, 300, 900] as const;

type WidgetKind = ProofDraftWidget["kind"];

interface BindingOption {
  readonly value: string;
  readonly label: string;
}

type EditableProofWidget = ProofDraftWidget & {
  readonly visible: boolean;
  readonly region: ProofWidgetRegion;
};

type EditableProofMetadata = Omit<
  ProofPreviewMetadata,
  "description" | "widgets" | "theme" | "filterDefault" | "refreshIntervalSeconds"
> & {
  readonly description: string;
  readonly widgets: readonly EditableProofWidget[];
  readonly theme: ProofThemeToken;
  readonly filterDefault: string;
  readonly refreshIntervalSeconds: number;
};

const PROOF_WIDGET_BINDINGS: Record<WidgetKind, readonly BindingOption[]> = {
  map: [{ value: "incidents.coordinates", label: "Incident coordinates" }],
  list: [
    { value: "incidents.rows", label: "All incidents" },
    { value: "incidents.openRows", label: "Open incidents" },
    { value: "incidents.highPriorityRows", label: "High-priority incidents" },
  ],
  count: [
    { value: "incidents.count", label: "All incidents" },
    { value: "incidents.openCount", label: "Open incidents" },
    { value: "incidents.highPriorityCount", label: "High-priority incidents" },
  ],
  chart: [
    { value: "incidents.type", label: "By type" },
    { value: "incidents.priority", label: "By priority" },
    { value: "incidents.status", label: "By status" },
  ],
  filter: [
    { value: "incidents.district", label: "District" },
    { value: "incidents.priority", label: "Priority" },
    { value: "incidents.status", label: "Status" },
  ],
};

const PROOF_THEME_OPTIONS: readonly { readonly value: ProofThemeToken; readonly label: string }[] = [
  { value: "default", label: "Default" },
  { value: "harbor", label: "Harbor" },
  { value: "high-contrast", label: "High contrast" },
];

const REGION_LABELS: Record<ProofWidgetRegion, string> = {
  main: "Main",
  side: "Side",
  footer: "Footer",
};

type SourceState =
  | { kind: "loading" }
  | { kind: "ready"; source: ProofSource }
  | { kind: "error"; error: CatalogError | Error };

type FlowPhase = "idle" | "thinking" | "clarifying" | "review" | "applying" | "preview" | "error";

interface FlowState {
  readonly phase: FlowPhase;
  readonly agentMessage?: string;
  readonly intent?: BuilderIntent;
  readonly plan?: BuilderPlan;
  readonly review?: ProofPlanReview;
  readonly progress?: JobProgress;
  readonly executionId?: string;
  readonly preview?: PreviewHandle;
  readonly appPackage?: AppPackage;
  readonly error?: string;
}

const INITIAL_FLOW: FlowState = { phase: "idle" };

export function StudioProofPage(): JSX.Element {
  const client = useCatalogClient();
  const [searchParams, setSearchParams] = useSearchParams();
  const itemId = searchParams.get("itemId") ?? DEFAULT_SOURCE_ITEM_ID;
  const requestedSourceKind = searchParams.get("source");
  const fixture = normalizeAppBuilderProofFixture(searchParams.get("fixture"));
  const [sourceState, setSourceState] = useState<SourceState>({ kind: "loading" });

  useEffect(() => {
    let cancelled = false;
    setSourceState({ kind: "loading" });
    client
      .getItem(itemId)
      .then((item) => {
        if (cancelled) return;
        setSourceState({
          kind: "ready",
          source: {
            kind: normalizeSourceKind(requestedSourceKind, item),
            item,
          },
        });
      })
      .catch((error: unknown) => {
        if (cancelled) return;
        setSourceState({ kind: "error", error: toError(error) });
      });
    return () => {
      cancelled = true;
    };
  }, [client, itemId, requestedSourceKind]);

  const handleFixtureChange = useCallback(
    (nextFixture: AppBuilderProofFixture) => {
      const next = new URLSearchParams(searchParams);
      if (nextFixture === DEFAULT_APP_BUILDER_PROOF_FIXTURE) {
        next.delete("fixture");
      } else {
        next.set("fixture", nextFixture);
      }
      setSearchParams(next, { replace: true });
    },
    [searchParams, setSearchParams],
  );

  if (sourceState.kind === "loading") {
    return (
      <main className="abp" data-testid="app-builder-proof-page">
        <PageSkeleton fixture={fixture} onFixtureChange={handleFixtureChange} />
        <EmptyState title="Loading source" description="Resolving the selected catalog source for the Studio proof route." />
      </main>
    );
  }

  if (sourceState.kind === "error") {
    return (
      <main className="abp" data-testid="app-builder-proof-page">
        <PageSkeleton fixture={fixture} onFixtureChange={handleFixtureChange} />
        <SourceErrorState error={sourceState.error} />
      </main>
    );
  }

  return (
    <main
      className="abp"
      data-testid="app-builder-proof-page"
      data-source-kind={sourceState.source.kind}
      data-source-item-id={sourceState.source.item.id}
    >
      <ProofHeader source={sourceState.source} fixture={fixture} onFixtureChange={handleFixtureChange} />
      {isBlockingProofFixture(fixture) ? (
        <ProofBlockedState source={sourceState.source} fixture={fixture} />
      ) : (
        <ProofFlow
          key={`${sourceState.source.kind}:${sourceState.source.item.id}:${fixture}`}
          source={sourceState.source}
          fixture={fixture}
        />
      )}
    </main>
  );
}

function ProofFlow({ source, fixture }: { source: ProofSource; fixture: AppBuilderProofFixture }): JSX.Element {
  const [prompt, setPrompt] = useState(APP_BUILDER_PROOF_PROMPT);
  const [flow, setFlow] = useState<FlowState>(INITIAL_FLOW);
  const telemetryContext = useMemo(
    () => ({
      sourceKind: source.kind,
      itemId: source.item.id,
      fixture,
    }),
    [source.kind, source.item.id, fixture],
  );
  const telemetry = useMemo<OperatorTelemetry>(
    () => ({
      before(span) {
        emitStudioProofTelemetry({
          name: "sdk.before",
          ...telemetryContext,
          detail: { kind: span.kind, intentId: span.intentId, ...span.detail },
        });
      },
      after(span) {
        emitStudioProofTelemetry({
          name: "sdk.after",
          ...telemetryContext,
          detail: { kind: span.kind, durationMs: span.durationMs, ...span.detail },
        });
      },
      error(span) {
        emitStudioProofTelemetry({
          name: "sdk.error",
          ...telemetryContext,
          detail: { kind: span.kind, durationMs: span.durationMs, message: errorMessage(span.error) },
        });
      },
    }),
    [telemetryContext],
  );
  const workspace = useMemo(
    () =>
      new OperatorWorkspace({
        client: createProofOperatorClient({ source, fixture }),
        telemetry,
      }),
    [source, fixture, telemetry],
  );

  useEffect(() => {
    emitStudioProofTelemetry({ name: "studio.proof.opened", ...telemetryContext });
    return () => {
      workspace.dispose();
    };
  }, [workspace, telemetryContext]);

  useEffect(() => {
    let cancelled = false;
    const showPreview = (pkg: AppPackage) => {
      queueMicrotask(() => {
        if (cancelled) return;
        try {
          const preview = workspace.builder.preview();
          setFlow((prev) => ({
            ...prev,
            phase: "preview",
            appPackage: pkg,
            preview,
            progress: { percent: 100, message: "Preview ready" },
          }));
          emitStudioProofTelemetry({
            name: "studio.proof.preview-ready",
            ...telemetryContext,
            detail: {
              appPackageId: pkg.id,
              previewUrl: preview.url,
              mapPackageId: preview.mapPackage?.mapPackageId,
            },
          });
        } catch (error) {
          const message = errorMessage(error);
          setFlow((prev) => ({ ...prev, phase: "error", error: message }));
          emitStudioProofTelemetry({
            name: "studio.proof.error",
            ...telemetryContext,
            detail: { message, recoverable: false },
          });
        }
      });
    };
    const unsubscribe = workspace.on((event) => {
      switch (event.kind) {
        case "turn-updated":
          if (event.turn.role === "agent") {
            setFlow((prev) => ({ ...prev, agentMessage: event.turn.content }));
          }
          return;
        case "intent-drafted":
          setFlow((prev) => ({ ...prev, intent: event.intent as BuilderIntent }));
          return;
        case "clarification-needed":
          setFlow((prev) => ({ ...prev, phase: "clarifying", intent: event.intent as BuilderIntent }));
          emitStudioProofTelemetry({
            name: "studio.proof.clarification-needed",
            ...telemetryContext,
            detail: { fields: event.intent.clarifications?.map((field) => field.id) ?? [] },
          });
          return;
        case "clarification-answered":
          setFlow((prev) => ({ ...prev, phase: "thinking", intent: event.intent as BuilderIntent }));
          return;
        case "plan-loaded": {
          if (event.plan.kind !== "builder") return;
          const plan = event.plan as BuilderPlan;
          const review = readProofPlanReview(plan);
          if (!review) {
            setFlow({ phase: "error", error: "The proof fixture returned a plan without draft/spec metadata." });
            return;
          }
          setFlow((prev) => ({
            ...prev,
            phase: "review",
            plan,
            review,
            progress: undefined,
            preview: undefined,
            appPackage: undefined,
            error: undefined,
          }));
          emitStudioProofTelemetry({
            name: "studio.proof.plan-ready",
            ...telemetryContext,
            detail: { planId: plan.id, warningCount: review.warnings.length },
          });
          return;
        }
        case "plan-accepted":
          setFlow((prev) => ({
            ...prev,
            phase: "applying",
            progress: { percent: 0, message: "Submitting apply job" },
          }));
          return;
        case "execution-started":
          setFlow((prev) => ({
            ...prev,
            phase: "applying",
            executionId: event.executionId,
            progress: { percent: 5, message: "Apply job accepted" },
          }));
          return;
        case "execution-progress":
          setFlow((prev) => ({
            ...prev,
            phase: "applying",
            executionId: event.executionId,
            progress: {
              percent: event.percent,
              message: event.message ?? "Applying plan",
            },
          }));
          emitStudioProofTelemetry({
            name: "studio.proof.apply-progress",
            ...telemetryContext,
            detail: { executionId: event.executionId, percent: event.percent, message: event.message },
          });
          return;
        case "execution-terminal":
          setFlow((prev) => ({
            ...prev,
            phase: "applying",
            executionId: event.executionId,
            progress: { percent: 100, message: "Loading preview package" },
          }));
          return;
        case "app-loaded": {
          showPreview(event.pkg);
          return;
        }
        case "error":
          setFlow((prev) => ({ ...prev, phase: "error", error: event.error.message }));
          emitStudioProofTelemetry({
            name: "studio.proof.error",
            ...telemetryContext,
            detail: { message: event.error.message, recoverable: event.recoverable },
          });
          return;
        case "execution-dismissed":
        case "map-loaded":
        case "map-refined":
        case "app-refined":
        case "approval-required":
        case "approval-resolved":
          return;
      }
    });
    return () => {
      cancelled = true;
      unsubscribe();
    };
  }, [workspace, telemetryContext]);

  const handlePromptSubmit = useCallback(
    async (event: FormEvent<HTMLFormElement>) => {
      event.preventDefault();
      setFlow({ phase: "thinking", progress: undefined });
      emitStudioProofTelemetry({
        name: "studio.proof.prompt-submitted",
        ...telemetryContext,
        detail: { promptLength: prompt.length },
      });
      try {
        await workspace.chat.send(prompt);
      } catch (error) {
        setFlow({ phase: "error", error: errorMessage(error) });
      }
    },
    [prompt, workspace, telemetryContext],
  );

  const handleClarificationSubmit = useCallback(
    async (answers: Readonly<Record<string, string>>) => {
      try {
        for (const [fieldId, value] of Object.entries(answers)) {
          workspace.clarification.setAnswer(fieldId, value);
        }
        setFlow((prev) => ({ ...prev, phase: "thinking" }));
        emitStudioProofTelemetry({
          name: "studio.proof.clarification-submitted",
          ...telemetryContext,
          detail: { fields: Object.keys(answers) },
        });
        await workspace.clarification.submit();
      } catch (error) {
        setFlow((prev) => ({ ...prev, phase: "error", error: errorMessage(error) }));
      }
    },
    [workspace, telemetryContext],
  );

  const handleApply = useCallback(() => {
    try {
      setFlow((prev) => ({
        ...prev,
        phase: "applying",
        progress: { percent: 0, message: "Submitting apply job" },
        error: undefined,
      }));
      emitStudioProofTelemetry({
        name: "studio.proof.apply-started",
        ...telemetryContext,
        detail: { planId: flow.plan?.id },
      });
      workspace.planReview.accept();
    } catch (error) {
      setFlow((prev) => ({ ...prev, phase: "error", error: errorMessage(error) }));
    }
  }, [workspace, flow.plan?.id, telemetryContext]);

  return (
    <div className="abp__flow">
      <PromptPanel
        prompt={prompt}
        disabled={flow.phase === "thinking" || flow.phase === "applying"}
        agentMessage={flow.agentMessage}
        onPromptChange={setPrompt}
        onSubmit={handlePromptSubmit}
      />
      {flow.phase === "thinking" ? <ThinkingPanel /> : null}
      {flow.phase === "clarifying" && flow.intent ? (
        <ClarificationPanel intent={flow.intent} onSubmit={handleClarificationSubmit} />
      ) : null}
      {flow.phase === "review" && flow.plan && flow.review ? (
        <ReviewPanel plan={flow.plan} review={flow.review} onApply={handleApply} />
      ) : null}
      {flow.phase === "applying" ? <ProgressPanel progress={flow.progress} executionId={flow.executionId} /> : null}
      {flow.phase === "preview" && flow.preview && flow.appPackage ? (
        <GeneratedAppPreview preview={flow.preview} appPackage={flow.appPackage} />
      ) : null}
      {flow.phase === "error" ? (
        <ApplyErrorState message={flow.error} canRetry={Boolean(flow.plan)} onRetry={handleApply} />
      ) : null}
    </div>
  );
}

function PageSkeleton({
  fixture,
  onFixtureChange,
}: {
  fixture: AppBuilderProofFixture;
  onFixtureChange: (fixture: AppBuilderProofFixture) => void;
}): JSX.Element {
  return (
    <header className="abp__header">
      <div>
        <p className="abp__crumb">
          <Link to="/">Back to home</Link>
        </p>
        <h1 className="abp__title">Honua Studio proof</h1>
      </div>
      <FixtureSelector fixture={fixture} onFixtureChange={onFixtureChange} />
    </header>
  );
}

function ProofHeader({
  source,
  fixture,
  onFixtureChange,
}: {
  source: ProofSource;
  fixture: AppBuilderProofFixture;
  onFixtureChange: (fixture: AppBuilderProofFixture) => void;
}): JSX.Element {
  return (
    <header className="abp__header">
      <div className="abp__header-main">
        <p className="abp__crumb">
          <Link to="/">Back to home</Link>
        </p>
        <h1 className="abp__title">Honua Studio proof</h1>
        <p className="abp__subtitle">Prompt-to-preview slice for a selected Console catalog source.</p>
      </div>
      <div className="abp__header-side">
        <SourceBadge source={source} />
        <FixtureSelector fixture={fixture} onFixtureChange={onFixtureChange} />
      </div>
    </header>
  );
}

function SourceBadge({ source }: { source: ProofSource }): JSX.Element {
  return (
    <section className="abp-source" aria-label="Selected source">
      <span className="abp-source__kind">{source.kind === "saved-map" ? "Saved map" : "Catalog item"}</span>
      <strong>{source.item.title}</strong>
      <span>{source.item.type}</span>
    </section>
  );
}

function FixtureSelector({
  fixture,
  onFixtureChange,
}: {
  fixture: AppBuilderProofFixture;
  onFixtureChange: (fixture: AppBuilderProofFixture) => void;
}): JSX.Element {
  return (
    <label className="abp-fixture">
      <span>Fixture</span>
      <select
        value={fixture}
        onChange={(event) => onFixtureChange(event.target.value as AppBuilderProofFixture)}
        aria-label="Proof fixture state"
      >
        {APP_BUILDER_PROOF_FIXTURES.map((option) => (
          <option key={option} value={option}>
            {fixtureLabel(option)}
          </option>
        ))}
      </select>
    </label>
  );
}

function PromptPanel({
  prompt,
  disabled,
  agentMessage,
  onPromptChange,
  onSubmit,
}: {
  prompt: string;
  disabled: boolean;
  agentMessage?: string;
  onPromptChange: (value: string) => void;
  onSubmit: (event: FormEvent<HTMLFormElement>) => void;
}): JSX.Element {
  return (
    <section className="abp-panel" aria-labelledby="abp-prompt-title">
      <div className="abp-panel__header">
        <h2 id="abp-prompt-title">Studio prompt</h2>
      </div>
      <form className="abp-prompt" onSubmit={onSubmit}>
        <label htmlFor="abp-proof-prompt">Prompt</label>
        <textarea
          id="abp-proof-prompt"
          value={prompt}
          onChange={(event) => onPromptChange(event.target.value)}
          rows={4}
          disabled={disabled}
        />
        <button type="submit" className="hc-btn hc-btn--primary" disabled={disabled || prompt.trim().length === 0}>
          Generate draft
        </button>
      </form>
      {agentMessage ? (
        <output className="abp-agent" aria-live="polite">
          {agentMessage}
        </output>
      ) : null}
    </section>
  );
}

function ThinkingPanel(): JSX.Element {
  return (
    <section className="abp-panel abp-panel--compact" data-testid="app-builder-thinking">
      <span className="hc-loading" aria-hidden="true">
        <span className="hc-loading__dot" />
        <span className="hc-loading__dot" />
        <span className="hc-loading__dot" />
      </span>
      <output aria-live="polite">Preparing proof response…</output>
    </section>
  );
}

function ClarificationPanel({
  intent,
  onSubmit,
}: {
  intent: BuilderIntent;
  onSubmit: (answers: Readonly<Record<string, string>>) => void;
}): JSX.Element {
  const fields = intent.clarifications ?? [];
  const [answers, setAnswers] = useState<Record<string, string>>(() => defaultAnswers(fields));
  const missingRequired = fields.some((field) => field.required && !(answers[field.id] ?? "").trim());

  return (
    <section className="abp-panel" aria-labelledby="abp-clarification-title" data-testid="clarification-panel">
      <div className="abp-panel__header">
        <h2 id="abp-clarification-title">Clarification</h2>
      </div>
      <div className="abp-fields">
        {fields.map((field) => {
          const controlId = `abp-field-${field.id}`;
          return (
            <div key={field.id} className="abp-field">
              <label htmlFor={controlId}>{field.label}</label>
              {field.type === "select" ? (
                <select
                  id={controlId}
                  value={answers[field.id] ?? ""}
                  onChange={(event) => setAnswers((prev) => ({ ...prev, [field.id]: event.target.value }))}
                >
                  <option value="">Select one</option>
                  {field.options?.map((option) => (
                    <option key={option.value} value={option.value}>
                      {option.label}
                    </option>
                  ))}
                </select>
              ) : (
                <input
                  id={controlId}
                  value={answers[field.id] ?? ""}
                  onChange={(event) => setAnswers((prev) => ({ ...prev, [field.id]: event.target.value }))}
                />
              )}
            </div>
          );
        })}
      </div>
      <button
        type="button"
        className="hc-btn hc-btn--primary"
        disabled={missingRequired}
        onClick={() => onSubmit(answers)}
      >
        Submit clarification
      </button>
    </section>
  );
}

function ReviewPanel({
  plan,
  review,
  onApply,
}: {
  plan: BuilderPlan;
  review: ProofPlanReview;
  onApply: () => void;
}): JSX.Element {
  return (
    <section className="abp-panel" aria-labelledby="abp-review-title" data-testid="draft-review">
      <div className="abp-panel__header">
        <h2 id="abp-review-title">Review draft/spec and plan</h2>
        <button type="button" className="hc-btn hc-btn--primary" onClick={onApply}>
          Apply fixture plan
        </button>
      </div>
      <div className="abp-review">
        <DraftSpec draft={review.draft} />
        <PlanSteps plan={plan} />
        <Warnings warnings={review.warnings} />
      </div>
    </section>
  );
}

function DraftSpec({ draft }: { draft: ProofDraftSpec }): JSX.Element {
  return (
    <section className="abp-review__block">
      <h3>Draft/spec</h3>
      <pre data-testid="draft-spec-json">{JSON.stringify(draft, null, 2)}</pre>
    </section>
  );
}

function PlanSteps({ plan }: { plan: BuilderPlan }): JSX.Element {
  return (
    <section className="abp-review__block">
      <h3>Plan</h3>
      <ol className="abp-steps" data-testid="plan-steps">
        {plan.steps.map((step) => (
          <li key={step.id}>
            <strong>{step.label}</strong>
            <span>{step.kind}</span>
          </li>
        ))}
      </ol>
    </section>
  );
}

function Warnings({ warnings }: { warnings: readonly ProofWarning[] }): JSX.Element {
  return (
    <section className="abp-review__block">
      <h3>Warnings</h3>
      <ul className="abp-warnings" data-testid="plan-warnings">
        {warnings.map((warning) => (
          <li key={warning.code} data-severity={warning.severity}>
            <strong>{warning.code}</strong>
            <span>{warning.message}</span>
          </li>
        ))}
      </ul>
    </section>
  );
}

function ProgressPanel({
  progress,
  executionId,
}: {
  progress?: JobProgress;
  executionId?: string;
}): JSX.Element {
  const percent = progress?.percent ?? 0;
  return (
    <section className="abp-panel abp-progress" aria-labelledby="abp-progress-title" data-testid="apply-progress">
      <div className="abp-panel__header">
        <h2 id="abp-progress-title">Apply progress</h2>
        {executionId ? <span>{executionId}</span> : null}
      </div>
      <progress max={100} value={percent} />
      <output aria-live="polite">{progress?.message ?? "Applying plan"}</output>
    </section>
  );
}

function GeneratedAppPreview({
  preview,
  appPackage,
}: {
  preview: PreviewHandle;
  appPackage: AppPackage;
}): JSX.Element {
  const baseMetadata = readProofPreviewMetadata(appPackage);
  const [metadata, setMetadata] = useState<EditableProofMetadata | null>(() =>
    baseMetadata ? loadStoredProofMetadata(appPackage.id, baseMetadata) : null,
  );
  const [validation, setValidation] = useState<Readonly<Record<string, string>>>({});

  useEffect(() => {
    setMetadata(baseMetadata ? loadStoredProofMetadata(appPackage.id, baseMetadata) : null);
    setValidation({});
  }, [appPackage.id, baseMetadata]);

  if (!baseMetadata) {
    return (
      <EmptyState
        tone="warning"
        title="Preview package is missing metadata"
        description="The SDK-JS preview handle loaded, but the proof fixture did not include renderable app metadata."
      />
    );
  }

  const activeMetadata = metadata ?? normalizeProofMetadata(baseMetadata);
  const commitMetadata = (
    next: EditableProofMetadata,
    action: string,
    detail: Readonly<Record<string, unknown>> = {},
  ) => {
    setValidation({});
    setMetadata(next);
    saveStoredProofMetadata(appPackage.id, next);
    emitStudioProofTelemetry({
      name: "studio.proof.edit-committed",
      sourceKind: next.source.kind,
      itemId: next.source.itemId,
      detail: { action, ...detail },
    });
  };
  const blockEdit = (field: string, message: string, detail: Readonly<Record<string, unknown>> = {}) => {
    setValidation((prev) => ({ ...prev, [field]: message }));
    emitStudioProofTelemetry({
      name: "studio.proof.edit-blocked",
      sourceKind: activeMetadata.source.kind,
      itemId: activeMetadata.source.itemId,
      detail: { field, message, ...detail },
    });
  };

  const renameAppTitle = (value: string) => {
    if (!value.trim()) {
      blockEdit("app-title", "App title is required.");
      return;
    }
    if (value.length > 80) {
      blockEdit("app-title", "App title must be 80 characters or fewer.");
      return;
    }
    commitMetadata({ ...activeMetadata, title: value }, "rename-app", { field: "title" });
  };
  const setDescription = (value: string) => {
    if (value.length > 180) {
      blockEdit("app-description", "Description must be 180 characters or fewer.");
      return;
    }
    commitMetadata({ ...activeMetadata, description: value }, "set-app-description");
  };
  const renameWidget = (widgetId: string, value: string) => {
    if (!value.trim()) {
      blockEdit(`widget-${widgetId}-title`, "Widget title is required.", { widgetId });
      return;
    }
    if (value.length > 64) {
      blockEdit(`widget-${widgetId}-title`, "Widget title must be 64 characters or fewer.", { widgetId });
      return;
    }
    commitMetadata(
      updateWidget(activeMetadata, widgetId, (widget) => ({ ...widget, title: value })),
      "rename-widget",
      { widgetId },
    );
  };
  const setWidgetVisible = (widgetId: string, visible: boolean) => {
    const visibleCount = activeMetadata.widgets.filter((widget) => widget.visible).length;
    if (!visible && visibleCount <= 1) {
      blockEdit(`widget-${widgetId}-visible`, "At least one widget must remain visible.", { widgetId });
      return;
    }
    commitMetadata(
      updateWidget(activeMetadata, widgetId, (widget) => ({ ...widget, visible })),
      "set-widget-visible",
      { widgetId, visible },
    );
  };
  const setWidgetBinding = (widgetId: string, binding: string) => {
    const widget = activeMetadata.widgets.find((candidate) => candidate.id === widgetId);
    if (!widget || !isSupportedBinding(widget.kind, binding)) {
      blockEdit(`widget-${widgetId}-binding`, "This binding is not supported by the proof manifest.", {
        widgetId,
        binding,
      });
      return;
    }
    const next = updateWidget(activeMetadata, widgetId, (candidate) => ({ ...candidate, binding }));
    const filterDefault =
      widget.kind === "filter" ? (filterOptionsForBinding(next.rows, binding)[0] ?? "All") : next.filterDefault;
    commitMetadata({ ...next, filterDefault }, "set-widget-binding", { widgetId, binding });
  };
  const moveWidgetBy = (widgetId: string, delta: -1 | 1) => {
    const next = moveWidget(activeMetadata, widgetId, delta);
    if (next === activeMetadata) return;
    commitMetadata(next, "move-widget", { widgetId, delta });
  };
  const setWidgetRegion = (widgetId: string, region: ProofWidgetRegion) => {
    if (!PROOF_WIDGET_REGIONS.includes(region)) {
      blockEdit(`widget-${widgetId}-region`, "This layout region is not supported.", { widgetId, region });
      return;
    }
    commitMetadata(
      updateWidget(activeMetadata, widgetId, (widget) => ({ ...widget, region })),
      "set-widget-region",
      { widgetId, region },
    );
  };
  const setTheme = (theme: ProofThemeToken) => {
    if (!PROOF_THEME_TOKENS.includes(theme)) {
      blockEdit("theme", "This theme token is not supported.", { theme });
      return;
    }
    commitMetadata({ ...activeMetadata, theme }, "set-theme", { theme });
  };
  const setRefreshInterval = (seconds: number) => {
    if (!REFRESH_INTERVAL_OPTIONS.includes(seconds as (typeof REFRESH_INTERVAL_OPTIONS)[number])) {
      blockEdit("refresh-interval", "Refresh interval must use a supported manifest value.", { seconds });
      return;
    }
    commitMetadata({ ...activeMetadata, refreshIntervalSeconds: seconds }, "set-refresh-interval", { seconds });
  };
  const setFilterDefault = (value: string) => {
    const filterWidget = activeMetadata.widgets.find((widget) => widget.kind === "filter");
    const options = filterOptionsForBinding(activeMetadata.rows, filterWidget?.binding ?? "incidents.district");
    if (!options.includes(value)) {
      blockEdit("filter-default", "Filter default must match one of the generated filter values.", { value });
      return;
    }
    commitMetadata({ ...activeMetadata, filterDefault: value }, "set-filter-default", { value });
  };

  return (
    <div
      className="abp-editor-preview"
      data-testid="app-builder-editor-workspace"
      data-app-package-version={appPackage.version}
    >
      <ProofDraftEditor
        metadata={activeMetadata}
        validation={validation}
        onRenameAppTitle={renameAppTitle}
        onSetDescription={setDescription}
        onRenameWidget={renameWidget}
        onSetWidgetVisible={setWidgetVisible}
        onSetWidgetBinding={setWidgetBinding}
        onMoveWidget={moveWidgetBy}
        onSetWidgetRegion={setWidgetRegion}
        onSetTheme={setTheme}
        onSetRefreshInterval={setRefreshInterval}
        onSetFilterDefault={setFilterDefault}
      />
      <PreviewDashboard preview={preview} metadata={activeMetadata} />
    </div>
  );
}

function ProofDraftEditor({
  metadata,
  validation,
  onRenameAppTitle,
  onSetDescription,
  onRenameWidget,
  onSetWidgetVisible,
  onSetWidgetBinding,
  onMoveWidget,
  onSetWidgetRegion,
  onSetTheme,
  onSetRefreshInterval,
  onSetFilterDefault,
}: {
  metadata: EditableProofMetadata;
  validation: Readonly<Record<string, string>>;
  onRenameAppTitle: (value: string) => void;
  onSetDescription: (value: string) => void;
  onRenameWidget: (widgetId: string, value: string) => void;
  onSetWidgetVisible: (widgetId: string, visible: boolean) => void;
  onSetWidgetBinding: (widgetId: string, binding: string) => void;
  onMoveWidget: (widgetId: string, delta: -1 | 1) => void;
  onSetWidgetRegion: (widgetId: string, region: ProofWidgetRegion) => void;
  onSetTheme: (theme: ProofThemeToken) => void;
  onSetRefreshInterval: (seconds: number) => void;
  onSetFilterDefault: (value: string) => void;
}): JSX.Element {
  const [appTitle, setAppTitle] = useState(metadata.title);
  const [description, setDescription] = useState(metadata.description);
  const [widgetTitles, setWidgetTitles] = useState<Record<string, string>>(() =>
    Object.fromEntries(metadata.widgets.map((widget) => [widget.id, widget.title])),
  );
  const filterWidget = metadata.widgets.find((widget) => widget.kind === "filter");
  const filterOptions = filterOptionsForBinding(metadata.rows, filterWidget?.binding ?? "incidents.district");

  useEffect(() => {
    setAppTitle(metadata.title);
    setDescription(metadata.description);
    setWidgetTitles(Object.fromEntries(metadata.widgets.map((widget) => [widget.id, widget.title])));
  }, [metadata.title, metadata.description, metadata.widgets]);

  return (
    <section className="abp-editor" aria-labelledby="abp-editor-title">
      <div className="abp-panel__header">
        <h2 id="abp-editor-title">Direct editor</h2>
        <span data-testid="proof-refresh-summary">{formatRefreshInterval(metadata.refreshIntervalSeconds)}</span>
      </div>
      <div className="abp-editor__grid">
        <div className="abp-editor__section">
          <h3>App</h3>
          <div className="abp-field">
            <label htmlFor="abp-edit-app-title">Title</label>
            <input
              id="abp-edit-app-title"
              value={appTitle}
              aria-invalid={Boolean(validation["app-title"])}
              onChange={(event) => {
                setAppTitle(event.target.value);
                onRenameAppTitle(event.target.value);
              }}
            />
            <FieldError message={validation["app-title"]} />
          </div>
          <div className="abp-field">
            <label htmlFor="abp-edit-app-description">Description</label>
            <textarea
              id="abp-edit-app-description"
              value={description}
              rows={3}
              aria-invalid={Boolean(validation["app-description"])}
              onChange={(event) => {
                setDescription(event.target.value);
                onSetDescription(event.target.value);
              }}
            />
            <FieldError message={validation["app-description"]} />
          </div>
          <div className="abp-editor__row">
            <label className="abp-field" htmlFor="abp-edit-theme">
              <span>Theme</span>
              <select
                id="abp-edit-theme"
                value={metadata.theme}
                onChange={(event) => onSetTheme(event.target.value as ProofThemeToken)}
              >
                {PROOF_THEME_OPTIONS.map((option) => (
                  <option key={option.value} value={option.value}>
                    {option.label}
                  </option>
                ))}
              </select>
            </label>
            <label className="abp-field" htmlFor="abp-edit-refresh">
              <span>Refresh</span>
              <select
                id="abp-edit-refresh"
                value={metadata.refreshIntervalSeconds}
                onChange={(event) => onSetRefreshInterval(Number(event.target.value))}
              >
                {REFRESH_INTERVAL_OPTIONS.map((seconds) => (
                  <option key={seconds} value={seconds}>
                    {formatRefreshInterval(seconds)}
                  </option>
                ))}
              </select>
            </label>
          </div>
          {filterWidget ? (
            <label className="abp-field" htmlFor="abp-edit-filter-default">
              <span>Filter default</span>
              <select
                id="abp-edit-filter-default"
                value={metadata.filterDefault}
                onChange={(event) => onSetFilterDefault(event.target.value)}
              >
                {filterOptions.map((option) => (
                  <option key={option} value={option}>
                    {option}
                  </option>
                ))}
              </select>
            </label>
          ) : null}
        </div>
        <div className="abp-editor__section">
          <h3>Widgets</h3>
          <div className="abp-widget-list">
            {metadata.widgets.map((widget, index) => (
              <section key={widget.id} className="abp-widget-edit" data-widget-kind={widget.kind}>
                <div className="abp-widget-edit__header">
                  <label>
                    <input
                      type="checkbox"
                      checked={widget.visible}
                      onChange={(event) => onSetWidgetVisible(widget.id, event.target.checked)}
                    />
                    <span>Visible</span>
                  </label>
                  <div className="abp-widget-edit__moves">
                    <button
                      type="button"
                      className="hc-btn hc-btn--ghost"
                      disabled={index === 0}
                      onClick={() => onMoveWidget(widget.id, -1)}
                    >
                      Up
                    </button>
                    <button
                      type="button"
                      className="hc-btn hc-btn--ghost"
                      disabled={index === metadata.widgets.length - 1}
                      onClick={() => onMoveWidget(widget.id, 1)}
                    >
                      Down
                    </button>
                  </div>
                </div>
                <div className="abp-editor__row">
                  <div className="abp-field">
                    <label htmlFor={`abp-edit-${widget.id}-title`}>Title</label>
                    <input
                      id={`abp-edit-${widget.id}-title`}
                      value={widgetTitles[widget.id] ?? widget.title}
                      aria-invalid={Boolean(validation[`widget-${widget.id}-title`])}
                      onChange={(event) => {
                        setWidgetTitles((prev) => ({ ...prev, [widget.id]: event.target.value }));
                        onRenameWidget(widget.id, event.target.value);
                      }}
                    />
                    <FieldError message={validation[`widget-${widget.id}-title`]} />
                  </div>
                  <label className="abp-field" htmlFor={`abp-edit-${widget.id}-region`}>
                    <span>Region</span>
                    <select
                      id={`abp-edit-${widget.id}-region`}
                      value={widget.region}
                      onChange={(event) => onSetWidgetRegion(widget.id, event.target.value as ProofWidgetRegion)}
                    >
                      {PROOF_WIDGET_REGIONS.map((region) => (
                        <option key={region} value={region}>
                          {REGION_LABELS[region]}
                        </option>
                      ))}
                    </select>
                  </label>
                </div>
                <label className="abp-field" htmlFor={`abp-edit-${widget.id}-binding`}>
                  <span>Binding</span>
                  <select
                    id={`abp-edit-${widget.id}-binding`}
                    value={widget.binding}
                    onChange={(event) => onSetWidgetBinding(widget.id, event.target.value)}
                  >
                    {bindingOptionsForWidget(widget.kind).map((option) => (
                      <option key={option.value} value={option.value}>
                        {option.label}
                      </option>
                    ))}
                  </select>
                </label>
                <FieldError message={validation[`widget-${widget.id}-binding`]} />
                <FieldError message={validation[`widget-${widget.id}-visible`]} />
              </section>
            ))}
          </div>
        </div>
      </div>
      <FieldError message={validation.theme} />
      <FieldError message={validation["refresh-interval"]} />
      <FieldError message={validation["filter-default"]} />
    </section>
  );
}

function FieldError({ message }: { message?: string }): JSX.Element | null {
  return message ? (
    <output className="abp-field__error" aria-live="polite">
      {message}
    </output>
  ) : null;
}

function PreviewDashboard({
  preview,
  metadata,
}: {
  preview: PreviewHandle;
  metadata: EditableProofMetadata;
}): JSX.Element {
  const filterWidget = metadata.widgets.find((widget) => widget.kind === "filter" && widget.visible);
  const filterBinding = filterWidget?.binding ?? "incidents.district";
  const filterOptions = useMemo(
    () => filterOptionsForBinding(metadata.rows, filterBinding),
    [metadata.rows, filterBinding],
  );
  const [filterValue, setFilterValue] = useState(() => resolveFilterDefault(metadata.filterDefault, filterOptions));

  useEffect(() => {
    setFilterValue(resolveFilterDefault(metadata.filterDefault, filterOptions));
  }, [metadata.filterDefault, filterOptions]);

  const rows = useMemo(
    () => applyFilter(metadata.rows, filterBinding, filterValue),
    [filterBinding, filterValue, metadata.rows],
  );
  const widgetsByRegion = useMemo(() => {
    const regions: Record<ProofWidgetRegion, EditableProofWidget[]> = { main: [], side: [], footer: [] };
    for (const widget of metadata.widgets) {
      if (widget.visible) regions[widget.region].push(widget);
    }
    return regions;
  }, [metadata.widgets]);

  const usesVegaLite = metadata.widgets.some(
    (widget) => widget.visible && widget.kind === "chart" && widget.chartSpec?.kind === "vega-lite",
  );

  return (
    <section
      className="abp-preview"
      aria-labelledby="abp-preview-title"
      data-testid="generated-app-preview"
      data-sdk-preview-url={preview.url ?? ""}
      data-map-package={preview.mapPackage?.mapPackageId ?? ""}
      data-theme={metadata.theme}
      data-refresh-interval={metadata.refreshIntervalSeconds}
      data-chart-spec={usesVegaLite ? "vega-lite" : "css-bars"}
    >
      <header className="abp-preview__header">
        <div>
          <h2 id="abp-preview-title">{metadata.title}</h2>
          <p>{metadata.description}</p>
          <span>{metadata.source.title}</span>
        </div>
        <span className="abp-preview__refresh">{formatRefreshInterval(metadata.refreshIntervalSeconds)}</span>
      </header>
      <div className="abp-preview__layout">
        {PROOF_WIDGET_REGIONS.map((region) => (
          <div key={region} className="abp-preview__region" data-region={region}>
            {widgetsByRegion[region].map((widget) =>
              renderPreviewWidget(widget, {
                rows,
                filterValue,
                filterOptions,
                setFilterValue,
              }),
            )}
          </div>
        ))}
      </div>
      {metadata.warnings.length > 0 ? <Warnings warnings={metadata.warnings} /> : null}
    </section>
  );
}

interface PreviewWidgetContext {
  readonly rows: readonly ProofIncidentRow[];
  readonly filterValue: string;
  readonly filterOptions: readonly string[];
  readonly setFilterValue: (value: string) => void;
}

function renderPreviewWidget(widget: EditableProofWidget, context: PreviewWidgetContext): JSX.Element {
  const titleId = `abp-preview-${widget.id}-title`;
  switch (widget.kind) {
    case "map": {
      const rows = rowsForWidgetBinding(context.rows, widget.binding);
      return (
        <section key={widget.id} className="abp-preview__map" data-widget-kind="map" aria-label={widget.title}>
          <div className="abp-map-grid" aria-hidden="true" />
          {rows.map((row) => (
            <span
              key={row.id}
              className={`abp-map-marker abp-map-marker--${row.type.toLowerCase()}`}
              style={markerStyle(row)}
              title={`${row.id}: ${row.name}`}
            />
          ))}
        </section>
      );
    }
    case "count": {
      const rows = rowsForWidgetBinding(context.rows, widget.binding);
      return (
        <section key={widget.id} className="abp-preview__metric" data-widget-kind="count">
          <span>{widget.title}</span>
          <strong>{rows.length}</strong>
        </section>
      );
    }
    case "chart": {
      const rows = rowsForWidgetBinding(context.rows, widget.binding);
      const field = chartFieldForBinding(widget.binding);
      const counts = countByField(rows, field);
      return (
        <section key={widget.id} className="abp-preview__chart" data-widget-kind="chart" aria-labelledby={titleId}>
          <h3 id={titleId}>{widget.title}</h3>
          <ChartSpecView
            chartSpec={widget.chartSpec}
            fallback={counts}
            title={widget.title}
            data-widget-id={widget.id}
          />
        </section>
      );
    }
    case "filter":
      return (
        <section
          key={widget.id}
          className="abp-preview__filter-card"
          data-widget-kind="filter"
          aria-labelledby={titleId}
        >
          <label className="abp-preview__filter">
            <span id={titleId}>{widget.title}</span>
            <select
              value={context.filterValue}
              aria-label={filterLabelForBinding(widget.binding)}
              onChange={(event) => context.setFilterValue(event.target.value)}
            >
              {context.filterOptions.map((option) => (
                <option key={option} value={option}>
                  {option}
                </option>
              ))}
            </select>
          </label>
        </section>
      );
    case "list": {
      const rows = rowsForWidgetBinding(context.rows, widget.binding);
      return (
        <section key={widget.id} className="abp-preview__table" data-widget-kind="list" aria-labelledby={titleId}>
          <h3 id={titleId}>{widget.title}</h3>
          <table>
            <thead>
              <tr>
                <th>Incident</th>
                <th>Type</th>
                <th>District</th>
                <th>Priority</th>
                <th>Status</th>
              </tr>
            </thead>
            <tbody>
              {rows.map((row) => (
                <tr key={row.id}>
                  <td>
                    <strong>{row.id}</strong>
                    <span>{row.name}</span>
                  </td>
                  <td>{row.type}</td>
                  <td>{row.district}</td>
                  <td>{row.priority}</td>
                  <td>{row.status}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </section>
      );
    }
  }
}

function ProofBlockedState({
  source,
  fixture,
}: {
  source: ProofSource;
  fixture: AppBuilderProofFixture;
}): JSX.Element {
  useEffect(() => {
    emitStudioProofTelemetry({
      name: "studio.proof.fixture-blocked",
      sourceKind: source.kind,
      itemId: source.item.id,
      fixture,
    });
  }, [source.kind, source.item.id, fixture]);

  if (fixture === "auth-denied") {
    return (
      <Forbidden
        reason="The deterministic proof fixture denied apply access for this catalog source."
        action={
          <Link className="hc-btn" to="/">
            Back to home
          </Link>
        }
      />
    );
  }
  if (fixture === "oversized") {
    return (
      <EmptyState
        tone="warning"
        title="Source exceeds the proof fixture limit"
        description="Studio proof keeps oversized sources out of the deterministic apply path and surfaces the size gate before preview."
        primaryAction={
          <Link className="hc-btn" to="/">
            Back to home
          </Link>
        }
      >
        Fixture limit: 5 layers or 10,000 rows. Selected source: {source.item.title}.
      </EmptyState>
    );
  }
  return (
    <EmptyState
      title="Source is unsupported by the proof fixture"
      description="The selected source resolved, but this deterministic proof fixture does not produce an app package for its source shape."
      primaryAction={
        <Link className="hc-btn" to="/">
          Back to home
        </Link>
      }
    />
  );
}

function SourceErrorState({ error }: { error: CatalogError | Error }): JSX.Element {
  if (error instanceof CatalogError && error.code === "unauthorized") {
    return (
      <Forbidden
        reason={error.message}
        action={
          <Link className="hc-btn" to="/">
            Back to home
          </Link>
        }
      />
    );
  }
  return (
    <EmptyState
      tone={error instanceof CatalogError && error.code === "missing" ? "default" : "warning"}
      title={error instanceof CatalogError && error.code === "missing" ? "Source not found" : "Source failed to load"}
      description={error.message}
      primaryAction={
        <Link className="hc-btn" to="/">
          Back to home
        </Link>
      }
    />
  );
}

function ApplyErrorState({
  message,
  canRetry,
  onRetry,
}: {
  message?: string;
  canRetry: boolean;
  onRetry: () => void;
}): JSX.Element {
  return (
    <EmptyState
      tone="warning"
      title="Apply failed"
      description={message ?? "The deterministic apply fixture did not return a preview package."}
      primaryAction={
        canRetry ? (
          <button type="button" className="hc-btn hc-btn--primary" onClick={onRetry}>
            Retry apply
          </button>
        ) : undefined
      }
    />
  );
}

function defaultAnswers(fields: NonNullable<BuilderIntent["clarifications"]>): Record<string, string> {
  return Object.fromEntries(fields.map((field) => [field.id, field.options?.[0]?.value ?? ""]));
}

function normalizeSourceKind(value: string | null, item: ContentItem): ProofSource["kind"] {
  if (value === "saved-map") return "saved-map";
  if (value === "catalog-item") return "catalog-item";
  return item.type === "map" ? "saved-map" : "catalog-item";
}

function markerStyle(row: ProofIncidentRow): CSSProperties {
  const [lng, lat] = row.coordinates;
  const left = clamp(((lng + 157.9) / 0.14) * 100, 5, 95);
  const top = clamp((1 - (lat - 21.26) / 0.15) * 100, 5, 95);
  return { left: `${left}%`, top: `${top}%` };
}

function normalizeProofMetadata(metadata: ProofPreviewMetadata): EditableProofMetadata {
  const widgets = metadata.widgets.map(
    (widget): EditableProofWidget => ({
      ...widget,
      visible: widget.visible ?? true,
      region: normalizeWidgetRegion(widget.region, widget.kind),
      binding: isSupportedBinding(widget.kind, widget.binding)
        ? widget.binding
        : bindingOptionsForWidget(widget.kind)[0].value,
    }),
  );
  const filterWidget = widgets.find((widget) => widget.kind === "filter");
  const filterOptions = filterOptionsForBinding(metadata.rows, filterWidget?.binding ?? "incidents.district");
  return {
    ...metadata,
    description: metadata.description ?? DEFAULT_PROOF_DESCRIPTION,
    widgets,
    theme: isProofThemeToken(metadata.theme) ? metadata.theme : "default",
    filterDefault: resolveFilterDefault(metadata.filterDefault, filterOptions),
    refreshIntervalSeconds: isRefreshInterval(metadata.refreshIntervalSeconds)
      ? metadata.refreshIntervalSeconds
      : DEFAULT_PROOF_REFRESH_SECONDS,
  };
}

function normalizeWidgetRegion(region: ProofWidgetRegion | undefined, kind: WidgetKind): ProofWidgetRegion {
  if (region && PROOF_WIDGET_REGIONS.includes(region)) return region;
  switch (kind) {
    case "map":
    case "chart":
      return "main";
    case "filter":
    case "count":
      return "side";
    case "list":
      return "footer";
  }
}

function updateWidget(
  metadata: EditableProofMetadata,
  widgetId: string,
  update: (widget: EditableProofWidget) => EditableProofWidget,
): EditableProofMetadata {
  return {
    ...metadata,
    widgets: metadata.widgets.map((widget) => (widget.id === widgetId ? update(widget) : widget)),
  };
}

function moveWidget(metadata: EditableProofMetadata, widgetId: string, delta: -1 | 1): EditableProofMetadata {
  const index = metadata.widgets.findIndex((widget) => widget.id === widgetId);
  const nextIndex = index + delta;
  if (index < 0 || nextIndex < 0 || nextIndex >= metadata.widgets.length) return metadata;
  const widgets = [...metadata.widgets];
  const [widget] = widgets.splice(index, 1);
  widgets.splice(nextIndex, 0, widget);
  return { ...metadata, widgets };
}

function bindingOptionsForWidget(kind: WidgetKind): readonly BindingOption[] {
  return PROOF_WIDGET_BINDINGS[kind];
}

function isSupportedBinding(kind: WidgetKind, binding: string): boolean {
  return bindingOptionsForWidget(kind).some((option) => option.value === binding);
}

function filterOptionsForBinding(rows: readonly ProofIncidentRow[], binding: string): readonly string[] {
  const field = filterFieldForBinding(binding);
  if (!field) return ["All"];
  return [allFilterLabel(field), ...unique(rows.map((row) => String(row[field])))];
}

function resolveFilterDefault(value: string | undefined, options: readonly string[]): string {
  if (value && options.includes(value)) return value;
  return options[0] ?? "All";
}

function filterFieldForBinding(binding: string): keyof ProofIncidentRow | null {
  switch (binding) {
    case "incidents.district":
      return "district";
    case "incidents.priority":
      return "priority";
    case "incidents.status":
      return "status";
    default:
      return null;
  }
}

function chartFieldForBinding(binding: string): keyof ProofIncidentRow {
  switch (binding) {
    case "incidents.priority":
      return "priority";
    case "incidents.status":
      return "status";
    default:
      return "type";
  }
}

function filterLabelForBinding(binding: string): string {
  const field = filterFieldForBinding(binding);
  if (!field) return "Filter";
  return field[0].toUpperCase() + field.slice(1);
}

function allFilterLabel(field: keyof ProofIncidentRow): string {
  switch (field) {
    case "district":
      return "All districts";
    case "priority":
      return "All priorities";
    case "status":
      return "All statuses";
    default:
      return "All";
  }
}

function applyFilter(rows: readonly ProofIncidentRow[], binding: string, value: string): readonly ProofIncidentRow[] {
  const field = filterFieldForBinding(binding);
  if (!field || value === allFilterLabel(field)) return rows;
  return rows.filter((row) => String(row[field]) === value);
}

function rowsForWidgetBinding(rows: readonly ProofIncidentRow[], binding: string): readonly ProofIncidentRow[] {
  switch (binding) {
    case "incidents.openRows":
    case "incidents.openCount":
      return rows.filter((row) => row.status === "Open");
    case "incidents.highPriorityRows":
    case "incidents.highPriorityCount":
      return rows.filter((row) => row.priority === "High");
    default:
      return rows;
  }
}

function countByField(rows: readonly ProofIncidentRow[], field: keyof ProofIncidentRow): Record<string, number> {
  const counts: Record<string, number> = {};
  for (const row of rows) {
    const key = String(row[field]);
    counts[key] = (counts[key] ?? 0) + 1;
  }
  return counts;
}

function unique(values: readonly string[]): string[] {
  return [...new Set(values)];
}

function clamp(value: number, min: number, max: number): number {
  return Math.max(min, Math.min(max, value));
}

function isProofThemeToken(value: unknown): value is ProofThemeToken {
  return typeof value === "string" && PROOF_THEME_TOKENS.includes(value as ProofThemeToken);
}

function isRefreshInterval(value: unknown): value is (typeof REFRESH_INTERVAL_OPTIONS)[number] {
  return (
    typeof value === "number" && REFRESH_INTERVAL_OPTIONS.includes(value as (typeof REFRESH_INTERVAL_OPTIONS)[number])
  );
}

function formatRefreshInterval(seconds: number): string {
  if (seconds < 60) return `${seconds}s refresh`;
  return `${Math.round(seconds / 60)}m refresh`;
}

function loadStoredProofMetadata(packageId: string, base: ProofPreviewMetadata): EditableProofMetadata {
  const fallback = normalizeProofMetadata(base);
  try {
    const raw = readProofEditorStorage(proofEditorStorageKey(packageId));
    if (!raw) return fallback;
    const stored = JSON.parse(raw) as Partial<ProofPreviewMetadata>;
    if (stored.kind !== base.kind || stored.source?.itemId !== base.source.itemId) return fallback;
    return normalizeProofMetadata({
      ...base,
      ...stored,
      source: base.source,
      rows: base.rows,
      warnings: base.warnings,
    });
  } catch {
    return fallback;
  }
}

function saveStoredProofMetadata(packageId: string, metadata: EditableProofMetadata): void {
  writeProofEditorStorage(proofEditorStorageKey(packageId), JSON.stringify(metadata));
}

function readProofEditorStorage(key: string): string | null {
  try {
    const value = window.localStorage.getItem(key);
    if (value) return value;
  } catch {
    // Fall back to session storage below.
  }
  try {
    return window.sessionStorage.getItem(key);
  } catch {
    return null;
  }
}

function writeProofEditorStorage(key: string, value: string): void {
  try {
    window.localStorage.setItem(key, value);
    if (window.localStorage.getItem(key) === value) return;
  } catch {
    // Fall back to session storage below.
  }
  try {
    window.sessionStorage.setItem(key, value);
  } catch {
    // Workspace persistence is best-effort until the publish slice adds durable storage.
  }
}

function proofEditorStorageKey(packageId: string): string {
  return `${PROOF_EDITOR_STORAGE_PREFIX}${packageId}.draft`;
}

function toError(error: unknown): CatalogError | Error {
  if (error instanceof CatalogError) return error;
  if (error instanceof Error) return error;
  return new Error(String(error));
}

function errorMessage(error: unknown): string {
  if (error instanceof Error) return error.message;
  return String(error);
}

// suppress unused-symbol noise from copying a static type ref forward
export type { ProofChartSpec };
