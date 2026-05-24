import type { HonuaControlPlaneResult } from "../sdk/control-plane";
import { type LoadSurface, missing, ok, unauthorized, unsupported } from "./LoadSurface";

/**
 * Convert a `HonuaControlPlaneResult<T>` (the SDK's
 * `{ supported, ... } | { supported: false, ... }` shape) into a `LoadSurface<T>`.
 *
 * 404 maps to `missing`, 401/403 maps to `unauthorized`, 501 maps to
 * `unsupported`. The SDK already maps 404/501 to `supported: false`. 401/403
 * surface as thrown `HonuaHttpError`s today, so the loader should catch them
 * via {@link adaptSdkThrown} below.
 */
export function adaptControlPlaneResult<T>(result: HonuaControlPlaneResult<T>): LoadSurface<T> {
  if (result.supported) return ok(result.value);
  if (result.statusCode === 404) return missing<T>();
  return unsupported<T>(result.reason, String(result.statusCode));
}

/** Adapt an exception thrown by an SDK call. */
export function adaptSdkThrown<T>(error: unknown): LoadSurface<T> {
  const status =
    error && typeof error === "object" && "statusCode" in (error as Record<string, unknown>)
      ? Number((error as { statusCode?: unknown }).statusCode)
      : undefined;
  const code =
    error && typeof error === "object" && "code" in (error as Record<string, unknown>)
      ? String((error as { code?: unknown }).code ?? "")
      : undefined;
  const message =
    error instanceof Error ? error.message : typeof error === "string" ? error : "unknown SDK error";
  if (status === 401 || status === 403) return unauthorized<T>();
  if (status === 404) return missing<T>();
  return unsupported<T>(message, code);
}
