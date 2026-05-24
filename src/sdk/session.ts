/**
 * Session / capability / entitlement client facade.
 *
 * Until `honua-server#1162` ships the unified, non-admin capability endpoint,
 * this client synthesizes the session bundle by fanning out three existing
 * admin endpoints in parallel:
 *
 * - `GET /api/v1/admin/auth/session`           → identity (provider key, claims)
 * - `GET /api/v1/admin/users/{id}/effective-permissions` → capability list
 * - `GET /api/v1/admin/license/entitlements`   → entitlement flags
 *
 * Non-admin users hitting admin-scoped routes get 401; this facade maps that
 * to a `public-session` bundle so Catalog/Share still render with default
 * (empty) capabilities and entitlements. When `#1162` lands, only this file
 * changes; `useCapability` / `useEntitlement` callers stay put.
 *
 * No DTO definitions for `ContentItem`, `SavedMapItem`, `Provenance*`, etc.
 * live here — those are SDK-owned and pending `honua-sdk-js#225`.
 */

export type CapabilityName = string & { readonly __brand?: "CapabilityName" };
export type EntitlementName = string & { readonly __brand?: "EntitlementName" };

export interface CapabilityBundle {
  readonly capabilities: ReadonlySet<CapabilityName>;
  readonly entitlements: ReadonlySet<EntitlementName>;
}

export interface SessionIdentity {
  readonly providerKey: string;
  readonly displayName: string;
  readonly email?: string;
  readonly userId?: string;
  readonly expiresAt?: string;
}

export interface SessionWorkspace {
  readonly id: string;
  readonly name: string;
}

export type SessionStatus =
  | { kind: "loading" }
  | { kind: "anonymous" }
  | {
      kind: "authenticated";
      identity: SessionIdentity;
      workspace?: SessionWorkspace;
      bundle: CapabilityBundle;
    }
  | { kind: "error"; message: string };

export interface SessionBootstrapOptions {
  readonly baseUrl?: string;
  readonly fetchImpl?: typeof fetch;
  readonly accessToken?: string;
}

interface AdminAuthSessionResponse {
  readonly isAuthenticated: boolean;
  readonly providerKey?: string | null;
  readonly expiresAt?: string | null;
  readonly claims?: ReadonlyArray<{ type?: string; value?: string }>;
}

interface EffectivePermissionsResponse {
  readonly userId?: string | null;
  readonly roles?: ReadonlyArray<string>;
  readonly permissions?: ReadonlyArray<{ name?: string; key?: string; permission?: string }>;
  readonly resolvedAt?: string | null;
}

interface LicenseEntitlementsResponse {
  readonly edition?: string;
  readonly features?: ReadonlyArray<{ name?: string; key?: string; enabled?: boolean }>;
}

interface ApiEnvelope<T> {
  readonly success?: boolean;
  readonly data?: T;
}

const SESSION_ENDPOINT = "/api/v1/admin/auth/session";
const PERMISSIONS_ENDPOINT = (userId: string): string =>
  `/api/v1/admin/users/${encodeURIComponent(userId)}/effective-permissions`;
const ENTITLEMENTS_ENDPOINT = "/api/v1/admin/license/entitlements";

function joinUrl(base: string | undefined, path: string): string {
  if (!base) return path;
  const normalizedBase = base.replace(/\/+$/, "");
  return `${normalizedBase}${path}`;
}

function unwrap<T>(payload: ApiEnvelope<T> | T): T | undefined {
  if (payload == null) return undefined;
  if (typeof payload === "object" && payload !== null && "success" in (payload as Record<string, unknown>)) {
    const envelope = payload as ApiEnvelope<T>;
    return envelope.data;
  }
  return payload as T;
}

async function readJson<T>(response: Response): Promise<T> {
  const text = await response.text();
  if (!text) return undefined as unknown as T;
  return JSON.parse(text) as T;
}

function pickFirstClaim(
  claims: ReadonlyArray<{ type?: string; value?: string }> | undefined,
  keys: ReadonlyArray<string>,
): string | undefined {
  if (!claims) return undefined;
  for (const claim of claims) {
    if (claim.type && claim.value && keys.includes(claim.type)) return claim.value;
  }
  return undefined;
}

function permissionsToCapabilities(
  permissions: EffectivePermissionsResponse["permissions"],
): Set<CapabilityName> {
  const out = new Set<CapabilityName>();
  if (!permissions) return out;
  for (const grant of permissions) {
    const value = grant.name ?? grant.key ?? grant.permission;
    if (value) out.add(value as CapabilityName);
  }
  return out;
}

function featuresToEntitlements(
  features: LicenseEntitlementsResponse["features"],
): Set<EntitlementName> {
  const out = new Set<EntitlementName>();
  if (!features) return out;
  for (const feature of features) {
    const value = feature.name ?? feature.key;
    if (value && feature.enabled !== false) out.add(value as EntitlementName);
  }
  return out;
}

const EMPTY_BUNDLE: CapabilityBundle = Object.freeze({
  capabilities: new Set<CapabilityName>(),
  entitlements: new Set<EntitlementName>(),
});

export interface SessionBootstrapResult {
  readonly status: SessionStatus;
  /** When the bootstrap encountered 401s, this lists which endpoints were inaccessible. */
  readonly fellBackEndpoints: ReadonlyArray<string>;
}

export class SessionClient {
  private readonly fetchImpl: typeof fetch;
  private readonly baseUrl?: string;
  private readonly accessToken?: string;

  constructor(options: SessionBootstrapOptions = {}) {
    this.fetchImpl = options.fetchImpl ?? globalThis.fetch.bind(globalThis);
    this.baseUrl = options.baseUrl;
    this.accessToken = options.accessToken;
  }

  /**
   * Fan-out bootstrap. Parallel calls keep startup off the cold-start critical
   * path. Returns a resolved `SessionStatus` so consumers can render once.
   */
  async bootstrap(): Promise<SessionBootstrapResult> {
    const headers: HeadersInit = { accept: "application/json" };
    if (this.accessToken) {
      (headers as Record<string, string>).authorization = `Bearer ${this.accessToken}`;
    }

    const sessionPromise = this.fetchImpl(joinUrl(this.baseUrl, SESSION_ENDPOINT), {
      headers,
      credentials: "include",
    });
    const entitlementsPromise = this.fetchImpl(joinUrl(this.baseUrl, ENTITLEMENTS_ENDPOINT), {
      headers,
      credentials: "include",
    });

    let sessionResponse: Response;
    try {
      sessionResponse = await sessionPromise;
    } catch (error) {
      return {
        status: { kind: "error", message: error instanceof Error ? error.message : "session bootstrap failed" },
        fellBackEndpoints: [SESSION_ENDPOINT],
      };
    }

    const fellBack: string[] = [];

    if (sessionResponse.status === 401) {
      await entitlementsPromise.catch(() => undefined);
      fellBack.push(SESSION_ENDPOINT);
      return { status: { kind: "anonymous" }, fellBackEndpoints: fellBack };
    }
    if (!sessionResponse.ok) {
      return {
        status: { kind: "error", message: `session bootstrap returned ${sessionResponse.status}` },
        fellBackEndpoints: fellBack,
      };
    }

    const sessionPayload = unwrap<AdminAuthSessionResponse>(
      await readJson<ApiEnvelope<AdminAuthSessionResponse> | AdminAuthSessionResponse>(sessionResponse),
    );
    if (!sessionPayload || !sessionPayload.isAuthenticated) {
      await entitlementsPromise.catch(() => undefined);
      return { status: { kind: "anonymous" }, fellBackEndpoints: fellBack };
    }

    const userId =
      sessionPayload.providerKey ??
      pickFirstClaim(sessionPayload.claims, ["sub", "uid", "user_id"]) ??
      undefined;

    const permissionsPromise = userId
      ? this.fetchImpl(joinUrl(this.baseUrl, PERMISSIONS_ENDPOINT(userId)), {
          headers,
          credentials: "include",
        })
      : Promise.resolve(undefined);

    const [entitlementsResponse, permissionsResponse] = await Promise.all([
      entitlementsPromise.catch(() => undefined),
      permissionsPromise.catch(() => undefined),
    ]);

    const capabilities = new Set<CapabilityName>();
    if (permissionsResponse) {
      if (permissionsResponse.status === 401) {
        fellBack.push(userId ? PERMISSIONS_ENDPOINT(userId) : PERMISSIONS_ENDPOINT("(unknown)"));
      } else if (permissionsResponse.ok) {
        const payload = unwrap<EffectivePermissionsResponse>(
          await readJson<ApiEnvelope<EffectivePermissionsResponse> | EffectivePermissionsResponse>(
            permissionsResponse,
          ),
        );
        for (const cap of permissionsToCapabilities(payload?.permissions)) capabilities.add(cap);
        if (payload?.roles) for (const role of payload.roles) capabilities.add(`role:${role}` as CapabilityName);
      }
    }

    const entitlements = new Set<EntitlementName>();
    if (entitlementsResponse) {
      if (entitlementsResponse.status === 401) {
        fellBack.push(ENTITLEMENTS_ENDPOINT);
      } else if (entitlementsResponse.ok) {
        const payload = unwrap<LicenseEntitlementsResponse>(
          await readJson<ApiEnvelope<LicenseEntitlementsResponse> | LicenseEntitlementsResponse>(
            entitlementsResponse,
          ),
        );
        for (const ent of featuresToEntitlements(payload?.features)) entitlements.add(ent);
        if (payload?.edition) entitlements.add(`edition:${payload.edition}` as EntitlementName);
      }
    }

    const identity: SessionIdentity = {
      providerKey: sessionPayload.providerKey ?? userId ?? "anonymous",
      displayName:
        pickFirstClaim(sessionPayload.claims, ["name", "display_name", "preferred_username"]) ??
        sessionPayload.providerKey ??
        "Honua user",
      email: pickFirstClaim(sessionPayload.claims, ["email"]),
      ...(userId ? { userId } : {}),
      ...(sessionPayload.expiresAt ? { expiresAt: sessionPayload.expiresAt } : {}),
    };

    return {
      status: {
        kind: "authenticated",
        identity,
        bundle: { capabilities, entitlements },
      },
      fellBackEndpoints: fellBack,
    };
  }
}

export function createEmptyBundle(): CapabilityBundle {
  return EMPTY_BUNDLE;
}
