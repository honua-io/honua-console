/**
 * Status surfacing for published services.
 *
 * Two responsibilities:
 *
 * 1. Project the operator-side `AdminServiceStatus` onto the four user-safe
 *    `ServiceStatus` values the portal renders. Anything we do not have a
 *    mapping for collapses to `unavailable`, never `available` — we'd
 *    rather show a degraded badge than mislabel a broken service as healthy.
 *
 * 2. Sanitize an operator-side `statusReason` string into a user-safe
 *    `statusDetail` so operator vocabulary (job ids, stack traces, internal
 *    URLs) never leaks into the end-user catalog. Operators get the full
 *    diagnostic by following the admin-only link surfaced in `status.ts`.
 *
 * The badge metadata (label, tone, description) is the single source of
 * truth so catalog cards, item detail pages, and embed chrome render the
 * same string for the same status.
 */

import type { AdminServiceStatus, PublishStatusBadge, ServiceContentItem, ServiceStatus } from "./types.js";

const ADMIN_STATUS_MAP: Record<AdminServiceStatus, ServiceStatus> = {
  // honua-server canonical PublishedServiceStatus enum.
  // Provisioning: service is being materialized — usable status not yet
  // promised, render as `limited` so the catalog card warns the user.
  Provisioning: "limited",
  Active: "available",
  // Suspended: operator paused the service; portal should not let users
  // hit it but the item should remain discoverable.
  Suspended: "unavailable",
  // RefreshFailed: the latest refresh failed but prior data is still
  // being served — `limited` matches the "responding with reduced
  // capacity" badge and avoids misleading the user with `available`.
  RefreshFailed: "limited",
  Decommissioned: "unavailable",
  // Looser operator-side aliases.
  ok: "available",
  ready: "available",
  running: "available",
  publishing: "limited",
  deploying: "limited",
  warming: "limited",
  degraded: "limited",
  partial: "limited",
  throttled: "limited",
  failed: "unavailable",
  errored: "unavailable",
  broken: "unavailable",
  draft: "draft",
  unpublished: "draft",
};

const ADMIN_STATUS_MAP_LC: Map<string, ServiceStatus> = new Map(
  (Object.keys(ADMIN_STATUS_MAP) as AdminServiceStatus[]).map((k) => [k.toLowerCase(), ADMIN_STATUS_MAP[k]]),
);

/**
 * Map an operator-side status string onto a portal-side ServiceStatus.
 *
 * Lookup is case-insensitive: the canonical honua-server enum members
 * are PascalCase (`Active`, `Provisioning`, …) but admin adapters may
 * normalize to lowercase before sending. Both shapes map to the same
 * portal status.
 *
 * Defaults to `unavailable` for unknown strings. Returning `available` on
 * an unknown value would let an admin-side rename silently mark a broken
 * service as healthy; defaulting down forces a contract update before a
 * new "ok" state can ship to portal.
 */
export function mapAdminStatus(value: string): ServiceStatus {
  if (typeof value !== "string" || value.length === 0) return "unavailable";
  const hit = ADMIN_STATUS_MAP_LC.get(value.toLowerCase());
  return hit ?? "unavailable";
}

const BADGE: Record<ServiceStatus, PublishStatusBadge> = {
  available: {
    status: "available",
    tone: "ok",
    label: "Available",
    description: "Service is available.",
  },
  limited: {
    status: "limited",
    tone: "warn",
    label: "Limited availability",
    description: "Service is responding with reduced capacity.",
  },
  unavailable: {
    status: "unavailable",
    tone: "error",
    label: "Currently unavailable",
    description: "Service is not responding.",
  },
  draft: {
    status: "draft",
    tone: "info",
    label: "Draft",
    description: "Service is not yet published for end users.",
  },
};

export function statusBadge(status: ServiceStatus): PublishStatusBadge {
  return BADGE[status];
}

/**
 * Sanitize an operator-side reason string into a user-safe `statusDetail`.
 *
 * Rules:
 *   - `null` / empty / whitespace → `null`.
 *   - Anything that looks like an operator-only token (URLs, file paths,
 *     stack traces, GUIDs, job ids, http status codes with details) is
 *     dropped.
 *   - Free-text reasons are trimmed and capped at 160 chars; operators
 *     who need the full diagnostic follow the admin link.
 *
 * The cap matches a single catalog-card line; longer reasons are
 * intentionally truncated so the catalog layout stays predictable.
 */
export function sanitizeStatusReason(reason: string | null | undefined): string | null {
  if (reason == null) return null;
  if (typeof reason !== "string") return null;
  const trimmed = reason.trim();
  if (trimmed.length === 0) return null;
  if (looksOperatorOnly(trimmed)) return null;
  const MAX = 160;
  if (trimmed.length <= MAX) return trimmed;
  return `${trimmed.slice(0, MAX - 1).trimEnd()}…`;
}

const OPERATOR_ONLY_PATTERNS: readonly RegExp[] = [
  /https?:\/\//i,
  /file:\/\//i,
  /(?:^|\s)[A-Z]:\\/,
  /(?:^|\s)\/(?:var|usr|etc|opt|home|tmp)\//,
  /\bat\s+\S+\s+\(.+:\d+:\d+\)/,
  /Exception\b/,
  /\bStackTrace\b/i,
  /\b[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}\b/i,
  /\bjob[-_]?id\s*[:=]/i,
  /\bHTTP\s+\d{3}\b/,
];

function looksOperatorOnly(value: string): boolean {
  return OPERATOR_ONLY_PATTERNS.some((re) => re.test(value));
}

// ── Admin diagnostic link ───────────────────────────────────────────────────

export interface AdminDiagnosticLinkContext {
  /**
   * Base URL for honua-server-admin. Configured per-deployment; the portal
   * never hard-codes it because the same portal serves multiple admin
   * deployments behind different routes (per-tenant, per-region).
   */
  adminBaseUrl: string;
  /**
   * Permission set for the current actor. Only actors that include
   * `admin:diagnostics` see the link; all other callers get `null` so
   * the operator URL never reaches an end-user UI binding.
   */
  permissions: ReadonlySet<string> | readonly string[];
}

/**
 * Compose the admin-only diagnostic link for a service item.
 *
 * Returns `null` unless the actor has `admin:diagnostics` AND the item
 * carries an `adminDiagnosticsRef`. Both gates matter: portal must not
 * render the URL to non-operators, and admin must not have to render a
 * link target it does not have a correlation id for.
 *
 * The composed URL points at the admin diagnostic surface for the source
 * service. Portal never reasons about the path beyond joining base + ref.
 */
export function adminDiagnosticLink(item: ServiceContentItem, ctx: AdminDiagnosticLinkContext): string | null {
  const permissions = toPermissionSet(ctx.permissions);
  if (!permissions.has("admin:diagnostics")) return null;
  const ref = item.target.adminDiagnosticsRef;
  if (!ref) return null;
  const base = ctx.adminBaseUrl.replace(/\/+$/, "");
  const safeRef = encodeURIComponent(ref);
  return `${base}/services/${safeRef}/diagnostics`;
}

function toPermissionSet(perms: ReadonlySet<string> | readonly string[]): ReadonlySet<string> {
  if (perms instanceof Set) return perms;
  return new Set(perms);
}
