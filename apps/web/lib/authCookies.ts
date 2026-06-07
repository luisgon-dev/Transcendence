import { cookies } from "next/headers";

import type { components } from "@transcendence/api-client";

export const ACCESS_TOKEN_COOKIE = "trn_access_token";
export const REFRESH_TOKEN_COOKIE = "trn_refresh_token";
export const ACCESS_EXPIRES_AT_COOKIE = "trn_access_expires_at";

export type AuthTokenResponse = components["schemas"]["AuthTokenResponse"];

/**
 * `cookies().set`/`.delete` throw when called during a render — Next.js only allows
 * cookie mutation in Server Actions or Route Handlers. Some helpers here run in both
 * contexts: `getSessionMe()` refreshes/clears tokens, and it also renders the header
 * (`AccountNav`). Treat a render-context mutation as best-effort — skip it (the write
 * still persists when these run inside the BFF route handlers / account actions, which
 * is where it matters) and re-throw anything that isn't the read-only-store error.
 */
function isReadonlyCookieStoreError(error: unknown): boolean {
  // Matches the exact Next.js message to avoid swallowing unrelated errors:
  // "Cookies can only be modified in a Server Action or Route Handler."
  return (
    error instanceof Error &&
    error.message.includes("can only be modified in a Server Action or Route Handler")
  );
}

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

  try {
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
  } catch (error: unknown) {
    if (!isReadonlyCookieStoreError(error)) throw error;
    // Render context (e.g. header session check) — persisted later by a route handler/action.
  }
}

export async function clearAuthCookies() {
  const store = await cookies();
  try {
    store.delete({ name: ACCESS_TOKEN_COOKIE, path: "/" });
    store.delete({ name: REFRESH_TOKEN_COOKIE, path: "/" });
    store.delete({ name: ACCESS_EXPIRES_AT_COOKIE, path: "/" });
  } catch (error: unknown) {
    if (!isReadonlyCookieStoreError(error)) throw error;
    // Render context — nothing to persist; cookies are cleared on the next action/route handler.
  }
}

export function shouldRefreshAccessToken(accessExpiresAtUtc: string | null) {
  if (!accessExpiresAtUtc) return true;
  const exp = new Date(accessExpiresAtUtc).getTime();
  if (!Number.isFinite(exp)) return true;
  return exp - Date.now() < 60_000;
}
