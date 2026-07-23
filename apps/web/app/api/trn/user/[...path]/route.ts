import type { NextRequest } from "next/server";
import { NextResponse } from "next/server";

import { clearAuthCookies } from "@/lib/authCookies";
import { isSafeMethod, isSameOriginRequest } from "@/lib/bffOrigin";
import {
  getAccessTokenOrRefresh,
  refreshAccessToken
} from "@/lib/sessionToken";
import { proxyToBackend } from "@/lib/trnProxy";

type Ctx = { params: Promise<{ path: string[] }> };

async function handler(req: NextRequest, ctx: Ctx) {
  if (!isSafeMethod(req.method) && !isSameOriginRequest(req)) {
    return NextResponse.json({ message: "Invalid origin." }, { status: 403 });
  }

  const token = await getAccessTokenOrRefresh();

  if (!token.ok) {
    // Only log out for a definitive auth failure. A transient backend outage (e.g. a
    // container redeploy) must keep the session intact — clearing cookies here is what
    // signed users out on every deploy.
    if (token.reason === "unauthenticated") {
      await clearAuthCookies();
      return NextResponse.json({ message: "Not authenticated." }, { status: 401 });
    }
    return NextResponse.json(
      { message: "Authentication service temporarily unavailable." },
      { status: 503 }
    );
  }

  const { path } = await ctx.params;
  return proxyToBackend(req, path, {
    addHeaders: { authorization: `Bearer ${token.accessToken}` },
    onUnauthorized: async (requestId) => {
      const refreshed = await refreshAccessToken({ requestId });
      if (!refreshed.ok) {
        if (refreshed.reason === "unauthenticated") await clearAuthCookies();
        return null;
      }
      return { authorization: `Bearer ${refreshed.accessToken}` };
    }
  });
}

export async function GET(req: NextRequest, ctx: Ctx) {
  return handler(req, ctx);
}

export async function POST(req: NextRequest, ctx: Ctx) {
  return handler(req, ctx);
}

export async function PUT(req: NextRequest, ctx: Ctx) {
  return handler(req, ctx);
}

export async function DELETE(req: NextRequest, ctx: Ctx) {
  return handler(req, ctx);
}
