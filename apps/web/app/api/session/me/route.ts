import { NextResponse } from "next/server";

import type { components } from "@transcendence/api-client/schema";

import {
  clearAuthCookies,
  getAuthCookies,
  setAuthCookies,
  shouldRefreshAccessToken,
  type AuthTokenResponse
} from "@/lib/authCookies";
import { getTrnClient } from "@/lib/trnClient";

type AuthMeResponse = components["schemas"]["AuthMeResponse"];

async function refresh(): Promise<string | null> {
  const { refreshToken } = await getAuthCookies();
  if (!refreshToken) return null;

  const client = getTrnClient();
  const { data } = await client.POST("/api/auth/refresh", {
    body: { refreshToken }
  });

  if (!data) return null;
  const token = data as AuthTokenResponse;
  await setAuthCookies(token);
  return token.accessToken ?? null;
}

export async function GET() {
  const client = getTrnClient();
  const { accessToken, accessExpiresAtUtc } = await getAuthCookies();
  let token = accessToken;

  if (!token || shouldRefreshAccessToken(accessExpiresAtUtc)) {
    token = await refresh();
  }

  if (!token) {
    return NextResponse.json({ authenticated: false });
  }

  let me = await client.GET("/api/auth/me", {
    headers: { authorization: `Bearer ${token}` }
  });

  if (me.response.status === 401) {
    token = await refresh();
    if (!token) {
      await clearAuthCookies();
      return NextResponse.json({ authenticated: false });
    }

    me = await client.GET("/api/auth/me", {
      headers: { authorization: `Bearer ${token}` }
    });
  }

  if (!me.data) {
    if (me.response.status === 401) await clearAuthCookies();
    return NextResponse.json({ authenticated: false });
  }

  const data = me.data as AuthMeResponse;
  return NextResponse.json({
    authenticated: true,
    subject: data.subject ?? null,
    name: data.name ?? null,
    roles: data.roles ?? [],
    authType: data.authType ?? null
  });
}
