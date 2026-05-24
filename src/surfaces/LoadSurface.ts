/**
 * `LoadSurface<T>` — the discriminated union returned by every SDK-backed
 * loader hook in Console. Mirrors the `LoadSurface` already used in
 * `honua-portal/src/saved-maps`, with two additions:
 *
 * - `pending-binding` lets a feature render a typed unsupported state when an
 *   SDK contract is still in flight (notably `honua-sdk-js#225` for content
 *   items and dashboard/report packages).
 * - `unsupported` carries an SDK error `code` for telemetry, so typed errors
 *   from `HonuaMapPackageError` / sharing errors are preserved.
 *
 * Hoisting this union into `@honua/sdk-js/contract` is a tracked follow-on
 * (see Question 3 in the design brief). Until then, Console owns the type so
 * surfaces can adopt it without waiting on the SDK.
 */

export type LoadSurfaceStatus =
  | "ok"
  | "missing"
  | "unauthorized"
  | "unsupported"
  | "pending-binding";

export type LoadSurface<T> =
  | { readonly status: "ok"; readonly value: T }
  | { readonly status: "missing" }
  | { readonly status: "unauthorized" }
  | {
      readonly status: "unsupported";
      readonly reason: string;
      readonly code?: string;
    }
  | {
      readonly status: "pending-binding";
      readonly waitingFor: ReadonlyArray<string>;
    };

export const ok = <T>(value: T): LoadSurface<T> => ({ status: "ok", value });
export const missing = <T>(): LoadSurface<T> => ({ status: "missing" });
export const unauthorized = <T>(): LoadSurface<T> => ({ status: "unauthorized" });
export const unsupported = <T>(reason: string, code?: string): LoadSurface<T> =>
  code === undefined
    ? { status: "unsupported", reason }
    : { status: "unsupported", reason, code };
export const pendingBinding = <T>(
  waitingFor: ReadonlyArray<string>,
): LoadSurface<T> => ({ status: "pending-binding", waitingFor });

export function isOk<T>(surface: LoadSurface<T>): surface is { status: "ok"; value: T } {
  return surface.status === "ok";
}
