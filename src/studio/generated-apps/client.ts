import type { Session } from "../../auth/types.js";
import type { ContentItem } from "../../transitional/content-item.js";
import {
  addGeneratedAppRevision,
  materializeGeneratedAppDraft,
  publishGeneratedAppItem,
  rollbackGeneratedAppItem,
  toGeneratedAppLifecycleRecord,
} from "./lifecycle.js";
import type {
  GeneratedAppLifecycleRecord,
  GeneratedAppPreviewDescriptor,
  GeneratedAppRevisionInput,
  SaveGeneratedAppDraftInput,
} from "./types.js";

export type GeneratedAppLifecycleErrorCode =
  | "missing"
  | "unauthorized"
  | "unsupported"
  | "invalid"
  | "conflict"
  | "server";

export class GeneratedAppLifecycleError extends Error {
  constructor(
    public readonly code: GeneratedAppLifecycleErrorCode,
    message: string,
    public readonly details?: Readonly<Record<string, unknown>>,
  ) {
    super(message);
    this.name = "GeneratedAppLifecycleError";
  }
}

export interface GeneratedAppLifecycleClient {
  saveDraft(input: SaveGeneratedAppDraftInput): Promise<GeneratedAppLifecycleRecord>;
  get(itemId: string): Promise<GeneratedAppLifecycleRecord>;
  addRevision(itemId: string, input: GeneratedAppRevisionInput): Promise<GeneratedAppLifecycleRecord>;
  publish(itemId: string): Promise<GeneratedAppPreviewDescriptor>;
  rollback(itemId: string, targetRevisionId: string): Promise<GeneratedAppLifecycleRecord>;
  getPreview(itemId: string, options?: { revisionId?: string | null }): Promise<GeneratedAppPreviewDescriptor>;
}

export interface FixtureGeneratedAppLifecycleClientOptions {
  readonly consoleBaseUrl: string;
  readonly actorId: string | null;
  readonly records?: readonly GeneratedAppLifecycleRecord[];
  readonly now?: () => string;
}

export class FixtureGeneratedAppLifecycleClient implements GeneratedAppLifecycleClient {
  readonly #consoleBaseUrl: string;
  readonly #actorId: string | null;
  readonly #now: () => string;
  readonly #items = new Map<string, ContentItem>();

  constructor(options: FixtureGeneratedAppLifecycleClientOptions) {
    this.#consoleBaseUrl = options.consoleBaseUrl;
    this.#actorId = options.actorId;
    this.#now = options.now ?? (() => new Date().toISOString());
    for (const record of options.records ?? []) {
      this.#items.set(record.item.id, clone(record.item));
    }
  }

  async saveDraft(input: SaveGeneratedAppDraftInput): Promise<GeneratedAppLifecycleRecord> {
    const record = materializeGeneratedAppDraft(input, {
      consoleBaseUrl: this.#consoleBaseUrl,
      now: this.#now(),
      itemId: input.id,
    });
    if (this.#items.has(record.item.id)) {
      throw new GeneratedAppLifecycleError("conflict", `Generated app item already exists: ${record.item.id}`);
    }
    this.#items.set(record.item.id, clone(record.item));
    return cloneRecord(record);
  }

  async get(itemId: string): Promise<GeneratedAppLifecycleRecord> {
    const item = this.#requireItem(itemId);
    this.#requireRead(item);
    return cloneRecord(toGeneratedAppLifecycleRecord(item));
  }

  async addRevision(itemId: string, input: GeneratedAppRevisionInput): Promise<GeneratedAppLifecycleRecord> {
    const item = this.#requireItem(itemId);
    this.#requireEdit(item);
    const record = addGeneratedAppRevision(item, input, {
      consoleBaseUrl: this.#consoleBaseUrl,
      actor: input.actor,
      now: this.#now(),
    });
    this.#items.set(itemId, clone(record.item));
    return cloneRecord(record);
  }

  async publish(itemId: string): Promise<GeneratedAppPreviewDescriptor> {
    const item = this.#requireItem(itemId);
    this.#requireEdit(item);
    const current = toGeneratedAppLifecycleRecord(item);
    if (current.lifecycle.state === "unsupported") {
      throw new GeneratedAppLifecycleError(
        "unsupported",
        current.lifecycle.unsupportedReason ?? "Generated app is not publishable.",
      );
    }
    const record = publishGeneratedAppItem(item, {
      consoleBaseUrl: this.#consoleBaseUrl,
      actor: this.#actorId ?? item.owner.id,
      now: this.#now(),
    });
    this.#items.set(itemId, clone(record.item));
    return { ...cloneRecord(record), previewUrl: record.activeRevision.previewUrl };
  }

  async rollback(itemId: string, targetRevisionId: string): Promise<GeneratedAppLifecycleRecord> {
    const item = this.#requireItem(itemId);
    this.#requireEdit(item);
    const record = rollbackGeneratedAppItem(item, targetRevisionId, {
      consoleBaseUrl: this.#consoleBaseUrl,
      actor: this.#actorId ?? item.owner.id,
      now: this.#now(),
    });
    this.#items.set(itemId, clone(record.item));
    return cloneRecord(record);
  }

  async getPreview(
    itemId: string,
    options: { revisionId?: string | null } = {},
  ): Promise<GeneratedAppPreviewDescriptor> {
    const record = await this.get(itemId);
    if (record.lifecycle.state !== "published") {
      throw new GeneratedAppLifecycleError(
        "unsupported",
        record.lifecycle.unsupportedReason ?? "Generated app is saved as a draft and has not been published.",
      );
    }
    const revision = options.revisionId
      ? record.lifecycle.revisions.find((candidate) => candidate.id === options.revisionId)
      : record.activeRevision;
    if (!revision) {
      throw new GeneratedAppLifecycleError("missing", `Generated app revision not found: ${options.revisionId}`);
    }
    return { ...record, activeRevision: revision, previewUrl: revision.previewUrl };
  }

  #requireItem(itemId: string): ContentItem {
    const item = this.#items.get(itemId);
    if (!item) throw new GeneratedAppLifecycleError("missing", `Generated app item not found: ${itemId}`);
    return clone(item);
  }

  #requireRead(item: ContentItem): void {
    if (canRead(item, this.#actorId)) return;
    throw new GeneratedAppLifecycleError("unauthorized", `You do not have access to generated app ${item.id}.`);
  }

  #requireEdit(item: ContentItem): void {
    if (this.#actorId && item.owner.id === this.#actorId) return;
    throw new GeneratedAppLifecycleError("unauthorized", `You cannot edit generated app ${item.id}.`);
  }
}

/**
 * HTTP transport for the Console generated-app lifecycle API. The real
 * endpoints are owned by honua-server / honua-sdk-js#225; this client is a
 * thin wrapper that produces typed `GeneratedAppLifecycleError`s and threads
 * the session bearer token when present. Console runs the fixture client by
 * default until those server endpoints land.
 */
export interface HttpGeneratedAppLifecycleClientOptions {
  readonly baseUrl: string;
  readonly session?: Session;
}

export class HttpGeneratedAppLifecycleClient implements GeneratedAppLifecycleClient {
  readonly #baseUrl: string;
  readonly #session: Session | undefined;

  constructor(options: HttpGeneratedAppLifecycleClientOptions) {
    this.#baseUrl = options.baseUrl.replace(/\/+$/, "");
    this.#session = options.session;
  }

  async saveDraft(input: SaveGeneratedAppDraftInput): Promise<GeneratedAppLifecycleRecord> {
    return this.#request<GeneratedAppLifecycleRecord>("/generated-apps", {
      method: "POST",
      body: input,
    });
  }

  async get(itemId: string): Promise<GeneratedAppLifecycleRecord> {
    return this.#request<GeneratedAppLifecycleRecord>(`/generated-apps/${encodeURIComponent(itemId)}`);
  }

  async addRevision(itemId: string, input: GeneratedAppRevisionInput): Promise<GeneratedAppLifecycleRecord> {
    return this.#request<GeneratedAppLifecycleRecord>(`/generated-apps/${encodeURIComponent(itemId)}/revisions`, {
      method: "POST",
      body: input,
    });
  }

  async publish(itemId: string): Promise<GeneratedAppPreviewDescriptor> {
    return this.#request<GeneratedAppPreviewDescriptor>(`/generated-apps/${encodeURIComponent(itemId)}/publish`, {
      method: "POST",
    });
  }

  async rollback(itemId: string, targetRevisionId: string): Promise<GeneratedAppLifecycleRecord> {
    return this.#request<GeneratedAppLifecycleRecord>(`/generated-apps/${encodeURIComponent(itemId)}/rollback`, {
      method: "POST",
      body: { targetRevisionId },
    });
  }

  async getPreview(
    itemId: string,
    options: { revisionId?: string | null } = {},
  ): Promise<GeneratedAppPreviewDescriptor> {
    const query = new URLSearchParams();
    if (options.revisionId) query.set("revision", options.revisionId);
    const suffix = query.size > 0 ? `?${query.toString()}` : "";
    return this.#request<GeneratedAppPreviewDescriptor>(
      `/generated-apps/${encodeURIComponent(itemId)}/preview${suffix}`,
    );
  }

  async #request<T>(path: string, options: { method?: string; body?: unknown } = {}): Promise<T> {
    const url = `${this.#baseUrl}${path}`;
    const headers: Record<string, string> = { Accept: "application/json" };
    if (options.body !== undefined) headers["Content-Type"] = "application/json";
    if (this.#session?.status === "authenticated" && this.#session.accessToken) {
      headers.Authorization = `Bearer ${this.#session.accessToken}`;
    }
    let response: Response;
    try {
      response = await fetch(url, {
        method: options.method ?? "GET",
        credentials: "include",
        headers,
        body: options.body === undefined ? undefined : JSON.stringify(options.body),
      });
    } catch (error) {
      throw new GeneratedAppLifecycleError("server", error instanceof Error ? error.message : String(error));
    }
    if (!response.ok) {
      const envelope = await readErrorEnvelope(response);
      throw new GeneratedAppLifecycleError(
        lifecycleCodeFromStatus(response.status),
        envelope?.error.message ?? `Request failed with status ${response.status}`,
        envelope?.error.details,
      );
    }
    return (await response.json()) as T;
  }
}

interface HttpErrorEnvelope {
  readonly error: {
    readonly code: string;
    readonly message: string;
    readonly details?: Readonly<Record<string, unknown>>;
  };
}

async function readErrorEnvelope(response: Response): Promise<HttpErrorEnvelope | null> {
  try {
    const parsed = (await response.json()) as HttpErrorEnvelope;
    if (parsed && typeof parsed === "object" && parsed.error && typeof parsed.error.message === "string") {
      return parsed;
    }
  } catch {
    // Body was not JSON; fall through.
  }
  return null;
}

function canRead(item: ContentItem, actorId: string | null): boolean {
  if (actorId && item.owner.id === actorId) return true;
  if (!actorId) return item.access.sharing === "public-link" || item.access.sharing === "public";
  return item.access.sharing !== "private";
}

/**
 * Map an HTTP response status to a `GeneratedAppLifecycleErrorCode`. Both
 * 401 and 403 collapse to `unauthorized` so an expired session and a
 * permission denial render the same Forbidden surface — parity with the
 * session probe in `whoamiDriver`, which also treats 401 and 403 as auth
 * failures. Exported so the mapping invariant can be unit-tested.
 */
export function lifecycleCodeFromStatus(status: number): GeneratedAppLifecycleErrorCode {
  if (status === 404) return "missing";
  if (status === 401 || status === 403) return "unauthorized";
  if (status === 409) return "conflict";
  if (status === 422) return "unsupported";
  if (status >= 500) return "server";
  return "invalid";
}

function cloneRecord(record: GeneratedAppLifecycleRecord): GeneratedAppLifecycleRecord {
  return clone(record);
}

function clone<T>(value: T): T {
  return structuredClone(value);
}
