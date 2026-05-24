import { useMemo, useState } from "react";

import type {
  ProcessServicePublication,
  PublishedWorkflowContentItem,
  StudioWorkflowTransport,
  WorkflowDefinitionPayload,
  WorkflowDraft,
  WorkflowPublicationRequest,
  WorkflowRunMode,
  WorkflowRunRecord,
  WorkflowValidationResult,
} from "./types";
import {
  getProcessServiceEligibility,
  isFiveFieldCron,
  isWorkflowDefinitionPayload,
} from "./workflowContracts";

import "./workflow-editor.css";

const STARTER_PROMPT =
  "Import shoreline permits from CSV, buffer protected habitats by 500 meters, flag intersecting permits, and publish a scheduled review package.";

interface StudioWorkflowEditorProps {
  transport: StudioWorkflowTransport;
}

export function StudioWorkflowEditor({ transport }: StudioWorkflowEditorProps): JSX.Element {
  const [prompt, setPrompt] = useState(STARTER_PROMPT);
  const [draft, setDraft] = useState<WorkflowDraft | undefined>();
  const [definitionJson, setDefinitionJson] = useState("");
  const [validation, setValidation] = useState<WorkflowValidationResult | undefined>();
  const [runs, setRuns] = useState<WorkflowRunRecord[]>([]);
  const [publishedItem, setPublishedItem] = useState<PublishedWorkflowContentItem | undefined>();
  const [processService, setProcessService] = useState<ProcessServicePublication | undefined>();
  const [scheduleEnabled, setScheduleEnabled] = useState(false);
  const [cronExpression, setCronExpression] = useState("0 2 * * *");
  const [busy, setBusy] = useState<string | undefined>();
  const [error, setError] = useState<string | undefined>();

  const parsedDefinition = useMemo(() => parseDefinition(definitionJson), [definitionJson]);
  const definition =
    parsedDefinition.ok && isWorkflowDefinitionPayload(parsedDefinition.value) ? parsedDefinition.value : undefined;
  const serviceEligibility = definition ? getProcessServiceEligibility(definition) : undefined;
  const canValidate = parsedDefinition.ok && !busy;
  const canPublishDefinition =
    Boolean(definition && validation && validation.status !== "blocked" && !busy) &&
    (!scheduleEnabled || isFiveFieldCron(cronExpression));
  const canPublishProcessService = Boolean(
    definition && validation && validation.status !== "blocked" && serviceEligibility?.eligible && !busy,
  );
  const processServiceEligible = definition ? serviceEligibility?.eligible === true : false;

  async function generateDraft(): Promise<void> {
    await act("draft", async () => {
      const next = await transport.createDraftFromPrompt(prompt);
      setDraft(next);
      setDefinitionJson(JSON.stringify(next.definition, null, 2));
      setValidation(undefined);
      setRuns([]);
      setPublishedItem(undefined);
      setProcessService(undefined);
    });
  }

  async function validateDraft(): Promise<void> {
    if (!parsedDefinition.ok) {
      setError(definitionParseMessage(parsedDefinition));
      return;
    }
    await act("validate", async () => {
      setValidation(await transport.validateDefinition(parsedDefinition.value));
    });
  }

  async function run(mode: WorkflowRunMode): Promise<void> {
    if (!definition) {
      setError(definitionParseMessage(parsedDefinition));
      return;
    }
    await act(mode, async () => {
      const result = await transport.runDefinition(definition, mode);
      setRuns((current) => [result, ...current]);
    });
  }

  async function publishDefinition(): Promise<void> {
    if (!definition) {
      setError(definitionParseMessage(parsedDefinition));
      return;
    }
    const request: WorkflowPublicationRequest = scheduleEnabled
      ? {
          executionMode: "scheduled",
          cronExpression,
          timeZone: "Pacific/Honolulu",
        }
      : { executionMode: "manual" };
    await act("publish", async () => {
      setPublishedItem(await transport.publishDefinition(definition, request));
    });
  }

  async function publishProcessService(): Promise<void> {
    if (!definition) {
      setError(definitionParseMessage(parsedDefinition));
      return;
    }
    await act("service", async () => {
      setProcessService(await transport.publishProcessService(definition));
    });
  }

  async function rollback(versionId: string): Promise<void> {
    if (!publishedItem) return;
    await act("rollback", async () => {
      setPublishedItem(await transport.rollbackContentItem(publishedItem, versionId));
    });
  }

  async function act(label: string, action: () => Promise<void>): Promise<void> {
    setBusy(label);
    setError(undefined);
    try {
      await action();
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : String(caught));
    } finally {
      setBusy(undefined);
    }
  }

  function updateDefinitionJson(value: string): void {
    setDefinitionJson(value);
    setValidation(undefined);
    setProcessService(undefined);
  }

  return (
    <section className="workflow-editor" aria-labelledby="workflow-editor-title">
      <header className="workflow-header">
        <div>
          <p className="workflow-kicker">Studio</p>
          <h1 id="workflow-editor-title">Unified GP and ETL workflow editor</h1>
          <p className="workflow-subtitle">
            Draft, inspect, validate, run, and publish Honua workflow definitions through server-owned contracts.
          </p>
        </div>
        <div className="workflow-actions" aria-label="Workflow actions">
          <button disabled={Boolean(busy)} onClick={generateDraft} type="button">
            Generate Draft
          </button>
          <button disabled={!canValidate} onClick={validateDraft} type="button">
            Validate
          </button>
          <button disabled={!definition || Boolean(busy)} onClick={() => run("dry-run")} type="button">
            Dry Run
          </button>
          <button disabled={!definition || Boolean(busy)} onClick={() => run("sample-run")} type="button">
            Sample Run
          </button>
        </div>
      </header>

      {error ? (
        <div className="workflow-error" role="alert">
          {error}
        </div>
      ) : null}

      <div className="workflow-grid">
        <section className="workflow-panel prompt-panel" aria-label="Natural language workflow draft">
          <div className="panel-heading">
            <div>
              <h2>Natural Language</h2>
              <p>Start from a builder prompt and review the generated contract before execution.</p>
            </div>
            <span className="status-pill">{draft ? "Draft ready" : "No draft"}</span>
          </div>
          <textarea
            aria-label="Workflow prompt"
            className="prompt-input"
            onChange={(event) => setPrompt(event.target.value)}
            value={prompt}
          />
          {draft ? (
            <div className="draft-summary">
              <span>{draft.definition.mode}</span>
              <span>{draft.generatedContract.join(" / ")}</span>
              <span>{processServiceEligible ? "Process service eligible" : "Batch only"}</span>
            </div>
          ) : null}
        </section>

        <section className="workflow-panel definition-panel" aria-label="Generated workflow definition">
          <div className="panel-heading">
            <div>
              <h2>Generated Definition</h2>
              <p>Editable workflow view model. Validation uses the same structural contract vocabulary.</p>
            </div>
            <span className={parsedDefinition.ok ? "status-pill ok" : "status-pill blocked"}>
              {parsedDefinition.ok ? "JSON parsed" : "JSON invalid"}
            </span>
          </div>
          <textarea
            aria-label="Workflow definition JSON"
            className="definition-input"
            onChange={(event) => updateDefinitionJson(event.target.value)}
            spellCheck={false}
            value={definitionJson}
          />
        </section>
      </div>

      <div className="workflow-grid lower-grid">
        <section className="workflow-panel" aria-label="Workflow graph">
          <div className="panel-heading">
            <div>
              <h2>Workflow Graph</h2>
              <p>Source, transform, sink, process, artifact, and publication nodes from the contract.</p>
            </div>
          </div>
          {definition ? (
            <WorkflowGraph definition={definition} />
          ) : (
            <EmptyMessage
              message={
                parsedDefinition.ok
                  ? "Run validation to inspect contract issues before nodes render."
                  : "Generate a draft to inspect nodes."
              }
            />
          )}
        </section>

        <section className="workflow-panel" aria-label="Validation findings">
          <div className="panel-heading">
            <div>
              <h2>Validation</h2>
              <p>Missing parameters, unsupported transforms, sink constraints, and permission problems.</p>
            </div>
            <span className={validation?.status === "valid" ? "status-pill ok" : "status-pill"}>
              {validation?.status ?? "not checked"}
            </span>
          </div>
          {validation ? <ValidationList validation={validation} /> : <EmptyMessage message="Run validation before execution." />}
        </section>
      </div>

      <div className="workflow-grid lower-grid">
        <section className="workflow-panel" aria-label="Run history">
          <div className="panel-heading">
            <div>
              <h2>Runs</h2>
              <p>Dry-run and sample-run history with job status, logs, artifacts, and row failures.</p>
            </div>
          </div>
          <RunHistory runs={runs} />
        </section>

        <section className="workflow-panel" aria-label="Publication">
          <div className="panel-heading">
            <div>
              <h2>Publication</h2>
              <p>Promote validated definitions to reusable jobs or eligible process services.</p>
            </div>
          </div>
          <div className="publish-controls">
            <label className="toggle-row">
              <input
                checked={scheduleEnabled}
                onChange={(event) => setScheduleEnabled(event.target.checked)}
                type="checkbox"
              />
              <span>Enable scheduled execution</span>
            </label>
            <input
              aria-label="Schedule cron expression"
              disabled={!scheduleEnabled}
              onChange={(event) => setCronExpression(event.target.value)}
              value={cronExpression}
            />
            <button disabled={!canPublishDefinition} onClick={publishDefinition} type="button">
              Publish Batch Definition
            </button>
            <button disabled={!canPublishProcessService} onClick={publishProcessService} type="button">
              Publish Process Service
            </button>
          </div>
          <PublicationSummary item={publishedItem} onRollback={rollback} service={processService} />
        </section>
      </div>
    </section>
  );
}

function WorkflowGraph({ definition }: { definition: WorkflowDefinitionPayload }): JSX.Element {
  return (
    <ol className="node-list">
      {definition.steps.map((step) => (
        <li className="workflow-node" key={step.stepId}>
          <div>
            <span className="node-kind">{step.nodeKind}</span>
            <h3>{step.label}</h3>
            <p>{step.processId ?? step.plan.planId}</p>
          </div>
          <dl>
            <div>
              <dt>Depends</dt>
              <dd>{step.dependsOn.length ? step.dependsOn.join(", ") : "none"}</dd>
            </div>
            <div>
              <dt>Outputs</dt>
              <dd>{step.plan.outputs.join(", ")}</dd>
            </div>
          </dl>
        </li>
      ))}
    </ol>
  );
}

function ValidationList({ validation }: { validation: WorkflowValidationResult }): JSX.Element {
  if (validation.issues.length === 0) {
    return <EmptyMessage message="Definition is valid against the workflow contract checks." />;
  }

  return (
    <ul className="issue-list">
      {validation.issues.map((issue) => (
        <li className={issue.severity} key={`${issue.code}-${issue.path}-${issue.nodeId ?? "workflow"}`}>
          <span>{issue.kind}</span>
          <strong>{issue.message}</strong>
          <small>{issue.requiredAction}</small>
        </li>
      ))}
    </ul>
  );
}

function RunHistory({ runs }: { runs: readonly WorkflowRunRecord[] }): JSX.Element {
  if (runs.length === 0) {
    return <EmptyMessage message="No dry-run or sample-run history yet." />;
  }

  return (
    <div className="run-stack">
      {runs.map((run) => (
        <article className="run-record" key={run.runId}>
          <div className="run-title">
            <div>
              <strong>{run.mode}</strong>
              <span>{run.runId}</span>
            </div>
            <span className={run.status === "successful" ? "status-pill ok" : "status-pill blocked"}>{run.status}</span>
          </div>
          <dl className="run-metrics">
            <div>
              <dt>Snapshots</dt>
              <dd>{run.snapshots.map((snapshot) => snapshot.status).join(" -> ")}</dd>
            </div>
            <div>
              <dt>Artifacts</dt>
              <dd>{run.artifacts.length}</dd>
            </div>
            <div>
              <dt>Row failures</dt>
              <dd>{run.featureFailures.length}</dd>
            </div>
          </dl>
          <div className="run-columns">
            <div>
              <h3>Logs</h3>
              <ul>
                {run.logs.map((entry) => (
                  <li key={`${entry.at}-${entry.message}`}>{entry.message}</li>
                ))}
              </ul>
            </div>
            <div>
              <h3>Artifacts</h3>
              <ul>
                {run.artifacts.map((artifact) => (
                  <li key={artifact.artifactId}>
                    {artifact.title} <span>{artifact.kind}</span>
                  </li>
                ))}
              </ul>
            </div>
            <div>
              <h3>Rejected Rows</h3>
              <ul>
                {run.featureFailures.map((failure) => (
                  <li key={`${failure.row}-${failure.code}`}>Row {failure.row}: {failure.code}</li>
                ))}
              </ul>
            </div>
          </div>
        </article>
      ))}
    </div>
  );
}

function PublicationSummary({
  item,
  service,
  onRollback,
}: {
  item: PublishedWorkflowContentItem | undefined;
  service: ProcessServicePublication | undefined;
  onRollback(versionId: string): Promise<void>;
}): JSX.Element {
  if (!item && !service) {
    return <EmptyMessage message="Publish a workflow or process service to see content metadata." />;
  }

  return (
    <div className="publication-stack">
      {item ? (
        <article className="publication-record">
          <h3>{item.title}</h3>
          <p>{item.href}</p>
          <dl>
            <div>
              <dt>Modes</dt>
              <dd>{item.executionModes.join(", ")}</dd>
            </div>
            <div>
              <dt>Run history</dt>
              <dd>{item.runHistoryHref}</dd>
            </div>
            <div>
              <dt>Hash</dt>
              <dd>{item.provenance.definitionHash}</dd>
            </div>
          </dl>
          <div className="version-list">
            {item.versions.map((version) => (
              <button
                disabled={!version.rollbackAvailable || item.activeVersionId === version.versionId}
                key={version.versionId}
                onClick={() => void onRollback(version.versionId)}
                type="button"
              >
                {item.activeVersionId === version.versionId ? `${version.versionId} active` : `Rollback ${version.versionId}`}
              </button>
            ))}
          </div>
        </article>
      ) : null}
      {service ? (
        <article className="publication-record service-record">
          <h3>{service.title}</h3>
          <p>{service.stableInvocationRoute}</p>
          <dl>
            <div>
              <dt>Parameters</dt>
              <dd>{service.parameterMetadata.map((parameter) => parameter.name).join(", ")}</dd>
            </div>
            <div>
              <dt>Result package</dt>
              <dd>{service.resultPackageMetadata.resultPackageId}</dd>
            </div>
            <div>
              <dt>Permissions</dt>
              <dd>{service.permissions.join(", ")}</dd>
            </div>
          </dl>
        </article>
      ) : null}
    </div>
  );
}

function EmptyMessage({ message }: { message: string }): JSX.Element {
  return <p className="empty-message">{message}</p>;
}

function parseDefinition(json: string): { ok: true; value: unknown } | { ok: false; message: string } {
  if (!json.trim()) {
    return { ok: false, message: "Generate or paste a workflow definition before continuing." };
  }
  try {
    return { ok: true, value: JSON.parse(json) };
  } catch (caught) {
    return {
      ok: false,
      message: caught instanceof Error ? caught.message : "Workflow definition JSON could not be parsed.",
    };
  }
}

function definitionParseMessage(result: ReturnType<typeof parseDefinition>): string {
  return result.ok ? "Workflow definition JSON must match the server workflow contract before continuing." : result.message;
}
