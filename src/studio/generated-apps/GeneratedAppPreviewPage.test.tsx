import { act, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes, useLocation, useNavigate } from "react-router-dom";
import { describe, expect, it } from "vitest";

import { GeneratedAppLifecycleClientProvider } from "./GeneratedAppLifecycleContext.js";
import { GeneratedAppPreviewPage } from "./GeneratedAppPreviewPage.js";
import type { GeneratedAppLifecycleClient } from "./client.js";
import { buildDefaultGeneratedAppLifecycleRecords } from "./default-client.js";
import { rollbackGeneratedAppItem } from "./lifecycle.js";
import type {
  GeneratedAppLifecycleRecord,
  GeneratedAppPreviewDescriptor,
  GeneratedAppRevisionInput,
  SaveGeneratedAppDraftInput,
} from "./types.js";

const ITEM_A = "app-a";
const ITEM_B = "app-b";

describe("GeneratedAppPreviewPage rollback state", () => {
  it("ignores rollback completion after navigating to another preview", async () => {
    const { itemA, itemB } = buildPreviewRecords();
    const client = new DeferredRollbackClient([itemA, itemB]);
    renderPreviewPage(client, `/studio/apps/${ITEM_A}/preview?revision=rev-002`);

    await expectPreviewPage(ITEM_A, "rev-002");

    await userEvent.click(screen.getByRole("button", { name: "Roll back to Revision 1" }));
    expect(client.rollbackCalls).toEqual([{ itemId: ITEM_A, targetRevisionId: "rev-001" }]);

    await userEvent.click(screen.getByRole("button", { name: "Go to item B" }));
    await expectPreviewPage(ITEM_B, "rev-002");

    const rolledBack = rollbackGeneratedAppItem(itemA.item, "rev-001", {
      consoleBaseUrl: "https://console.honua.example",
      actor: "u-member",
      now: "2026-05-08T18:00:00.000Z",
    });
    client.setRecord(rolledBack);

    await act(async () => {
      client.rollbackDeferred.resolve(rolledBack);
      await client.rollbackDeferred.promise;
    });
    await settle();

    expect(client.getPreviewCalls).toEqual([
      { itemId: ITEM_A, revisionId: "rev-002" },
      { itemId: ITEM_B, revisionId: "rev-002" },
    ]);
    expect(screen.getByTestId("test-location")).toHaveTextContent(`/studio/apps/${ITEM_B}/preview?revision=rev-002`);
    await expectPreviewPage(ITEM_B, "rev-002");
  });

  it("ignores rollback failure after navigating to another preview", async () => {
    const { itemA, itemB } = buildPreviewRecords();
    const client = new DeferredRollbackClient([itemA, itemB]);
    renderPreviewPage(client, `/studio/apps/${ITEM_A}/preview?revision=rev-002`);

    await expectPreviewPage(ITEM_A, "rev-002");

    await userEvent.click(screen.getByRole("button", { name: "Roll back to Revision 1" }));

    await userEvent.click(screen.getByRole("button", { name: "Go to item B" }));
    await expectPreviewPage(ITEM_B, "rev-002");

    await act(async () => {
      client.rollbackDeferred.reject(new Error("late rollback failure"));
      await client.rollbackDeferred.promise.catch(() => undefined);
    });
    await settle();

    expect(screen.queryByText("Generated app preview failed")).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Roll back to Revision 1" })).toBeEnabled();
    await expectPreviewPage(ITEM_B, "rev-002");
  });
});

class DeferredRollbackClient implements GeneratedAppLifecycleClient {
  readonly getPreviewCalls: Array<{ itemId: string; revisionId: string | null }> = [];
  readonly rollbackCalls: Array<{ itemId: string; targetRevisionId: string }> = [];
  readonly rollbackDeferred = deferred<GeneratedAppLifecycleRecord>();
  readonly #records = new Map<string, GeneratedAppLifecycleRecord>();

  constructor(records: readonly GeneratedAppLifecycleRecord[]) {
    for (const record of records) {
      this.setRecord(record);
    }
  }

  setRecord(record: GeneratedAppLifecycleRecord): void {
    this.#records.set(record.item.id, record);
  }

  async saveDraft(_input: SaveGeneratedAppDraftInput): Promise<GeneratedAppLifecycleRecord> {
    throw new Error("saveDraft is not used by this test.");
  }

  async get(itemId: string): Promise<GeneratedAppLifecycleRecord> {
    return this.#requireRecord(itemId);
  }

  async addRevision(_itemId: string, _input: GeneratedAppRevisionInput): Promise<GeneratedAppLifecycleRecord> {
    throw new Error("addRevision is not used by this test.");
  }

  async publish(_itemId: string): Promise<GeneratedAppPreviewDescriptor> {
    throw new Error("publish is not used by this test.");
  }

  async rollback(itemId: string, targetRevisionId: string): Promise<GeneratedAppLifecycleRecord> {
    this.rollbackCalls.push({ itemId, targetRevisionId });
    return this.rollbackDeferred.promise;
  }

  async getPreview(
    itemId: string,
    options: { revisionId?: string | null } = {},
  ): Promise<GeneratedAppPreviewDescriptor> {
    this.getPreviewCalls.push({ itemId, revisionId: options.revisionId ?? null });
    return descriptorFor(this.#requireRecord(itemId), options.revisionId);
  }

  #requireRecord(itemId: string): GeneratedAppLifecycleRecord {
    const record = this.#records.get(itemId);
    if (!record) throw new Error(`Missing generated app record: ${itemId}`);
    return record;
  }
}

function renderPreviewPage(client: DeferredRollbackClient, initialEntry: string): void {
  render(
    <MemoryRouter initialEntries={[initialEntry]}>
      <PreviewHarness client={client} />
    </MemoryRouter>,
  );
}

function PreviewHarness({ client }: { client: GeneratedAppLifecycleClient }): JSX.Element {
  const navigate = useNavigate();
  const location = useLocation();
  return (
    <>
      <button type="button" onClick={() => navigate(`/studio/apps/${ITEM_B}/preview?revision=rev-002`)}>
        Go to item B
      </button>
      <span data-testid="test-location">
        {location.pathname}
        {location.search}
      </span>
      <Routes>
        <Route
          path="/studio/apps/:itemId/preview"
          element={
            <GeneratedAppLifecycleClientProvider client={client}>
              <GeneratedAppPreviewPage />
            </GeneratedAppLifecycleClientProvider>
          }
        />
      </Routes>
    </>
  );
}

function buildPreviewRecords(): { itemA: GeneratedAppLifecycleRecord; itemB: GeneratedAppLifecycleRecord } {
  const [record] = buildDefaultGeneratedAppLifecycleRecords();
  return {
    itemA: withItem(record, ITEM_A, "Operations dashboard A"),
    itemB: withItem(record, ITEM_B, "Operations dashboard B"),
  };
}

function withItem(record: GeneratedAppLifecycleRecord, itemId: string, title: string): GeneratedAppLifecycleRecord {
  const cloned = structuredClone(record);
  return {
    ...cloned,
    item: { ...cloned.item, id: itemId, title },
    summary: { ...cloned.summary, id: itemId, title },
  };
}

function descriptorFor(
  record: GeneratedAppLifecycleRecord,
  revisionId: string | null | undefined,
): GeneratedAppPreviewDescriptor {
  const activeRevisionId = revisionId ?? record.activeRevision.id;
  const activeRevision = record.lifecycle.revisions.find((revision) => revision.id === activeRevisionId);
  if (!activeRevision) throw new Error(`Missing generated app revision: ${activeRevisionId}`);
  return { ...record, activeRevision, previewUrl: activeRevision.previewUrl };
}

async function expectPreviewPage(itemId: string, revisionId: string): Promise<void> {
  await waitFor(() => {
    expect(screen.getByTestId("generated-app-preview-page")).toHaveAttribute("data-item-id", itemId);
    expect(screen.getByTestId("generated-app-preview-page")).toHaveAttribute("data-active-revision", revisionId);
  });
}

async function settle(): Promise<void> {
  await act(async () => {
    await Promise.resolve();
    await Promise.resolve();
  });
}

function deferred<T>(): {
  promise: Promise<T>;
  resolve: (value: T) => void;
  reject: (reason?: unknown) => void;
} {
  let resolve!: (value: T) => void;
  let reject!: (reason?: unknown) => void;
  const promise = new Promise<T>((innerResolve, innerReject) => {
    resolve = innerResolve;
    reject = innerReject;
  });
  return { promise, resolve, reject };
}
