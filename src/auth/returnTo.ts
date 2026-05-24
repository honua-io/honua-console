const FALLBACK_RETURN_TO = "/";
const RETURN_TO_ORIGIN = "https://portal.local";
const BLOCKED_RETURN_PATHS = new Set(["/auth/signin", "/auth/callback", "/auth/signed-out"]);

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
