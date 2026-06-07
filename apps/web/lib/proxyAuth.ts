// Pure helpers for the proxy (Next 16 middleware) token-refresh path. Kept free of
// next/headers and any I/O so they are unit-testable and middleware-safe. The proxy
// (proxy.ts) does the cookie reads, the backend fetch, and applies these results.

import {
  ACCESS_EXPIRES_AT_COOKIE,
  ACCESS_TOKEN_COOKIE,
  REFRESH_TOKEN_COOKIE,
  shouldRefreshAccessToken
} from "@/lib/authCookieShared";

export type ProxyAuthCookieState = {
  accessToken: string | null;
  accessExpiresAtUtc: string | null;
  refreshToken: string | null;
};

export type AuthCookieSpec = {
  name: string;
  value: string;
  httpOnly: true;
  sameSite: "lax";
  secure: boolean;
  path: "/";
  expires?: Date;
  maxAge?: number;
};

/**
 * Whether the proxy should attempt a backend refresh for this request. Only when:
 * - there IS a refresh token (otherwise the visitor is anonymous — nothing to do),
 * - the access token is missing / within 60s of expiry, and
 * - this is a real navigation, not a speculative prefetch. Refreshing on prefetch
 *   would consume the single-use refresh token for a page the user may never open.
 */
export function shouldAttemptProxyRefresh(
  state: ProxyAuthCookieState,
  isPrefetch: boolean
): boolean {
  if (!state.refreshToken) return false;
  if (isPrefetch) return false;
  return shouldRefreshAccessToken(state.accessExpiresAtUtc);
}

export type ParsedAuthToken = {
  accessToken: string;
  refreshToken: string;
  accessTokenExpiresAtUtc: string;
};

/**
 * Validate the backend /api/auth/refresh 200 body before trusting it to overwrite a
 * live session. Returns null on any missing/blank field, so a malformed response can
 * never corrupt good cookies.
 */
export function parseAuthTokenResponse(json: unknown): ParsedAuthToken | null {
  if (!json || typeof json !== "object") return null;
  const o = json as Record<string, unknown>;
  const { accessToken, refreshToken, accessTokenExpiresAtUtc } = o;
  if (
    typeof accessToken !== "string" ||
    accessToken.length === 0 ||
    typeof refreshToken !== "string" ||
    refreshToken.length === 0 ||
    typeof accessTokenExpiresAtUtc !== "string" ||
    accessTokenExpiresAtUtc.length === 0
  ) {
    return null;
  }
  return { accessToken, refreshToken, accessTokenExpiresAtUtc };
}

/**
 * Build the three auth cookie specs from a refreshed token, matching setAuthCookies
 * exactly (HttpOnly, SameSite=Lax, access cookies expire with the token, refresh
 * cookie lives 7 days).
 */
export function buildAuthCookieSpecs(
  token: ParsedAuthToken,
  options: { secure: boolean }
): AuthCookieSpec[] {
  const exp = new Date(token.accessTokenExpiresAtUtc);
  const accessExpires = Number.isFinite(exp.getTime()) ? exp : undefined;
  const base = {
    httpOnly: true as const,
    sameSite: "lax" as const,
    secure: options.secure,
    path: "/" as const
  };
  return [
    { name: ACCESS_TOKEN_COOKIE, value: token.accessToken, ...base, expires: accessExpires },
    {
      name: ACCESS_EXPIRES_AT_COOKIE,
      value: token.accessTokenExpiresAtUtc,
      ...base,
      expires: accessExpires
    },
    { name: REFRESH_TOKEN_COOKIE, value: token.refreshToken, ...base, maxAge: 60 * 60 * 24 * 7 }
  ];
}
