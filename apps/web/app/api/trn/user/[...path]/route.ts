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

async function refreshAccessToken() {
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

async function proxy(req: NextRequest, path: string[]) {
  const { accessToken, accessExpiresAtUtc } = getAuthCookies();
  let token = accessToken;

  if (!token || shouldRefreshAccessToken(accessExpiresAtUtc)) {
    token = await refreshAccessToken();
  }

  if (!token) {
    clearAuthCookies();
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
      clearAuthCookies();
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

export async function GET(req: NextRequest, ctx: { params: { path: string[] } }) {
  return proxy(req, ctx.params.path);
}

export async function POST(req: NextRequest, ctx: { params: { path: string[] } }) {
  return proxy(req, ctx.params.path);
}

export async function PUT(req: NextRequest, ctx: { params: { path: string[] } }) {
  return proxy(req, ctx.params.path);
}

export async function DELETE(
  req: NextRequest,
  ctx: { params: { path: string[] } }
) {
  return proxy(req, ctx.params.path);
}
