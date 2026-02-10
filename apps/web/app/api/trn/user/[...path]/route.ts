import type { NextRequest } from "next/server";
import { NextResponse } from "next/server";

import {
  clearAuthCookies,
  getAuthCookies,
  setAuthCookies,
  shouldRefreshAccessToken,
  type AuthTokenResponse
} from "@/lib/authCookies";
import {
  getBackendBaseUrl,
  getBackendTimeoutMs,
  getErrorVerbosity
} from "@/lib/env";
import { fetchWithTimeout, isAbortError } from "@/lib/fetchWithTimeout";
import { newRequestId } from "@/lib/requestId";
import { logEvent } from "@/lib/serverLog";
import { getTrnClient } from "@/lib/trnClient";

async function refreshAccessToken(requestId: string): Promise<string | null> {
  const { refreshToken } = await getAuthCookies();
  if (!refreshToken) return null;

  try {
    const client = getTrnClient();
    const { data } = await client.POST("/api/auth/refresh", {
      body: { refreshToken },
      headers: { "x-trn-request-id": requestId }
    });

    if (!data) return null;
    const token = data as AuthTokenResponse;
    await setAuthCookies(token);
    return token.accessToken ?? null;
  } catch (err: unknown) {
    logEvent("error", "refresh access token failed", { requestId, error: err });
    return null;
  }
}

async function proxy(req: NextRequest, path: string[]) {
  const requestId = newRequestId();
  const { accessToken, accessExpiresAtUtc } = await getAuthCookies();
  let token = accessToken;

  if (!token || shouldRefreshAccessToken(accessExpiresAtUtc)) {
    token = await refreshAccessToken(requestId);
  }

  if (!token) {
    await clearAuthCookies();
    return NextResponse.json(
      { message: "Not authenticated.", requestId },
      { status: 401, headers: { "x-trn-request-id": requestId } }
    );
  }

  const url = new URL(`/api/${path.join("/")}`, getBackendBaseUrl());
  url.search = req.nextUrl.search;

  const headers = new Headers(req.headers);
  headers.delete("cookie");
  headers.delete("host");
  headers.delete("content-length");
  headers.set("authorization", `Bearer ${token}`);
  headers.set("x-trn-request-id", requestId);

  const body =
    req.method === "GET" || req.method === "HEAD" ? undefined : await req.text();

  let res: Response;
  try {
    res = await fetchWithTimeout(
      url,
      {
        method: req.method,
        headers,
        body,
        redirect: "manual"
      },
      { timeoutMs: getBackendTimeoutMs() }
    );
  } catch (err: unknown) {
    const status = isAbortError(err) ? 504 : 503;
    const verbosity = getErrorVerbosity();
    logEvent("error", "user proxy upstream fetch failed", {
      requestId,
      status,
      method: req.method,
      url: url.toString(),
      error: err
    });

    return NextResponse.json(
      {
        message:
          status === 504
            ? "Timed out reaching the backend."
            : "We are having trouble reaching the backend.",
        code: status === 504 ? "BACKEND_TIMEOUT" : "BACKEND_UNREACHABLE",
        requestId,
        ...(verbosity === "verbose"
          ? { detail: err instanceof Error ? err.message : String(err) }
          : null)
      },
      { status, headers: { "x-trn-request-id": requestId } }
    );
  }

  if (res.status === 401) {
    // Token might be stale; retry once after refresh.
    token = await refreshAccessToken(requestId);
    if (!token) {
      await clearAuthCookies();
      return NextResponse.json(
        { message: "Not authenticated.", requestId },
        { status: 401, headers: { "x-trn-request-id": requestId } }
      );
    }

    headers.set("authorization", `Bearer ${token}`);
    let retry: Response;
    try {
      retry = await fetchWithTimeout(
        url,
        {
          method: req.method,
          headers,
          body,
          redirect: "manual"
        },
        { timeoutMs: getBackendTimeoutMs() }
      );
    } catch (err: unknown) {
      const status = isAbortError(err) ? 504 : 503;
      const verbosity = getErrorVerbosity();
      logEvent("error", "user proxy upstream retry failed", {
        requestId,
        status,
        method: req.method,
        url: url.toString(),
        error: err
      });

      return NextResponse.json(
        {
          message:
            status === 504
              ? "Timed out reaching the backend."
              : "We are having trouble reaching the backend.",
          code: status === 504 ? "BACKEND_TIMEOUT" : "BACKEND_UNREACHABLE",
          requestId,
          ...(verbosity === "verbose"
            ? { detail: err instanceof Error ? err.message : String(err) }
            : null)
        },
        { status, headers: { "x-trn-request-id": requestId } }
      );
    }

    const outHeaders = new Headers(retry.headers);
    outHeaders.delete("set-cookie");
    outHeaders.set("x-trn-request-id", requestId);
    return new Response(retry.body, { status: retry.status, headers: outHeaders });
  }

  const outHeaders = new Headers(res.headers);
  outHeaders.delete("set-cookie");
  outHeaders.set("x-trn-request-id", requestId);
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
