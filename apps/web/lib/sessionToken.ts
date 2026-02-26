import "server-only";

import {
  getAuthCookies,
  setAuthCookies,
  shouldRefreshAccessToken,
  type AuthTokenResponse
} from "@/lib/authCookies";
import { getTrnClient } from "@/lib/trnClient";

async function refreshAccessToken(refreshToken: string): Promise<string | null> {
  const client = getTrnClient();
  const { data } = await client.POST("/api/auth/refresh", {
    body: { refreshToken }
  });

  if (!data) return null;
  const token = data as AuthTokenResponse;
  await setAuthCookies(token);
  return token.accessToken ?? null;
}

export async function getAccessTokenOrRefresh(): Promise<string | null> {
  const { accessToken, accessExpiresAtUtc, refreshToken } = await getAuthCookies();
  if (accessToken && !shouldRefreshAccessToken(accessExpiresAtUtc)) {
    return accessToken;
  }

  if (!refreshToken) return null;
  try {
    return await refreshAccessToken(refreshToken);
  } catch {
    return null;
  }
}
