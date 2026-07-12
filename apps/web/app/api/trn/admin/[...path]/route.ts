import type { NextRequest } from "next/server";
import { NextResponse } from "next/server";

import { hasAdminRole } from "@/lib/authz";
import { getPublicOrigin } from "@/lib/env";
import { getSessionMe } from "@/lib/session";
import { getAccessTokenOrRefresh } from "@/lib/sessionToken";
import { proxyToBackend } from "@/lib/trnProxy";

function isSafeMethod(method: string) {
  return method === "GET" || method === "HEAD" || method === "OPTIONS";
}

function isSameOrigin(req: NextRequest) {
  const origin = req.headers.get("origin");
  if (!origin) return true;
  let originUrl: URL;
  try {
    originUrl = new URL(origin);
  } catch {
    return false;
  }

  // Prefer the server-configured canonical origin: it cannot be influenced by a client-supplied
  // X-Forwarded-Host, so the check can't be spoofed. Recommended in production.
  const configured = getPublicOrigin();
  if (configured) return originUrl.origin === configured;

  // Fallback (dev / unconfigured) — kept for compatibility; set TRN_PUBLIC_ORIGIN to harden. Note
  // SameSite=Lax cookies are the primary CSRF control here, this is defense-in-depth.
  const forwardedHost = req.headers.get("x-forwarded-host");
  const forwardedProto = req.headers.get("x-forwarded-proto");
  const host = forwardedHost ?? req.headers.get("host");
  const proto = forwardedProto ?? req.nextUrl.protocol.replace(":", "");
  if (!host) return false;
  return originUrl.host === host && originUrl.protocol === `${proto}:`;
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
