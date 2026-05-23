import type { AuthenticatedSession, Session } from "../auth/types";
import { consoleEnv } from "../env";

export class ConsoleApiError extends Error {
  constructor(
    public readonly status: number,
    public readonly url: string,
    message: string,
    public readonly envelope?: ConsoleApiErrorEnvelope,
  ) {
    super(message);
    this.name = "ConsoleApiError";
  }
}

export interface ConsoleApiErrorEnvelope {
  readonly error?: {
    readonly code?: string;
    readonly message?: string;
    readonly details?: Readonly<Record<string, unknown>>;
  };
}

export interface RequestOptions extends Omit<RequestInit, "body" | "headers"> {
  body?: unknown;
  headers?: Record<string, string>;
}

function authHeaders(session: Session | undefined): Record<string, string> {
  if (!session || session.status !== "authenticated") return {};
  const auth = session as AuthenticatedSession;
  return auth.accessToken ? { Authorization: `Bearer ${auth.accessToken}` } : {};
}

function resolveUrl(path: string): string {
  if (/^https?:\/\//i.test(path)) return path;
  if (!consoleEnv.apiBaseUrl) return path;
  const base = consoleEnv.apiBaseUrl.replace(/\/+$/, "");
  const suffix = path.startsWith("/") ? path : `/${path}`;
  return `${base}${suffix}`;
}

/**
 * Thin bearer-injecting fetch wrapper.
 *
 * The Console does NOT re-implement service, webmap, or package contracts here
 * — those stay in `@honua/sdk-js` and are imported from its stable subpaths.
 * This wrapper exists only so every call site shares one place to attach auth,
 * set Accept, and surface typed errors.
 */
export async function consoleFetch<T>(
  path: string,
  session: Session | undefined,
  options: RequestOptions = {},
): Promise<T> {
  const url = resolveUrl(path);
  const { body, headers, ...rest } = options;
  const init: RequestInit = {
    ...rest,
    headers: {
      Accept: "application/json",
      ...(body !== undefined ? { "Content-Type": "application/json" } : {}),
      ...authHeaders(session),
      ...headers,
    },
  };
  if (body !== undefined) {
    init.body = typeof body === "string" ? body : JSON.stringify(body);
  }
  const response = await fetch(url, init);
  if (!response.ok) {
    const envelope = await readConsoleApiErrorEnvelope(response);
    const message = envelope?.error?.message ?? `Request to ${url} failed: ${response.status}`;
    throw new ConsoleApiError(response.status, url, message, envelope);
  }
  if (response.status === 204) return undefined as T;
  return (await response.json()) as T;
}

async function readConsoleApiErrorEnvelope(response: Response): Promise<ConsoleApiErrorEnvelope | undefined> {
  try {
    return (await response.json()) as ConsoleApiErrorEnvelope;
  } catch {
    return undefined;
  }
}
