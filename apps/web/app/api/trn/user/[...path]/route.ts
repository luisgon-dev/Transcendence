import type { NextRequest } from "next/server";
import { NextResponse } from "next/server";

import {
  clearAuthCookies,
  getAuthCookies,
  setAuthCookies,
  shouldRefreshAccessToken,
  type AuthTokenResponse
} from "@/lib/authCookies";
import { getBackendBaseUrl } from "@/lib/env";
import { getTrnClient } from "@/lib/trnClient";

async function refreshAccessToken(): Promise<string | null> {
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

async function proxy(req: NextRequest, path: string[]) {
  const { accessToken, accessExpiresAtUtc } = await getAuthCookies();
  let token = accessToken;

  if (!token || shouldRefreshAccessToken(accessExpiresAtUtc)) {
    token = await refreshAccessToken();
  }

  if (!token) {
    await clearAuthCookies();
    return NextResponse.json({ message: "Not authenticated." }, { status: 401 });
  }

  const url = new URL(`/api/${path.join("/")}`, getBackendBaseUrl());
  url.search = req.nextUrl.search;

  const headers = new Headers(req.headers);
  headers.delete("cookie");
  headers.delete("host");
  headers.delete("content-length");
  headers.set("authorization", `Bearer ${token}`);

  const body =
    req.method === "GET" || req.method === "HEAD" ? undefined : await req.text();

  const res = await fetch(url, {
    method: req.method,
    headers,
    body,
    redirect: "manual"
  });

  if (res.status === 401) {
    // Token might be stale; retry once after refresh.
    token = await refreshAccessToken();
    if (!token) {
      await clearAuthCookies();
      return NextResponse.json(
        { message: "Not authenticated." },
        { status: 401 }
      );
    }

    headers.set("authorization", `Bearer ${token}`);
    const retry = await fetch(url, {
      method: req.method,
      headers,
      body,
      redirect: "manual"
    });

    const outHeaders = new Headers(retry.headers);
    outHeaders.delete("set-cookie");
    return new Response(retry.body, { status: retry.status, headers: outHeaders });
  }

  const outHeaders = new Headers(res.headers);
  outHeaders.delete("set-cookie");
  return new Response(res.body, { status: res.status, headers: outHeaders });
}

type Ctx = { params: Promise<{ path: string[] }> };

export async function GET(req: NextRequest, ctx: Ctx) {
  const { path } = await ctx.params;
  return proxy(req, path);
}

export async function POST(req: NextRequest, ctx: Ctx) {
  const { path } = await ctx.params;
  return proxy(req, path);
}

export async function PUT(req: NextRequest, ctx: Ctx) {
  const { path } = await ctx.params;
  return proxy(req, path);
}

export async function DELETE(
  req: NextRequest,
  ctx: Ctx
) {
  const { path } = await ctx.params;
  return proxy(req, path);
}
