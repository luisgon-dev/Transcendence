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

export async function refreshAccessToken(
  options?: { requestId?: string }
): Promise<string | null> {
  const { refreshToken } = await getAuthCookies();
  if (!refreshToken) return null;

  try {
    const client = getTrnClient();
    const headers: Record<string, string> = {};
    if (options?.requestId) {
      headers["x-trn-request-id"] = options.requestId;
    }

    const { data } = await client.POST("/api/auth/refresh", {
      body: { refreshToken },
      ...(Object.keys(headers).length > 0 ? { headers } : {})
    });

    if (!data) return null;
    const token = data as AuthTokenResponse;
    await setAuthCookies(token);
    return token.accessToken ?? null;
  } catch (error: unknown) {
    if (isDynamicServerUsageError(error)) throw error;
    logEvent("warn", "token refresh failed", {
      requestId: options?.requestId,
      error
    });
    return null;
  }
}

export async function getAccessTokenOrRefresh(
  options?: { requestId?: string }
): Promise<string | null> {
  const { accessToken, accessExpiresAtUtc, refreshToken } = await getAuthCookies();
  if (accessToken && !shouldRefreshAccessToken(accessExpiresAtUtc)) {
    return accessToken;
  }

  if (!refreshToken) return null;
  return refreshAccessToken(options);
}
