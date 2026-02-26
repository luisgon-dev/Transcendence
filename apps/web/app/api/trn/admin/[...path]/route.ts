import type { NextRequest } from "next/server";
import { NextResponse } from "next/server";

import { hasAdminRole } from "@/lib/authz";
import { getSessionMe } from "@/lib/session";
import { getAccessTokenOrRefresh } from "@/lib/sessionToken";
import { proxyToBackend } from "@/lib/trnProxy";

function isSafeMethod(method: string) {
  return method === "GET" || method === "HEAD" || method === "OPTIONS";
}

function isSameOrigin(req: NextRequest) {
  const origin = req.headers.get("origin");
  if (!origin) return true;
  try {
    const originUrl = new URL(origin);
    const forwardedHost = req.headers.get("x-forwarded-host");
    const forwardedProto = req.headers.get("x-forwarded-proto");
    const host = forwardedHost ?? req.headers.get("host");
    const proto = forwardedProto ?? req.nextUrl.protocol.replace(":", "");
    if (!host) return false;
    return originUrl.host === host && originUrl.protocol === `${proto}:`;
  } catch {
    return false;
  }
}

type Ctx = { params: Promise<{ path: string[] }> };

async function handler(req: NextRequest, ctx: Ctx) {
  if (!isSafeMethod(req.method) && !isSameOrigin(req)) {
    return NextResponse.json({ message: "Invalid origin." }, { status: 403 });
  }

  const me = await getSessionMe();
  if (!me.authenticated) {
    return NextResponse.json({ message: "Not authenticated." }, { status: 401 });
  }

  if (!hasAdminRole(me.roles)) {
    return NextResponse.json({ message: "Forbidden." }, { status: 403 });
  }

  const accessToken = await getAccessTokenOrRefresh();
  if (!accessToken) {
    return NextResponse.json({ message: "Not authenticated." }, { status: 401 });
  }

  const { path } = await ctx.params;
  return proxyToBackend(req, path, {
    addHeaders: { authorization: `Bearer ${accessToken}` }
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
