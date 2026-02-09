import { cookies } from "next/headers";

import type { components } from "@transcendence/api-client/schema";

export const ACCESS_TOKEN_COOKIE = "trn_access_token";
export const REFRESH_TOKEN_COOKIE = "trn_refresh_token";
export const ACCESS_EXPIRES_AT_COOKIE = "trn_access_expires_at";

export type AuthTokenResponse = components["schemas"]["AuthTokenResponse"];

export async function getAuthCookies() {
  const store = await cookies();
  return {
    accessToken: store.get(ACCESS_TOKEN_COOKIE)?.value ?? null,
    refreshToken: store.get(REFRESH_TOKEN_COOKIE)?.value ?? null,
    accessExpiresAtUtc: store.get(ACCESS_EXPIRES_AT_COOKIE)?.value ?? null
  };
}

export async function setAuthCookies(token: AuthTokenResponse) {
  const store = await cookies();
  const secure = process.env.NODE_ENV === "production";

  // The OpenAPI spec marks some fields nullable. Backend should always send them; guard anyway.
  if (
    !token?.accessToken ||
    !token?.refreshToken ||
    !token?.accessTokenExpiresAtUtc
  ) {
    throw new Error("Invalid auth token response.");
  }

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

export async function clearAuthCookies() {
  const store = await cookies();
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
