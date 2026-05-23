import { mkdir, readFile, writeFile } from "node:fs/promises";
import path from "node:path";
import {
  type IJobRun,
  type JobProgress,
  type JobResult,
  type JobSnapshot,
  type JobSnapshotListener,
  type JobStatus,
  isJobTerminal,
} from "@honua/sdk-js/contract";
import { createExplorationContext } from "@honua/sdk-js/exploration";
import {
  type AppPackage,
  type ApprovalDecision,
  type BuilderIntent,
  type BuilderPlan,
  BuilderWorkspaceController,
  type ChatChunk,
  ChatController,
  type ClarificationAnswer,
  ClarificationController,
  ExecutionController,
  type ExecutionResult,
  OPERATOR_EXECUTION_OUTPUT_KEY,
  type OperatorClient,
  type OperatorPlan,
  PlanReviewController,
} from "@honua/sdk-js/operator";
import type { HonuaMapPackage } from "@honua/sdk-js/runtime";
import { type Page, expect, test } from "@playwright/test";

const FIXTURE_DIR = path.resolve(process.cwd(), "fixtures/app-builder/operations-dashboard");
const ARTIFACT_ROOT = path.resolve(process.cwd(), "artifacts/smoke/app-builder-proof");
const SDK_WIDGET_RUNTIME = "@honua/sdk-js/exploration";

const PROOF_STEPS = [
  "prompt",
  "clarification",
  "spec-review",
  "plan-review",
  "apply",
  "preview",
  "edit",
  "publish",
  "reopen",
] as const;

const FAILURE_FIXTURES = [
  "unsupported-capability.json",
  "auth-denial.json",
  "oversized-estimate.json",
  "missing-binding.json",
  "apply-failure.json",
] as const;
const RUNTIME_CHANGE_EVENT_ORDER = ["filters", "grouping", "selection"] as const;

type ProofStepId = (typeof PROOF_STEPS)[number];
type WidgetKind = "map" | "list" | "indicator" | "chart" | "filter";
type WidgetRole = "map" | "grid" | "chart" | "form" | "custom";

interface RuntimeWidgetDescriptor {
  id: string;
  kind: WidgetKind;
  role: WidgetRole;
  title: string;
  runtime: string;
  binding: Record<string, unknown>;
}

interface SuccessFixture {
  id: string;
  kind: "success";
  ticket: string;
  mode: "model-free";
  prompt: string;
  source: {
    savedMapId: string;
    catalogItemId: string;
    title: string;
    sourceIds: string[];
  };
  intent: BuilderIntent;
  clarificationAnswers: ClarificationAnswer[];
  clarifiedIntent: BuilderIntent;
  spec: {
    id: string;
    manifestVersion: string;
    datasetId: string;
    runtime: {
      package: string;
      entrypoints: string[];
    };
    widgets: RuntimeWidgetDescriptor[];
    layout: Array<{ widgetId: string; region: string }>;
    theme: Record<string, string>;
    refreshIntervalSeconds: number;
  };
  plan: BuilderPlan & { warnings?: string[] };
  execution: {
    id: string;
    progress: JobProgress[];
  };
  mapPackage: HonuaMapPackage;
  appPackage: AppPackage & {
    metadata?: Record<string, unknown>;
    manifest?: Record<string, unknown>;
  };
  edit: {
    field: string;
    before: string;
    after: string;
    themeAccent: string;
    refreshIntervalSeconds: number;
  };
  publish: {
    itemId: string;
    url: string;
    visibility: "private";
    reopenRequiresGeneration: boolean;
  };
}

type FailureCode =
  | "unsupported-capability"
  | "auth-denial"
  | "oversized-estimate"
  | "missing-binding"
  | "apply-failure";

interface FailureFixture {
  id: string;
  kind: "failure";
  ticket: string;
  mode: "model-free";
  prompt: string;
  failure: {
    code: FailureCode;
    stage: "prompt" | "spec-review" | "plan-review" | "preview-validation" | "apply";
    title: string;
    message: string;
    expectedSurface: string;
    owner: "portal" | "server" | "sdk" | "admin" | "deploy";
    attributedOwner: "portal" | "server" | "sdk" | "admin" | "deploy";
    canRetryWithoutModel: boolean;
    jobId?: string;
  };
  evidence: {
    selector: string;
    screenshot: string;
  };
}

interface FixtureCounters {
  chat: number;
  clarify: number;
  getPlan: number;
  revisePlan: number;
  submitPlan: number;
  refineApp: number;
  liveModelCalls: number;
}

interface EvidenceStep {
  id: ProofStepId;
  status: "pass";
  outputs: Record<string, string>;
}

interface SuccessEvidence {
  fixtureId: string;
  generatedPackageId: string;
  mapPackageId: string;
  manifestVersion: string;
  publishedItemId: string;
  previewUrl: string;
  editedTitle: string;
  reopenedTitle: string;
  runtimeWidgetIds: string[];
  runtimeChangeEvents: string[];
  counters: FixtureCounters;
  steps: EvidenceStep[];
  /**
   * Branch of the Console chart adapter that rendered the dashboard's chart
   * widget. "vega-lite" is the Console default established by honua-console#5;
   * fixtures without a Vega-Lite spec fall back to the deterministic CSS bars.
   */
  chartSpec: "vega-lite" | "css-bars";
}

interface PublishedAppRecord {
  itemId: string;
  url: string;
  visibility: "private";
  packageId: string;
  manifestVersion: string;
  appPackage: AppPackage;
  sourceSavedMapId: string;
  publishedAt: string;
}

class FixtureJobRun<T> implements IJobRun<T> {
  readonly id: string;
  readonly type = "app-builder-proof";
  #status: JobStatus = "accepted";
  #progress: JobProgress | undefined;
  #snapshotIndex = 0;
  readonly #snapshots: JobSnapshot<T>[];
  readonly #listeners = new Set<JobSnapshotListener<T>>();

  public constructor(id: string, snapshots: JobSnapshot<T>[]) {
    this.id = id;
    this.#snapshots = snapshots;
  }

  public get status(): JobStatus {
    return this.#status;
  }

  public get progress(): JobProgress | undefined {
    return this.#progress;
  }

  public async poll(): Promise<JobSnapshot<T>> {
    const snapshot = this.#snapshots[Math.min(this.#snapshotIndex, this.#snapshots.length - 1)];
    this.#commit(snapshot);
    this.#snapshotIndex += 1;
    return snapshot;
  }

  public watch(listener: JobSnapshotListener<T>): () => void {
    this.#listeners.add(listener);
    return () => this.#listeners.delete(listener);
  }

  public async results(): Promise<JobResult<T>> {
    for (const snapshot of this.#snapshots) {
      await Promise.resolve();
      this.#commit(snapshot);
      for (const listener of this.#listeners) listener(snapshot);
      if (!isJobTerminal(snapshot.status)) continue;
      if (snapshot.status === "successful" && snapshot.result) return snapshot.result;
      throw new Error(snapshot.error?.message ?? `job ${this.id} ended as ${snapshot.status}`);
    }
    throw new Error(`job ${this.id} did not reach a terminal state`);
  }

  public async cancel(): Promise<JobStatus> {
    this.#status = "dismissed";
    return this.#status;
  }

  #commit(snapshot: JobSnapshot<T>): void {
    this.#status = snapshot.status;
    this.#progress = snapshot.progress;
  }
}

test.describe("App-builder proof smoke/eval (honua-console#5)", () => {
  test("model-free operations-dashboard success path", async ({ page }) => {
    const fixture = await loadJsonFixture<SuccessFixture>("success.json");
    const evidence = await runSuccessFlow(fixture);
    const artifactDir = await createArtifactDir("success");

    await renderSuccessEvidence(page, evidence);
    for (const step of PROOF_STEPS) {
      await expect(page.getByTestId(`proof-${step}`)).toBeVisible();
    }
    for (const widgetId of evidence.runtimeWidgetIds) {
      await expect(page.getByTestId(`runtime-widget-${widgetId}`)).toBeVisible();
    }
    await expect(page.getByTestId("proof-reopen")).toContainText(evidence.publishedItemId);

    const screenshot = path.join(artifactDir, "success-path.png");
    await page.screenshot({ path: screenshot, fullPage: true });
    await writeManifest(artifactDir, {
      schemaVersion: 1,
      ticket: fixture.ticket,
      result: "pass",
      fixtureIds: [fixture.id],
      model: {
        mode: fixture.mode,
        liveModelCalls: evidence.counters.liveModelCalls,
      },
      generatedPackageIds: [evidence.generatedPackageId, evidence.mapPackageId],
      manifestVersion: evidence.manifestVersion,
      publishedItemId: evidence.publishedItemId,
      screenshots: [repoRelative(screenshot)],
      steps: evidence.steps,
      runtime: {
        package: fixture.spec.runtime.package,
        entrypoints: fixture.spec.runtime.entrypoints,
        widgets: evidence.runtimeWidgetIds,
        changeEvents: evidence.runtimeChangeEvents,
        chartSpec: evidence.chartSpec,
      },
    });

    expect(evidence.counters.liveModelCalls).toBe(0);
    expect(evidence.counters.refineApp).toBe(0);
    expect(evidence.chartSpec).toBe("vega-lite");
  });

  test("failure fixtures cover named proof states", async ({ page }) => {
    const fixtures = await Promise.all(FAILURE_FIXTURES.map((name) => loadJsonFixture<FailureFixture>(name)));
    assertFailureCoverage(fixtures);

    const artifactDir = await createArtifactDir("failures");
    await renderFailureEvidence(page, fixtures);
    for (const fixture of fixtures) {
      await expect(page.getByTestId(fixture.evidence.selector)).toBeVisible();
      await expect(page.getByTestId(fixture.evidence.selector)).toContainText(fixture.failure.title);
    }

    const screenshot = path.join(artifactDir, "failure-states.png");
    await page.screenshot({ path: screenshot, fullPage: true });
    await writeManifest(artifactDir, {
      schemaVersion: 1,
      ticket: "honua-console#5",
      result: "pass",
      fixtureIds: fixtures.map((fixture) => fixture.id),
      model: {
        mode: "model-free",
        liveModelCalls: 0,
      },
      failureStates: fixtures.map((fixture) => ({
        id: fixture.id,
        code: fixture.failure.code,
        stage: fixture.failure.stage,
        attributedOwner: fixture.failure.attributedOwner,
        selector: fixture.evidence.selector,
      })),
      screenshots: [repoRelative(screenshot)],
    });
  });
});

async function runSuccessFlow(fixture: SuccessFixture): Promise<SuccessEvidence> {
  assertModelFreeFixture(fixture);
  assertRuntimeWidgets(fixture);

  const steps: EvidenceStep[] = [];
  const { client, counters } = createFixtureClient(fixture);
  const chat = new ChatController({
    client,
    generateTurnId: fixedIdGenerator("turn"),
    now: fixedClock(),
  });
  const agentTurn = await chat.send(fixture.prompt);
  const intent = agentTurn.intentDraft;
  expect(intent?.id).toBe(fixture.intent.id);
  expect(intent?.kind).toBe("builder");
  steps.push(passStep("prompt", { fixtureId: fixture.id, intentId: fixture.intent.id }));

  const clarification = new ClarificationController({ client });
  clarification.load(fixture.intent);
  expect(clarification.state.fields.length).toBeGreaterThan(0);
  for (const answer of fixture.clarificationAnswers) {
    clarification.setAnswer(answer.fieldId, answer.value);
  }
  const clarifiedIntent = await clarification.submit();
  expect(clarifiedIntent.id).toBe(fixture.clarifiedIntent.id);
  steps.push(passStep("clarification", { answeredFields: String(fixture.clarificationAnswers.length) }));

  steps.push(
    passStep("spec-review", {
      specId: fixture.spec.id,
      manifestVersion: fixture.spec.manifestVersion,
      widgetCount: String(fixture.spec.widgets.length),
    }),
  );

  const planReview = new PlanReviewController({ client });
  const plan = await planReview.load(clarifiedIntent.id);
  expect(plan.kind).toBe("builder");
  expect(plan.steps.some((step) => step.kind === "apply-builder-plan")).toBe(true);
  const acceptedPlan = planReview.accept();
  steps.push(passStep("plan-review", { planId: plan.id, stepCount: String(plan.steps.length) }));

  const execution = new ExecutionController({ client });
  const executionResult = waitForExecution(execution);
  await execution.start(acceptedPlan);
  const result = await executionResult;
  expect(result.kind).toBe("builder");
  if (!result.appPackage || !result.mapPackage) {
    throw new Error("success fixture did not return both appPackage and mapPackage");
  }
  steps.push(passStep("apply", { executionId: fixture.execution.id, status: "successful" }));

  const builder = new BuilderWorkspaceController({ client });
  builder.bindIntent(clarifiedIntent.id);
  await builder.loadPackage(result.appPackage);
  builder.bindMapPackage(result.mapPackage);
  const preview = builder.preview();
  expect(preview.url).toBe(fixture.appPackage.assets[0]?.url);
  expect(preview.mapPackage?.mapPackageId).toBe(fixture.mapPackage.mapPackageId);

  const runtimeChangeEvents = await exerciseSdkExplorationRuntime(fixture);
  expect(runtimeChangeEvents).toEqual([...RUNTIME_CHANGE_EVENT_ORDER]);
  steps.push(
    passStep("preview", {
      generatedPackageId: result.appPackage.id,
      mapPackageId: result.mapPackage.mapPackageId,
      runtimeWidgetCount: String(fixture.spec.widgets.length),
    }),
  );

  const editedPackage = applyDirectEdit(result.appPackage, fixture);
  await builder.loadPackage(editedPackage);
  const editedTitle = readPackageTitle(editedPackage);
  expect(editedTitle).toBe(fixture.edit.after);
  steps.push(passStep("edit", { editedField: fixture.edit.field, editedTitle }));

  const store = new Map<string, PublishedAppRecord>();
  const published = publishFixtureApp(store, editedPackage, fixture);
  steps.push(
    passStep("publish", {
      publishedItemId: published.itemId,
      visibility: published.visibility,
      manifestVersion: published.manifestVersion,
    }),
  );

  const generationCallsBeforeReopen = countGenerationCalls(counters);
  const reopened = reopenPublishedApp(store, fixture.publish.itemId);
  expect(reopened.packageId).toBe(editedPackage.id);
  expect(countGenerationCalls(counters)).toBe(generationCallsBeforeReopen);
  const reopenedTitle = readPackageTitle(reopened.appPackage);
  steps.push(
    passStep("reopen", {
      publishedItemId: reopened.itemId,
      reopenedWithoutGeneration: String(countGenerationCalls(counters) === generationCallsBeforeReopen),
    }),
  );

  return {
    fixtureId: fixture.id,
    generatedPackageId: result.appPackage.id,
    mapPackageId: result.mapPackage.mapPackageId,
    manifestVersion: result.appPackage.version,
    publishedItemId: published.itemId,
    previewUrl: preview.url ?? "",
    editedTitle,
    reopenedTitle,
    runtimeWidgetIds: fixture.spec.widgets.map((widget) => widget.id),
    runtimeChangeEvents,
    counters,
    steps,
    chartSpec: detectChartSpec(fixture),
  };
}

/**
 * Branch selector for the Console chart adapter. The success fixture flags the
 * Vega-Lite branch by including a `runtime.chartSpec` declaration; if absent
 * the deterministic CSS bar fallback is assumed.
 */
function detectChartSpec(fixture: SuccessFixture): "vega-lite" | "css-bars" {
  const declared = (fixture.spec as unknown as { runtime?: { chartSpec?: string } }).runtime?.chartSpec;
  return declared === "vega-lite" ? "vega-lite" : "css-bars";
}

function createFixtureClient(fixture: SuccessFixture): { client: OperatorClient; counters: FixtureCounters } {
  const counters: FixtureCounters = {
    chat: 0,
    clarify: 0,
    getPlan: 0,
    revisePlan: 0,
    submitPlan: 0,
    refineApp: 0,
    liveModelCalls: 0,
  };
  const client: OperatorClient = {
    operator: {
      async *chat(text: string): AsyncIterable<ChatChunk> {
        counters.chat += 1;
        expect(text).toBe(fixture.prompt);
        yield { turnId: "fixture-turn-agent", delta: "Using deterministic app-builder fixture. ", done: false };
        yield {
          turnId: "fixture-turn-agent",
          delta: "One clarification is required.",
          done: true,
          intentDraft: fixture.intent,
        };
      },
      async clarify(intentId: string, answers: ReadonlyArray<ClarificationAnswer>): Promise<BuilderIntent> {
        counters.clarify += 1;
        expect(intentId).toBe(fixture.intent.id);
        expect(answers).toEqual(fixture.clarificationAnswers);
        return fixture.clarifiedIntent;
      },
      async getPlan(intentId: string): Promise<BuilderPlan> {
        counters.getPlan += 1;
        expect(intentId).toBe(fixture.clarifiedIntent.id);
        return fixture.plan;
      },
      async revisePlan(): Promise<BuilderPlan> {
        counters.revisePlan += 1;
        return fixture.plan;
      },
      async submitPlan(plan: OperatorPlan): Promise<IJobRun<ExecutionResult>> {
        counters.submitPlan += 1;
        expect(plan.id).toBe(fixture.plan.id);
        const result: ExecutionResult = {
          kind: "builder",
          summary: "Fixture app package ready",
          mapPackage: fixture.mapPackage,
          appPackage: fixture.appPackage,
          artifacts: fixture.appPackage.assets,
          provenance: [
            {
              step: "fixture-apply",
              tool: "app-builder-proof-fixture",
              startedAt: 1760000000000,
              finishedAt: 1760000000001,
            },
          ],
        };
        const snapshots: JobSnapshot<ExecutionResult>[] = [
          { status: "accepted", progress: fixture.execution.progress[0] },
          { status: "running", progress: fixture.execution.progress[1] },
          {
            status: "successful",
            progress: fixture.execution.progress[2],
            result: { outputs: { [OPERATOR_EXECUTION_OUTPUT_KEY]: result } },
          },
        ];
        return new FixtureJobRun(fixture.execution.id, snapshots);
      },
      async refineApp(): Promise<AppPackage> {
        counters.refineApp += 1;
        return fixture.appPackage;
      },
      async refineMap(): Promise<HonuaMapPackage> {
        return fixture.mapPackage;
      },
      async getApproval(operationId: string): Promise<ApprovalDecision> {
        return approvalDecision(operationId);
      },
      async confirmApproval(operationId: string): Promise<ApprovalDecision> {
        return approvalDecision(operationId);
      },
    },
  };
  return { client, counters };
}

function waitForExecution(execution: ExecutionController): Promise<ExecutionResult> {
  return new Promise((resolve, reject) => {
    let unsubscribe = (): void => {};
    unsubscribe = execution.on((event) => {
      if (event.kind === "successful") {
        unsubscribe();
        resolve(event.result);
      }
      if (event.kind === "failed" || event.kind === "error") {
        unsubscribe();
        reject(event.error);
      }
    });
  });
}

async function exerciseSdkExplorationRuntime(fixture: SuccessFixture): Promise<string[]> {
  const context = createExplorationContext({
    datasetId: fixture.spec.datasetId,
    sourceIds: fixture.source.sourceIds,
    preset: "globalLinked",
  });
  const handles = fixture.spec.widgets.map((widget) => context.bind({ id: widget.id, role: widget.role }));
  const changeEvents = new Set<string>();
  const unsubscribe = context.subscribe("all", (event) => {
    for (const slice of event.changedSlices) {
      changeEvents.add(slice);
    }
  });

  context.dispatch({
    kind: "set-filter",
    viewId: "district-filter",
    id: "district",
    clause: {
      field: "district",
      operator: "=",
      value: "Central",
      appliesTo: ["incidents"],
    },
  });
  context.dispatch({ kind: "set-grouping", viewId: "incidents-by-type", grouping: ["incident_type"] });
  context.dispatch({ kind: "select", viewId: "incident-list", ids: ["incident-001"], replace: true });

  await Promise.resolve();
  await Promise.resolve();

  expect(context.state.filters.district?.field).toBe("district");
  expect(context.state.grouping).toEqual(["incident_type"]);
  expect(context.state.selection).toEqual(["incident-001"]);

  unsubscribe();
  for (const handle of handles) handle.unbind();
  context.dispose();
  return RUNTIME_CHANGE_EVENT_ORDER.filter((slice) => changeEvents.has(slice));
}

function applyDirectEdit(appPackage: AppPackage, fixture: SuccessFixture): AppPackage {
  const metadata = asRecord(appPackage.metadata);
  const manifest = asRecord((appPackage as { manifest?: unknown }).manifest);
  return {
    ...appPackage,
    metadata: {
      ...metadata,
      title: fixture.edit.after,
      refreshIntervalSeconds: fixture.edit.refreshIntervalSeconds,
      themeAccent: fixture.edit.themeAccent,
    },
    manifest: {
      ...manifest,
      title: fixture.edit.after,
      refreshIntervalSeconds: fixture.edit.refreshIntervalSeconds,
      theme: {
        accent: fixture.edit.themeAccent,
      },
    },
  };
}

function publishFixtureApp(
  store: Map<string, PublishedAppRecord>,
  appPackage: AppPackage,
  fixture: SuccessFixture,
): PublishedAppRecord {
  const record: PublishedAppRecord = {
    itemId: fixture.publish.itemId,
    url: fixture.publish.url,
    visibility: fixture.publish.visibility,
    packageId: appPackage.id,
    manifestVersion: appPackage.version,
    appPackage,
    sourceSavedMapId: fixture.source.savedMapId,
    publishedAt: "2026-05-08T00:00:00.000Z",
  };
  store.set(record.itemId, record);
  return record;
}

function reopenPublishedApp(store: Map<string, PublishedAppRecord>, itemId: string): PublishedAppRecord {
  const record = store.get(itemId);
  if (!record) throw new Error(`published item ${itemId} was not found`);
  return record;
}

function assertModelFreeFixture(fixture: Pick<SuccessFixture | FailureFixture, "mode">): void {
  expect(fixture.mode).toBe("model-free");
  expect(JSON.stringify(fixture).toLowerCase()).not.toContain("openai");
  expect(JSON.stringify(fixture).toLowerCase()).not.toContain("anthropic");
  expect(JSON.stringify(fixture).toLowerCase()).not.toContain("modelprovider");
}

function assertRuntimeWidgets(fixture: SuccessFixture): void {
  const widgetKinds = new Set(fixture.spec.widgets.map((widget) => widget.kind));
  expect(widgetKinds).toEqual(new Set<WidgetKind>(["map", "list", "indicator", "chart", "filter"]));
  for (const widget of fixture.spec.widgets) {
    expect(widget.runtime).toBe(SDK_WIDGET_RUNTIME);
  }
}

function assertFailureCoverage(fixtures: FailureFixture[]): void {
  const expectedCodes: FailureCode[] = [
    "unsupported-capability",
    "auth-denial",
    "oversized-estimate",
    "missing-binding",
    "apply-failure",
  ];
  expect(fixtures.map((fixture) => fixture.failure.code).sort()).toEqual([...expectedCodes].sort());
  for (const fixture of fixtures) {
    assertModelFreeFixture(fixture);
    expect(fixture.failure.message).not.toHaveLength(0);
    expect(fixture.evidence.selector).toMatch(/^failure-/);
  }
}

async function renderSuccessEvidence(page: Page, evidence: SuccessEvidence): Promise<void> {
  const stepCards = evidence.steps
    .map(
      (step) => `
        <section class="proof-card" data-testid="proof-${step.id}">
          <span class="proof-kicker">${escapeHtml(step.id)}</span>
          <h2>${escapeHtml(stepTitle(step.id))}</h2>
          <dl>${Object.entries(step.outputs)
            .map(([key, value]) => `<div><dt>${escapeHtml(key)}</dt><dd>${escapeHtml(value)}</dd></div>`)
            .join("")}</dl>
        </section>
      `,
    )
    .join("");
  const widgets = evidence.runtimeWidgetIds
    .map(
      (id) => `
        <li data-testid="runtime-widget-${escapeHtml(id)}">
          <strong>${escapeHtml(id)}</strong>
          <span>${SDK_WIDGET_RUNTIME}</span>
        </li>
      `,
    )
    .join("");

  await page.setContent(`
    <!doctype html>
    <html lang="en">
      <head>
        <meta charset="utf-8" />
        <title>App-builder proof evidence</title>
        <style>
          body {
            margin: 0;
            background: #f6f8f7;
            color: #17201d;
            font-family: Inter, ui-sans-serif, system-ui, -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif;
          }
          main {
            max-width: 1180px;
            margin: 0 auto;
            padding: 32px;
          }
          header {
            margin-bottom: 24px;
          }
          h1 {
            margin: 0 0 8px;
            font-size: 30px;
          }
          h2 {
            margin: 4px 0 12px;
            font-size: 18px;
          }
          .proof-grid {
            display: grid;
            grid-template-columns: repeat(3, minmax(0, 1fr));
            gap: 14px;
          }
          .proof-card,
          .proof-runtime {
            border: 1px solid #d9e1dd;
            border-radius: 8px;
            background: #fff;
            padding: 16px;
          }
          .proof-kicker {
            color: #52625c;
            font-size: 12px;
            font-weight: 700;
            letter-spacing: 0;
            text-transform: uppercase;
          }
          dl {
            display: grid;
            gap: 8px;
            margin: 0;
          }
          dl div {
            display: grid;
            grid-template-columns: 140px 1fr;
            gap: 8px;
          }
          dt {
            color: #5d6b66;
            font-size: 12px;
          }
          dd {
            margin: 0;
            overflow-wrap: anywhere;
            font-size: 13px;
          }
          .proof-runtime {
            margin-top: 14px;
          }
          ul {
            display: grid;
            grid-template-columns: repeat(5, minmax(0, 1fr));
            gap: 10px;
            padding: 0;
            margin: 0;
            list-style: none;
          }
          li {
            border: 1px solid #d9e1dd;
            border-radius: 8px;
            padding: 10px;
          }
          li span {
            display: block;
            margin-top: 4px;
            color: #5d6b66;
            font-size: 12px;
            overflow-wrap: anywhere;
          }
        </style>
      </head>
      <body>
        <main data-testid="app-builder-proof-success">
          <header>
            <h1>Operations Dashboard App-Builder Proof</h1>
            <p>Fixture ${escapeHtml(evidence.fixtureId)} published ${escapeHtml(
              evidence.publishedItemId,
            )}; live model calls: ${evidence.counters.liveModelCalls}.</p>
          </header>
          <div class="proof-grid">${stepCards}</div>
          <section class="proof-runtime" aria-label="SDK runtime widgets">
            <h2>SDK-JS Runtime Widgets</h2>
            <ul>${widgets}</ul>
          </section>
        </main>
      </body>
    </html>
  `);
}

async function renderFailureEvidence(page: Page, fixtures: FailureFixture[]): Promise<void> {
  const cards = fixtures
    .map(
      (fixture) => `
        <section class="failure-card" data-testid="${escapeHtml(fixture.evidence.selector)}">
          <span>${escapeHtml(fixture.failure.code)}</span>
          <h2>${escapeHtml(fixture.failure.title)}</h2>
          <p>${escapeHtml(fixture.failure.message)}</p>
          <dl>
            <div><dt>Stage</dt><dd>${escapeHtml(fixture.failure.stage)}</dd></div>
            <div><dt>Attributed owner</dt><dd>${escapeHtml(fixture.failure.attributedOwner)}</dd></div>
            <div><dt>Expected surface</dt><dd>${escapeHtml(fixture.failure.expectedSurface)}</dd></div>
          </dl>
        </section>
      `,
    )
    .join("");
  await page.setContent(`
    <!doctype html>
    <html lang="en">
      <head>
        <meta charset="utf-8" />
        <title>App-builder failure proof evidence</title>
        <style>
          body {
            margin: 0;
            background: #f7f7f2;
            color: #1f2320;
            font-family: Inter, ui-sans-serif, system-ui, -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif;
          }
          main {
            max-width: 1120px;
            margin: 0 auto;
            padding: 32px;
          }
          h1 {
            margin: 0 0 20px;
            font-size: 28px;
          }
          .failure-grid {
            display: grid;
            grid-template-columns: repeat(2, minmax(0, 1fr));
            gap: 14px;
          }
          .failure-card {
            border: 1px solid #decfc2;
            border-radius: 8px;
            background: #fff;
            padding: 16px;
          }
          .failure-card span {
            color: #7f3f1f;
            font-size: 12px;
            font-weight: 700;
            letter-spacing: 0;
            text-transform: uppercase;
          }
          h2 {
            margin: 4px 0 8px;
            font-size: 18px;
          }
          p {
            margin: 0 0 12px;
            line-height: 1.45;
          }
          dl {
            display: grid;
            gap: 7px;
            margin: 0;
          }
          dl div {
            display: grid;
            grid-template-columns: 140px 1fr;
            gap: 8px;
          }
          dt {
            color: #60564e;
            font-size: 12px;
          }
          dd {
            margin: 0;
            font-size: 13px;
          }
        </style>
      </head>
      <body>
        <main data-testid="app-builder-proof-failures">
          <h1>Operations Dashboard Failure Fixtures</h1>
          <div class="failure-grid">${cards}</div>
        </main>
      </body>
    </html>
  `);
}

async function loadJsonFixture<T>(name: string): Promise<T> {
  const raw = await readFile(path.join(FIXTURE_DIR, name), "utf8");
  return JSON.parse(raw) as T;
}

async function createArtifactDir(slug: string): Promise<string> {
  const timestamp = new Date().toISOString().replace(/[:.]/g, "-");
  const dir = path.join(ARTIFACT_ROOT, `${timestamp}-${slug}`);
  await mkdir(dir, { recursive: true });
  return dir;
}

async function writeManifest(artifactDir: string, manifest: Record<string, unknown>): Promise<void> {
  await writeFile(path.join(artifactDir, "manifest.json"), `${JSON.stringify(manifest, null, 2)}\n`, "utf8");
}

function passStep(id: ProofStepId, outputs: Record<string, string>): EvidenceStep {
  return { id, status: "pass", outputs };
}

function approvalDecision(operationId: string): ApprovalDecision {
  return {
    operationId,
    state: "granted",
    scope: "app-builder-proof",
    reasons: [],
    requiredRoles: [],
    audit: [],
  };
}

function fixedIdGenerator(prefix: string): () => string {
  let next = 0;
  return () => {
    next += 1;
    return `${prefix}-${next}`;
  };
}

function fixedClock(): () => number {
  let next = 1760000000000;
  return () => {
    next += 1;
    return next;
  };
}

function countGenerationCalls(counters: FixtureCounters): number {
  return (
    counters.chat + counters.clarify + counters.getPlan + counters.submitPlan + counters.revisePlan + counters.refineApp
  );
}

function readPackageTitle(appPackage: AppPackage): string {
  const metadataTitle = asRecord(appPackage.metadata).title;
  if (typeof metadataTitle === "string") return metadataTitle;
  const manifestTitle = asRecord((appPackage as { manifest?: unknown }).manifest).title;
  return typeof manifestTitle === "string" ? manifestTitle : appPackage.id;
}

function asRecord(value: unknown): Record<string, unknown> {
  return value && typeof value === "object" && !Array.isArray(value) ? (value as Record<string, unknown>) : {};
}

function repoRelative(filePath: string): string {
  return path.relative(process.cwd(), filePath).replaceAll(path.sep, "/");
}

function stepTitle(step: ProofStepId): string {
  switch (step) {
    case "prompt":
      return "Prompt Captured";
    case "clarification":
      return "Clarification Answered";
    case "spec-review":
      return "Spec Reviewed";
    case "plan-review":
      return "Plan Reviewed";
    case "apply":
      return "Apply Completed";
    case "preview":
      return "Preview Rendered";
    case "edit":
      return "Direct Edit Applied";
    case "publish":
      return "Private App Published";
    case "reopen":
      return "Published App Reopened";
  }
}

function escapeHtml(value: string): string {
  return value
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;")
    .replaceAll("'", "&#039;");
}
