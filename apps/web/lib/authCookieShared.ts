// Auth cookie names + the pure "is the access token stale?" check.
//
// Deliberately free of `next/headers` (and any server-only import) so it can also be
// imported by `proxy.ts` (Next 16 middleware), which runs before render and cannot use
// the next/headers cookie store. `lib/authCookies.ts` re-exports these for existing callers.

export const ACCESS_TOKEN_COOKIE = "trn_access_token";
export const REFRESH_TOKEN_COOKIE = "trn_refresh_token";
export const ACCESS_EXPIRES_AT_COOKIE = "trn_access_expires_at";

export const AUTH_COOKIE_NAMES = [
  ACCESS_TOKEN_COOKIE,
  REFRESH_TOKEN_COOKIE,
  ACCESS_EXPIRES_AT_COOKIE
] as const;

/** True when the access token is missing, unparseable, or within 60s of expiry. */
export function shouldRefreshAccessToken(accessExpiresAtUtc: string | null): boolean {
  if (!accessExpiresAtUtc) return true;
  const exp = new Date(accessExpiresAtUtc).getTime();
  if (!Number.isFinite(exp)) return true;
  return exp - Date.now() < 60_000;
}
