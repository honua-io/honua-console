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
  readonly permissions?: ReadonlyArray<PermissionGrantResponse>;
  readonly resolvedAt?: string | null;
}

interface PermissionGrantResponse {
  readonly name?: string;
  readonly key?: string;
  readonly permission?: string;
  readonly service?: string;
  readonly layer?: string;
  readonly operation?: string;
}

interface LicenseFeatureEntitlementsResponse {
  readonly edition?: string;
  readonly features?: ReadonlyArray<{
    readonly name?: string;
    readonly key?: string;
    readonly enabled?: boolean;
    readonly isEnabled?: boolean;
    readonly isActive?: boolean;
  }>;
}

interface LicenseEntitlementResponse {
  readonly name?: string;
  readonly key?: string;
  readonly enabled?: boolean;
  readonly isEnabled?: boolean;
  readonly isActive?: boolean;
}

type LicenseEntitlementsResponse =
  | LicenseFeatureEntitlementsResponse
  | ReadonlyArray<LicenseEntitlementResponse>;

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

const USER_ID_CLAIMS = [
  "sub",
  "uid",
  "user_id",
  "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier",
  "http://schemas.microsoft.com/ws/2008/06/identity/claims/nameidentifier",
];

const CONSOLE_WILDCARD_READ_CAPABILITIES: ReadonlyArray<CapabilityName> = [
  "catalog:read" as CapabilityName,
  "map-packages:read" as CapabilityName,
  "sharing:read" as CapabilityName,
];

const CONSOLE_WILDCARD_ADMIN_CAPABILITIES: ReadonlyArray<CapabilityName> = [
  ...CONSOLE_WILDCARD_READ_CAPABILITIES,
  "studio:preview" as CapabilityName,
  "operate:provenance:read" as CapabilityName,
];

function normalizedGrantPart(value: string | undefined): string | undefined {
  const trimmed = value?.trim();
  return trimmed ? trimmed : undefined;
}

function addPermissionGrantCapability(out: Set<CapabilityName>, grant: PermissionGrantResponse): void {
  const service = normalizedGrantPart(grant.service);
  const layer = normalizedGrantPart(grant.layer);
  const operation = normalizedGrantPart(grant.operation);
  if (!service || !layer || !operation) return;

  out.add(`permission:${service}:${layer}:${operation}` as CapabilityName);
  if (service === "*" && layer === "*" && (operation === "read" || operation === "*")) {
    for (const cap of CONSOLE_WILDCARD_READ_CAPABILITIES) out.add(cap);
  }
  if (service === "*" && layer === "*" && operation === "*") {
    for (const cap of CONSOLE_WILDCARD_ADMIN_CAPABILITIES) out.add(cap);
  }
}

function permissionsToCapabilities(
  permissions: EffectivePermissionsResponse["permissions"],
): Set<CapabilityName> {
  const out = new Set<CapabilityName>();
  if (!permissions) return out;
  for (const grant of permissions) {
    const value = grant.name ?? grant.key ?? grant.permission;
    if (value) out.add(value as CapabilityName);
    addPermissionGrantCapability(out, grant);
  }
  return out;
}

function isFeatureEntitlementsResponse(
  payload: LicenseEntitlementsResponse,
): payload is LicenseFeatureEntitlementsResponse {
  return !Array.isArray(payload);
}

function featuresToEntitlements(
  payload: LicenseEntitlementsResponse | undefined,
): Set<EntitlementName> {
  const out = new Set<EntitlementName>();
  if (!payload) return out;
  if (Array.isArray(payload)) {
    for (const entitlement of payload) {
      const value = entitlement.key ?? entitlement.name;
      if (value && entitlement.isActive !== false && entitlement.isEnabled !== false && entitlement.enabled !== false) {
        out.add(value as EntitlementName);
      }
    }
    return out;
  }
  if (!isFeatureEntitlementsResponse(payload)) return out;
  for (const feature of payload.features ?? []) {
    const value = feature.key ?? feature.name;
    if (value && feature.isEnabled !== false && feature.isActive !== false && feature.enabled !== false) {
      out.add(value as EntitlementName);
    }
  }
  if (payload.edition) out.add(`edition:${payload.edition}` as EntitlementName);
  return out;
}

const EMPTY_BUNDLE: CapabilityBundle = Object.freeze({
  capabilities: new Set<CapabilityName>(),
  entitlements: new Set<EntitlementName>(),
});

export interface SessionBootstrapResult {
  readonly status: SessionStatus;
  /** When bootstrap encountered inaccessible endpoints, this lists which endpoints fell back. */
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

    type SecondaryFetchOutcome =
      | { readonly kind: "response"; readonly response: Response }
      | { readonly kind: "network-error" };

    const fetchEndpoint = (endpoint: string): Promise<SecondaryFetchOutcome> =>
      this.fetchImpl(joinUrl(this.baseUrl, endpoint), {
        headers,
        credentials: "include",
      }).then(
        (response): SecondaryFetchOutcome => ({ kind: "response", response }),
        (): SecondaryFetchOutcome => ({ kind: "network-error" }),
      );

    const sessionPromise = this.fetchImpl(joinUrl(this.baseUrl, SESSION_ENDPOINT), {
      headers,
      credentials: "include",
    });
    const entitlementsPromise = fetchEndpoint(ENTITLEMENTS_ENDPOINT);

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

    const recordSecondary = (
      outcome: SecondaryFetchOutcome,
      endpoint: string,
    ): Response | undefined => {
      if (outcome.kind === "network-error") {
        fellBack.push(endpoint);
        return undefined;
      }
      const { response } = outcome;
      if (response.ok) return response;
      fellBack.push(endpoint);
      // Any non-ok secondary response is treated as a fallback: parsing the
      // body would risk a `JSON.parse` throw on non-JSON 5xx pages (HTML
      // error pages, plain text from a reverse proxy), which would reject
      // the whole bootstrap and erase the authenticated session along with
      // every other `fellBackEndpoints` diagnostic we already recorded.
      return undefined;
    };

    if (sessionResponse.status === 401) {
      await entitlementsPromise.then((outcome) => recordSecondary(outcome, ENTITLEMENTS_ENDPOINT));
      fellBack.push(SESSION_ENDPOINT);
      return { status: { kind: "anonymous" }, fellBackEndpoints: fellBack };
    }
    if (!sessionResponse.ok) {
      await entitlementsPromise.then((outcome) => recordSecondary(outcome, ENTITLEMENTS_ENDPOINT));
      return {
        status: { kind: "error", message: `session bootstrap returned ${sessionResponse.status}` },
        fellBackEndpoints: fellBack,
      };
    }

    const sessionPayload = unwrap<AdminAuthSessionResponse>(
      await readJson<ApiEnvelope<AdminAuthSessionResponse> | AdminAuthSessionResponse>(sessionResponse),
    );
    if (!sessionPayload || !sessionPayload.isAuthenticated) {
      await entitlementsPromise.then((outcome) => recordSecondary(outcome, ENTITLEMENTS_ENDPOINT));
      return { status: { kind: "anonymous" }, fellBackEndpoints: fellBack };
    }

    const userId = pickFirstClaim(sessionPayload.claims, USER_ID_CLAIMS) ?? undefined;
    const permissionsEndpoint = userId
      ? PERMISSIONS_ENDPOINT(userId)
      : PERMISSIONS_ENDPOINT("(unknown)");

    const permissionsOutcome: SecondaryFetchOutcome | undefined = userId
      ? await fetchEndpoint(permissionsEndpoint)
      : undefined;
    const entitlementsOutcome = await entitlementsPromise;

    const capabilities = new Set<CapabilityName>();
    const usablePermissions = permissionsOutcome
      ? recordSecondary(permissionsOutcome, permissionsEndpoint)
      : undefined;
    if (usablePermissions) {
      const payload = unwrap<EffectivePermissionsResponse>(
        await readJson<ApiEnvelope<EffectivePermissionsResponse> | EffectivePermissionsResponse>(
          usablePermissions,
        ),
      );
      for (const cap of permissionsToCapabilities(payload?.permissions)) capabilities.add(cap);
      if (payload?.roles) for (const role of payload.roles) capabilities.add(`role:${role}` as CapabilityName);
    }

    const entitlements = new Set<EntitlementName>();
    const usableEntitlements = recordSecondary(entitlementsOutcome, ENTITLEMENTS_ENDPOINT);
    if (usableEntitlements) {
      const payload = unwrap<LicenseEntitlementsResponse>(
        await readJson<ApiEnvelope<LicenseEntitlementsResponse> | LicenseEntitlementsResponse>(
          usableEntitlements,
        ),
      );
      for (const ent of featuresToEntitlements(payload)) entitlements.add(ent);
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
