import type {
  IJobRun,
  JobError,
  JobProgress,
  JobResult,
  JobSnapshot,
  JobSnapshotListener,
  JobStatus,
} from "@honua/sdk-js";
import {
  type AppPackage,
  type ApprovalDecision,
  type BuilderIntent,
  type BuilderPlan,
  type ChatChunk,
  type ClarificationAnswer,
  type ExecutionResult,
  OPERATOR_EXECUTION_OUTPUT_KEY,
  type OperatorClient,
} from "@honua/sdk-js/operator";
import { HONUA_MAP_PACKAGE_FORMAT_V1, type HonuaMapPackage } from "@honua/sdk-js/runtime";

import type { ContentItem } from "../../transitional/content-item.js";

export const APP_BUILDER_PROOF_PROMPT =
  "Build an operations dashboard for this saved map showing a map, incident list, incident count, incidents by type chart, and district filter.";

export const APP_BUILDER_PROOF_FIXTURES = [
  "happy",
  "clarification",
  "unsupported",
  "auth-denied",
  "oversized",
  "apply-failure",
] as const;

export type AppBuilderProofFixture = (typeof APP_BUILDER_PROOF_FIXTURES)[number];

export const DEFAULT_APP_BUILDER_PROOF_FIXTURE: AppBuilderProofFixture = "happy";

export interface ProofSource {
  readonly kind: "catalog-item" | "saved-map";
  readonly item: ContentItem;
}

export interface ProofWarning {
  readonly code: string;
  readonly severity: "info" | "warning";
  readonly message: string;
}

export const PROOF_WIDGET_REGIONS = ["main", "side", "footer"] as const;

export type ProofWidgetRegion = (typeof PROOF_WIDGET_REGIONS)[number];

export const PROOF_THEME_TOKENS = ["default", "harbor", "high-contrast"] as const;

export type ProofThemeToken = (typeof PROOF_THEME_TOKENS)[number];

/**
 * The chart spec carried on a `chart` widget binding. When `kind` is
 * "vega-lite" the renderer uses the Vega-Lite adapter; otherwise it falls
 * back to the deterministic CSS bar-chart. Introduced for the Studio port
 * to ground future dashboard/report charts on Vega-Lite (ADR-0001).
 */
export interface ProofChartSpec {
  readonly kind: "vega-lite" | "css-bars";
  readonly title?: string;
  readonly vegaLite?: Readonly<Record<string, unknown>>;
}

export interface ProofDraftWidget {
  readonly id: string;
  readonly kind: "map" | "list" | "count" | "chart" | "filter";
  readonly title: string;
  readonly binding: string;
  readonly visible?: boolean;
  readonly region?: ProofWidgetRegion;
  readonly chartSpec?: ProofChartSpec;
}

export interface ProofDraftSpec {
  readonly id: string;
  readonly template: "operations-dashboard";
  readonly source: {
    readonly kind: ProofSource["kind"];
    readonly itemId: string;
    readonly title: string;
  };
  readonly widgets: readonly ProofDraftWidget[];
  readonly data: {
    readonly primaryLayer: string;
    readonly filterField: string;
    readonly chartField: string;
  };
}

export interface ProofIncidentRow {
  readonly id: string;
  readonly name: string;
  readonly type: string;
  readonly district: string;
  readonly priority: "High" | "Medium" | "Low";
  readonly status: "Open" | "Monitoring" | "Contained";
  readonly coordinates: readonly [number, number];
}

export interface ProofPreviewMetadata {
  readonly kind: "honua-app-builder-proof/v1";
  readonly title: string;
  readonly description?: string;
  readonly source: ProofDraftSpec["source"];
  readonly widgets: readonly ProofDraftWidget[];
  readonly rows: readonly ProofIncidentRow[];
  readonly warnings: readonly ProofWarning[];
  readonly theme?: ProofThemeToken;
  readonly filterDefault?: string;
  readonly refreshIntervalSeconds?: number;
}

export interface ProofPlanReview {
  readonly draft: ProofDraftSpec;
  readonly warnings: readonly ProofWarning[];
}

type ProofBuilderPlan = BuilderPlan & ProofPlanReview;

interface FixtureClientOptions {
  readonly fixture: AppBuilderProofFixture;
  readonly source: ProofSource;
}

const CLARIFICATION_FIELDS: BuilderIntent["clarifications"] = [
  {
    id: "incidentLayer",
    label: "Which layer should feed the incident widgets?",
    type: "select",
    required: true,
    options: [
      { value: "incidents", label: "Active incidents" },
      { value: "field-stations", label: "Field stations" },
    ],
  },
  {
    id: "districtField",
    label: "Which field should drive the district filter?",
    type: "select",
    required: true,
    options: [
      { value: "district", label: "district" },
      { value: "response_area", label: "response_area" },
    ],
  },
  {
    id: "typeField",
    label: "Which field groups incidents by type?",
    type: "select",
    required: true,
    options: [
      { value: "type", label: "type" },
      { value: "category", label: "category" },
    ],
  },
];

const INCIDENT_ROWS: readonly ProofIncidentRow[] = [
  {
    id: "INC-1001",
    name: "Ala Wai pump alarm",
    type: "Water",
    district: "East District",
    priority: "High",
    status: "Open",
    coordinates: [-157.826, 21.291],
  },
  {
    id: "INC-1002",
    name: "Kahala road closure",
    type: "Road",
    district: "East District",
    priority: "Medium",
    status: "Monitoring",
    coordinates: [-157.789, 21.274],
  },
  {
    id: "INC-1003",
    name: "Pearl City smoke report",
    type: "Fire",
    district: "West District",
    priority: "High",
    status: "Open",
    coordinates: [-157.878, 21.394],
  },
  {
    id: "INC-1004",
    name: "Harbor debris field",
    type: "Water",
    district: "West District",
    priority: "Low",
    status: "Contained",
    coordinates: [-157.866, 21.305],
  },
  {
    id: "INC-1005",
    name: "Downtown signal outage",
    type: "Road",
    district: "Central District",
    priority: "Medium",
    status: "Open",
    coordinates: [-157.858, 21.309],
  },
  {
    id: "INC-1006",
    name: "Punchbowl brush fire",
    type: "Fire",
    district: "Central District",
    priority: "High",
    status: "Monitoring",
    coordinates: [-157.846, 21.315],
  },
];

export function normalizeAppBuilderProofFixture(value: string | null): AppBuilderProofFixture {
  return APP_BUILDER_PROOF_FIXTURES.includes(value as AppBuilderProofFixture)
    ? (value as AppBuilderProofFixture)
    : DEFAULT_APP_BUILDER_PROOF_FIXTURE;
}

export function isBlockingProofFixture(fixture: AppBuilderProofFixture): boolean {
  return fixture === "unsupported" || fixture === "auth-denied" || fixture === "oversized";
}

export function fixtureLabel(fixture: AppBuilderProofFixture): string {
  switch (fixture) {
    case "happy":
      return "Happy path";
    case "clarification":
      return "Clarification";
    case "unsupported":
      return "Unsupported";
    case "auth-denied":
      return "Auth denied";
    case "oversized":
      return "Oversized";
    case "apply-failure":
      return "Apply failure";
  }
}

export function createProofOperatorClient(options: FixtureClientOptions): OperatorClient {
  return new FixtureAppBuilderProofClient(options);
}

export function readProofPlanReview(plan: BuilderPlan): ProofPlanReview | null {
  const draft = (plan as { draft?: unknown }).draft;
  const warnings = (plan as { warnings?: unknown }).warnings;
  if (!isProofDraftSpec(draft) || !isProofWarnings(warnings)) return null;
  return { draft, warnings };
}

export function readProofPreviewMetadata(pkg: AppPackage): ProofPreviewMetadata | null {
  const metadata = pkg.metadata;
  if (!metadata || typeof metadata !== "object") return null;
  const candidate = metadata as Partial<ProofPreviewMetadata>;
  if (candidate.kind !== "honua-app-builder-proof/v1") return null;
  if (typeof candidate.title !== "string") return null;
  if (!candidate.source || !Array.isArray(candidate.widgets) || !Array.isArray(candidate.rows)) return null;
  return candidate as ProofPreviewMetadata;
}

class FixtureAppBuilderProofClient implements OperatorClient {
  public readonly operator = {
    chat: this.chat.bind(this),
    clarify: this.clarify.bind(this),
    getPlan: this.getPlan.bind(this),
    revisePlan: this.revisePlan.bind(this),
    submitPlan: this.submitPlan.bind(this),
    refineMap: this.refineMap.bind(this),
    refineApp: this.refineApp.bind(this),
    getApproval: this.getApproval.bind(this),
    confirmApproval: this.confirmApproval.bind(this),
  };

  readonly #fixture: AppBuilderProofFixture;
  readonly #source: ProofSource;
  #prompt = APP_BUILDER_PROOF_PROMPT;
  #answers: readonly ClarificationAnswer[] = [];

  public constructor(options: FixtureClientOptions) {
    this.#fixture = options.fixture;
    this.#source = options.source;
  }

  private async *chat(text: string): AsyncIterable<ChatChunk> {
    this.#prompt = text;
    const intent = this.buildIntent(needsClarification(text, this.#fixture));
    yield {
      turnId: "studio-proof-agent",
      delta: intent.clarifications?.length
        ? "I need a few source bindings before drafting the app."
        : "Drafting the operations dashboard package.",
      done: true,
      intentDraft: intent,
    };
  }

  private async clarify(intentId: string, answers: ReadonlyArray<ClarificationAnswer>): Promise<BuilderIntent> {
    this.#answers = [...answers];
    return {
      ...this.buildIntent(false),
      id: intentId,
      clarifications: [],
      clarificationAnswers: answers,
    };
  }

  private async getPlan(intentId: string): Promise<ProofBuilderPlan> {
    return buildProofPlan(intentId, this.#source, this.#answers);
  }

  private async revisePlan(intentId: string): Promise<ProofBuilderPlan> {
    return buildProofPlan(intentId, this.#source, this.#answers);
  }

  private async submitPlan(plan: BuilderPlan): Promise<IJobRun<ExecutionResult>> {
    const result: ExecutionResult = {
      kind: "builder",
      summary: "Generated operations dashboard preview package.",
      mapPackage: buildMapPackage(this.#source),
      appPackage: buildAppPackage(this.#source),
      provenance: [
        { step: "draft", tool: "honua-server-fixture", startedAt: 1, finishedAt: 2 },
        { step: "package", tool: "sdk-js-app-runtime", startedAt: 2, finishedAt: 3 },
      ],
    };
    return new ProofJobRun({
      id: `job-${plan.id}`,
      result,
      fail: this.#fixture === "apply-failure",
    });
  }

  private async refineMap(): Promise<HonuaMapPackage> {
    return buildMapPackage(this.#source);
  }

  private async refineApp(): Promise<AppPackage> {
    return buildAppPackage(this.#source);
  }

  private async getApproval(): Promise<ApprovalDecision> {
    return buildApprovalDecision("granted");
  }

  private async confirmApproval(): Promise<ApprovalDecision> {
    return buildApprovalDecision("granted");
  }

  private buildIntent(withClarification: boolean): BuilderIntent {
    return {
      id: `intent-${this.#source.item.id}`,
      kind: "builder",
      request: this.#prompt,
      source: {
        kind: this.#source.kind,
        itemId: this.#source.item.id,
        title: this.#source.item.title,
      },
      clarifications: withClarification ? CLARIFICATION_FIELDS : [],
    };
  }
}

interface ProofJobRunOptions {
  readonly id: string;
  readonly result: ExecutionResult;
  readonly fail: boolean;
}

class ProofJobRun implements IJobRun<ExecutionResult> {
  public readonly id: string;
  public readonly type = "studio.proof.apply";
  public status: JobStatus = "accepted";
  public progress: JobProgress | undefined;

  readonly #result: ExecutionResult;
  readonly #fail: boolean;
  readonly #listeners = new Set<JobSnapshotListener<ExecutionResult>>();
  readonly #timers: number[] = [];
  #started = false;
  #terminal: Promise<JobResult<ExecutionResult>> | null = null;
  #resolveTerminal: ((value: JobResult<ExecutionResult>) => void) | null = null;
  #rejectTerminal: ((error: Error) => void) | null = null;

  public constructor(options: ProofJobRunOptions) {
    this.id = options.id;
    this.#result = options.result;
    this.#fail = options.fail;
  }

  public async poll(): Promise<JobSnapshot<ExecutionResult>> {
    if (this.status === "successful") {
      return {
        status: this.status,
        progress: this.progress,
        result: { outputs: { [OPERATOR_EXECUTION_OUTPUT_KEY]: this.#result } },
      };
    }
    if (this.status === "failed") {
      return { status: this.status, progress: this.progress, error: applyFailure() };
    }
    return { status: this.status, progress: this.progress };
  }

  public watch(listener: JobSnapshotListener<ExecutionResult>): () => void {
    this.#listeners.add(listener);
    this.start();
    return () => {
      this.#listeners.delete(listener);
    };
  }

  public async results(): Promise<JobResult<ExecutionResult>> {
    this.start();
    return this.#terminal!;
  }

  public async cancel(): Promise<JobStatus> {
    for (const timer of this.#timers.splice(0)) {
      window.clearTimeout(timer);
    }
    this.status = "dismissed";
    this.progress = { percent: 100, message: "Apply dismissed" };
    this.emit({ status: this.status, progress: this.progress });
    this.#rejectTerminal?.(new Error("Apply dismissed"));
    return this.status;
  }

  private start(): void {
    if (this.#started) return;
    this.#started = true;
    this.#terminal = new Promise((resolve, reject) => {
      this.#resolveTerminal = resolve;
      this.#rejectTerminal = reject;
    });
    this.schedule(0, () => {
      this.status = "running";
      this.progress = { percent: 25, message: "Validating source bindings" };
      this.emit({ status: this.status, progress: this.progress });
    });
    this.schedule(35, () => {
      this.status = "running";
      this.progress = { percent: 68, message: "Composing generated app package" };
      this.emit({ status: this.status, progress: this.progress });
    });
    this.schedule(70, () => {
      if (this.#fail) {
        this.status = "failed";
        this.progress = { percent: 82, message: "Apply failed" };
        const error = applyFailure();
        this.emit({ status: this.status, progress: this.progress, error });
        this.#rejectTerminal?.(new Error(error.message));
        return;
      }
      this.status = "successful";
      this.progress = { percent: 100, message: "Preview ready" };
      const result = { outputs: { [OPERATOR_EXECUTION_OUTPUT_KEY]: this.#result } };
      this.emit({ status: this.status, progress: this.progress, result });
      this.#resolveTerminal?.(result);
    });
  }

  private schedule(delayMs: number, callback: () => void): void {
    this.#timers.push(window.setTimeout(callback, delayMs));
  }

  private emit(snapshot: JobSnapshot<ExecutionResult>): void {
    for (const listener of [...this.#listeners]) listener(snapshot);
  }
}

function buildProofPlan(
  intentId: string,
  source: ProofSource,
  answers: readonly ClarificationAnswer[],
): ProofBuilderPlan {
  const draft = buildDraftSpec(source);
  const clarified = answers.length > 0;
  return {
    id: `plan-${source.item.id}`,
    intentId,
    kind: "builder",
    draft,
    warnings: [
      {
        code: "fixture-deterministic",
        severity: "info",
        message: "Uses the deterministic server fixture path; no public app is published.",
      },
      {
        code: "preview-sample-data",
        severity: "warning",
        message: "Incident rows are fixture data for GTM proof validation.",
      },
      ...(clarified
        ? [
            {
              code: "clarification-applied",
              severity: "info",
              message: "Clarification answers were applied to the draft widget bindings.",
            } satisfies ProofWarning,
          ]
        : []),
    ],
    steps: [
      {
        id: "source",
        kind: "source-bindings",
        label: `Bind ${source.kind === "saved-map" ? "saved map" : "catalog item"} source`,
        inputs: [source.item.id],
        outputs: ["source-manifest"],
      },
      {
        id: "draft",
        kind: "draft-spec",
        label: "Create deterministic dashboard draft/spec",
        inputs: ["source-manifest"],
        outputs: ["app-draft", "plan-warnings"],
      },
      {
        id: "package",
        kind: "app-package",
        label: "Apply fixture plan and package generated app preview",
        inputs: ["app-draft"],
        outputs: ["app-package", "map-package"],
        requiresApproval: true,
      },
    ],
  };
}

const INCIDENTS_BY_TYPE_VEGA_LITE: Readonly<Record<string, unknown>> = {
  $schema: "https://vega.github.io/schema/vega-lite/v5.json",
  description: "Incidents grouped by type, rendered by the Console Vega-Lite adapter.",
  data: {
    values: [
      { type: "Water", count: 2 },
      { type: "Fire", count: 2 },
      { type: "Road", count: 2 },
    ],
  },
  mark: { type: "bar", tooltip: true, cornerRadiusEnd: 2 },
  encoding: {
    x: { field: "type", type: "nominal", title: "Type" },
    y: { field: "count", type: "quantitative", title: "Count" },
    color: { field: "type", type: "nominal", legend: null },
  },
  config: { view: { stroke: null } },
};

function buildDraftSpec(source: ProofSource): ProofDraftSpec {
  return {
    id: `draft-${source.item.id}`,
    template: "operations-dashboard",
    source: {
      kind: source.kind,
      itemId: source.item.id,
      title: source.item.title,
    },
    widgets: [
      {
        id: "map",
        kind: "map",
        title: "Operations map",
        binding: "incidents.coordinates",
        visible: true,
        region: "main",
      },
      {
        id: "district-filter",
        kind: "filter",
        title: "District filter",
        binding: "incidents.district",
        visible: true,
        region: "side",
      },
      {
        id: "incident-count",
        kind: "count",
        title: "Incident count",
        binding: "incidents.count",
        visible: true,
        region: "side",
      },
      {
        id: "incidents-by-type",
        kind: "chart",
        title: "Incidents by type",
        binding: "incidents.type",
        visible: true,
        region: "main",
        chartSpec: {
          kind: "vega-lite",
          title: "Incidents by type",
          vegaLite: INCIDENTS_BY_TYPE_VEGA_LITE,
        },
      },
      {
        id: "incident-list",
        kind: "list",
        title: "Incident list",
        binding: "incidents.rows",
        visible: true,
        region: "footer",
      },
    ],
    data: {
      primaryLayer: "incidents",
      filterField: "district",
      chartField: "type",
    },
  };
}

function buildAppPackage(source: ProofSource): AppPackage {
  const draft = buildDraftSpec(source);
  const warnings: readonly ProofWarning[] = [
    {
      code: "sdk-preview-handle",
      severity: "info",
      message: "Preview is mounted from the SDK-JS BuilderWorkspace preview handle.",
    },
  ];
  return {
    id: `app-${source.item.id}`,
    version: "studio-proof/v1",
    assets: [
      {
        id: "entry",
        kind: "app-package",
        url: `honua://studio-proof/${encodeURIComponent(source.item.id)}`,
      },
    ],
    metadata: {
      kind: "honua-app-builder-proof/v1",
      title: "Generated operations dashboard",
      description: "Live operations summary generated from the selected catalog source.",
      source: draft.source,
      widgets: draft.widgets,
      rows: INCIDENT_ROWS,
      warnings,
      theme: "default",
      filterDefault: "All districts",
      refreshIntervalSeconds: 60,
    } satisfies ProofPreviewMetadata,
  };
}

function buildMapPackage(source: ProofSource): HonuaMapPackage {
  return {
    mapPackageId: `map-${source.item.id}`,
    format: HONUA_MAP_PACKAGE_FORMAT_V1,
    status: "Ready",
    sourceBindings: [
      {
        sourceId: "incidents",
        protocol: "workspace_artifact",
        locator: { serviceId: source.item.id },
        metadata: { sourceKind: source.kind },
      },
    ],
    mapSpec: {
      version: 8,
      sources: {},
      layers: [],
    },
    initialView: {
      center: [-157.845, 21.32],
      zoom: 10.5,
      crs: "EPSG:4326",
    },
    legend: [
      { label: "Water", color: "#277da1" },
      { label: "Fire", color: "#d1495b" },
      { label: "Road", color: "#f4a261" },
    ],
  };
}

function buildApprovalDecision(state: ApprovalDecision["state"]): ApprovalDecision {
  return {
    operationId: "studio-proof-approval",
    state,
    scope: "studio.proof.apply",
    reasons: [],
    requiredRoles: [],
    audit: [{ at: Date.now(), actor: "fixture-policy", action: state }],
  };
}

function needsClarification(prompt: string, fixture: AppBuilderProofFixture): boolean {
  if (fixture === "clarification") return true;
  const normalized = prompt.toLowerCase();
  return !(normalized.includes("incident") && normalized.includes("type") && normalized.includes("district"));
}

function applyFailure(): JobError {
  return {
    code: "ApplyFailed",
    message: "The deterministic apply fixture returned a package validation failure.",
    details: { fixture: "apply-failure" },
  };
}

function isProofDraftSpec(value: unknown): value is ProofDraftSpec {
  if (!value || typeof value !== "object") return false;
  const candidate = value as Partial<ProofDraftSpec>;
  return (
    typeof candidate.id === "string" &&
    candidate.template === "operations-dashboard" &&
    Boolean(candidate.source) &&
    Array.isArray(candidate.widgets)
  );
}

function isProofWarnings(value: unknown): value is readonly ProofWarning[] {
  return (
    Array.isArray(value) &&
    value.every((warning) => {
      if (!warning || typeof warning !== "object") return false;
      const candidate = warning as Partial<ProofWarning>;
      return typeof candidate.code === "string" && typeof candidate.message === "string";
    })
  );
}
