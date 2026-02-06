import { cookies } from "next/headers";

export const ACCESS_TOKEN_COOKIE = "trn_access_token";
export const REFRESH_TOKEN_COOKIE = "trn_refresh_token";
export const ACCESS_EXPIRES_AT_COOKIE = "trn_access_expires_at";

export type AuthTokenResponse = {
  accessToken: string;
  refreshToken: string;
  accessTokenExpiresAtUtc: string;
  tokenType?: string;
};

export function getAuthCookies() {
  const store = cookies();
  return {
    accessToken: store.get(ACCESS_TOKEN_COOKIE)?.value ?? null,
    refreshToken: store.get(REFRESH_TOKEN_COOKIE)?.value ?? null,
    accessExpiresAtUtc: store.get(ACCESS_EXPIRES_AT_COOKIE)?.value ?? null
  };
}

export function setAuthCookies(token: AuthTokenResponse) {
  const store = cookies();
  const secure = process.env.NODE_ENV === "production";
  const accessExpires = new Date(token.accessTokenExpiresAtUtc);
  const accessCookieBase = {
    httpOnly: true as const,
    sameSite: "lax" as const,
    secure,
    path: "/",
    expires: Number.isFinite(accessExpires.getTime()) ? accessExpires : undefined
  };

  // Access token: short-lived, HttpOnly
  store.set(ACCESS_TOKEN_COOKIE, token.accessToken, {
    ...accessCookieBase
  });

  store.set(ACCESS_EXPIRES_AT_COOKIE, token.accessTokenExpiresAtUtc, {
    ...accessCookieBase
  });

  // Refresh token: longer-lived, HttpOnly
  store.set(REFRESH_TOKEN_COOKIE, token.refreshToken, {
    httpOnly: true,
    sameSite: "lax",
    secure,
    path: "/",
    maxAge: 60 * 60 * 24 * 7
  });
}

export function clearAuthCookies() {
  const store = cookies();
  store.delete({ name: ACCESS_TOKEN_COOKIE, path: "/" });
  store.delete({ name: REFRESH_TOKEN_COOKIE, path: "/" });
  store.delete({ name: ACCESS_EXPIRES_AT_COOKIE, path: "/" });
}

export function shouldRefreshAccessToken(accessExpiresAtUtc: string | null) {
  if (!accessExpiresAtUtc) return true;
  const exp = new Date(accessExpiresAtUtc).getTime();
  if (!Number.isFinite(exp)) return true;
  return exp - Date.now() < 60_000;
}
