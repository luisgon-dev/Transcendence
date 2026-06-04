import "server-only";

import {
  getAuthCookies,
  setAuthCookies,
  shouldRefreshAccessToken,
  type AuthTokenResponse
} from "@/lib/authCookies";
import { logEvent } from "@/lib/serverLog";
import { getTrnClient } from "@/lib/trnClient";

function isDynamicServerUsageError(error: unknown) {
  return (
    error instanceof Error &&
    error.message.includes("Dynamic server usage")
  );
}

/**
 * Result of resolving an access token. `unauthenticated` means the session is definitively
 * dead (no refresh token, or the backend rejected it with 401) — callers should clear cookies.
 * `unavailable` means a transient failure (backend unreachable/5xx, e.g. mid-redeploy) —
 * callers must NOT clear cookies, or a brief outage would log the user out permanently.
 */
export type AccessTokenResult =
  | { ok: true; accessToken: string }
  | { ok: false; reason: "unauthenticated" | "unavailable" };

export async function refreshAccessToken(
  options?: { requestId?: string }
): Promise<AccessTokenResult> {
  const { refreshToken } = await getAuthCookies();
  if (!refreshToken) return { ok: false, reason: "unauthenticated" };

  try {
    const client = getTrnClient();
    const headers: Record<string, string> = {};
    if (options?.requestId) {
      headers["x-trn-request-id"] = options.requestId;
    }

    const { data, response } = await client.POST("/api/auth/refresh", {
      body: { refreshToken },
      ...(Object.keys(headers).length > 0 ? { headers } : {})
    });

    if (data) {
      const token = data as AuthTokenResponse;
      if (!token.accessToken) return { ok: false, reason: "unauthenticated" };
      await setAuthCookies(token);
      return { ok: true, accessToken: token.accessToken };
    }

    // No token returned: a 401 means the refresh token is definitively rejected; anything
    // else (5xx / gateway / backend mid-redeploy) is transient and must not end the session.
    if (response?.status === 401) return { ok: false, reason: "unauthenticated" };
    return { ok: false, reason: "unavailable" };
  } catch (error: unknown) {
    if (isDynamicServerUsageError(error)) throw error;
    // Network error / timeout — backend unreachable (e.g. mid-redeploy). Transient.
    logEvent("warn", "token refresh failed (transient)", {
      requestId: options?.requestId,
      error
    });
    return { ok: false, reason: "unavailable" };
  }
}

export async function getAccessTokenOrRefresh(
  options?: { requestId?: string }
): Promise<AccessTokenResult> {
  const { accessToken, accessExpiresAtUtc, refreshToken } = await getAuthCookies();
  if (accessToken && !shouldRefreshAccessToken(accessExpiresAtUtc)) {
    return { ok: true, accessToken };
  }

  if (!refreshToken) return { ok: false, reason: "unauthenticated" };
  return refreshAccessToken(options);
}
