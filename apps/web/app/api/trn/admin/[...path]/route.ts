import type { NextRequest } from "next/server";
import { NextResponse } from "next/server";

import { hasAdminRole } from "@/lib/authz";
import { isSafeMethod, isSameOriginRequest } from "@/lib/bffOrigin";
import { getSessionMe } from "@/lib/session";
import { getAccessTokenOrRefresh } from "@/lib/sessionToken";
import { proxyToBackend } from "@/lib/trnProxy";

type Ctx = { params: Promise<{ path: string[] }> };

async function handler(req: NextRequest, ctx: Ctx) {
  if (!isSafeMethod(req.method) && !isSameOriginRequest(req)) {
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
  if (!accessToken.ok) {
    return NextResponse.json(
      {
        message:
          accessToken.reason === "unavailable"
            ? "Authentication service temporarily unavailable."
            : "Not authenticated."
      },
      { status: accessToken.reason === "unavailable" ? 503 : 401 }
    );
  }

  const { path } = await ctx.params;
  // Backend admin endpoints live under /api/admin/* (this BFF folder strips the "admin"
  // segment), so prepend it: /api/trn/admin/<x> -> /api/admin/<x>.
  return proxyToBackend(req, ["admin", ...path], {
    addHeaders: { authorization: `Bearer ${accessToken.accessToken}` }
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
