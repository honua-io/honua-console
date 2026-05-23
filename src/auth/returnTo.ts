const FALLBACK_RETURN_TO = "/";
const RETURN_TO_ORIGIN = "https://console.local";
const BLOCKED_RETURN_PATHS = new Set(["/auth/signin", "/auth/callback", "/auth/signed-out"]);

/**
 * Normalise a `returnTo` candidate so that the sign-in flow never bounces a
 * user to an off-origin URL or to a path that would re-enter the auth loop.
 * Anything that isn't a same-origin absolute path falls back to "/".
 */
export function sanitizeReturnTo(value: string | null | undefined): string {
  const raw = value?.trim();
  if (!raw || !raw.startsWith("/") || raw.startsWith("//")) return FALLBACK_RETURN_TO;

  try {
    const url = new URL(raw, RETURN_TO_ORIGIN);
    if (url.origin !== RETURN_TO_ORIGIN) return FALLBACK_RETURN_TO;
    if (BLOCKED_RETURN_PATHS.has(url.pathname)) return FALLBACK_RETURN_TO;
    return `${url.pathname}${url.search}${url.hash}` || FALLBACK_RETURN_TO;
  } catch {
    return FALLBACK_RETURN_TO;
  }
}
