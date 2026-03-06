import "server-only";

import type { components } from "@transcendence/api-client/schema";

import {
  clearAuthCookies,
  getAuthCookies,
  shouldRefreshAccessToken
} from "@/lib/authCookies";
import { logEvent } from "@/lib/serverLog";
import { refreshAccessToken } from "@/lib/sessionToken";
import { getTrnClient } from "@/lib/trnClient";

export type SessionMe =
  | { authenticated: false }
  | {
      authenticated: true;
      subject: string | null;
      name: string | null;
      roles: string[];
      authType: string | null;
    };

type AuthMeResponse = components["schemas"]["AuthMeResponse"];

function isDynamicServerUsageError(error: unknown) {
  return (
    error instanceof Error &&
    error.message.includes("Dynamic server usage")
  );
}

export async function getSessionMe(): Promise<SessionMe> {
  try {
    const client = getTrnClient();
    const { accessToken, accessExpiresAtUtc } = await getAuthCookies();
    let token = accessToken;

    if (!token || shouldRefreshAccessToken(accessExpiresAtUtc)) {
      token = await refreshAccessToken();
    }

    if (!token) {
      return { authenticated: false };
    }

    let me: Awaited<ReturnType<typeof client.GET>>;
    try {
      me = await client.GET("/api/auth/me", {
        headers: { authorization: `Bearer ${token}` }
      });
    } catch (error: unknown) {
      if (isDynamicServerUsageError(error)) throw error;
      logEvent("warn", "session me request failed", { error });
      return { authenticated: false };
    }

    if (me.response.status === 401) {
      token = await refreshAccessToken();
      if (!token) {
        await clearAuthCookies();
        return { authenticated: false };
      }

      try {
        me = await client.GET("/api/auth/me", {
          headers: { authorization: `Bearer ${token}` }
        });
      } catch (error: unknown) {
        if (isDynamicServerUsageError(error)) throw error;
        logEvent("warn", "session me retry failed", { error });
        return { authenticated: false };
      }
    }

    if (!me.data) {
      if (me.response.status === 401) await clearAuthCookies();
      return { authenticated: false };
    }

    const data = me.data as AuthMeResponse;
    return {
      authenticated: true,
      subject: data.subject ?? null,
      name: data.name ?? null,
      roles: data.roles ?? [],
      authType: data.authType ?? null
    };
  } catch (error: unknown) {
    if (isDynamicServerUsageError(error)) throw error;
    logEvent("error", "getSessionMe failed unexpectedly", { error });
    return { authenticated: false };
  }
}
