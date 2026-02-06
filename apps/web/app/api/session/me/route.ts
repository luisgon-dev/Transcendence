import { NextResponse } from "next/server";

import {
  clearAuthCookies,
  getAuthCookies,
  setAuthCookies,
  shouldRefreshAccessToken,
  type AuthTokenResponse
} from "@/lib/authCookies";
import { getBackendBaseUrl } from "@/lib/env";

type BackendMeResponse = {
  subject?: string | null;
  name?: string | null;
  roles?: string[];
  authType?: string | null;
};

async function refresh() {
  const { refreshToken } = getAuthCookies();
  if (!refreshToken) return null;

  const res = await fetch(`${getBackendBaseUrl()}/api/auth/refresh`, {
    method: "POST",
    headers: { "content-type": "application/json" },
    body: JSON.stringify({ refreshToken })
  });

  if (!res.ok) return null;
  const token = (await res.json()) as AuthTokenResponse;
  setAuthCookies(token);
  return token.accessToken;
}

export async function GET() {
  const { accessToken, accessExpiresAtUtc } = getAuthCookies();
  let token = accessToken;

  if (!token || shouldRefreshAccessToken(accessExpiresAtUtc)) {
    token = await refresh();
  }

  if (!token) {
    return NextResponse.json({ authenticated: false });
  }

  let meRes = await fetch(`${getBackendBaseUrl()}/api/auth/me`, {
    headers: { authorization: `Bearer ${token}` }
  });

  if (meRes.status === 401) {
    token = await refresh();
    if (!token) {
      clearAuthCookies();
      return NextResponse.json({ authenticated: false });
    }

    meRes = await fetch(`${getBackendBaseUrl()}/api/auth/me`, {
      headers: { authorization: `Bearer ${token}` }
    });
  }

  if (!meRes.ok) {
    if (meRes.status === 401) clearAuthCookies();
    return NextResponse.json({ authenticated: false });
  }

  const data = (await meRes.json()) as BackendMeResponse;
  return NextResponse.json({
    authenticated: true,
    subject: data.subject ?? null,
    name: data.name ?? null,
    roles: data.roles ?? [],
    authType: data.authType ?? null
  });
}
