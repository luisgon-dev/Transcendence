import type { NextRequest } from "next/server";
import { NextResponse } from "next/server";

import { clearAuthCookies } from "@/lib/authCookies";
import {
  getAccessTokenOrRefresh,
  refreshAccessToken
} from "@/lib/sessionToken";
import { proxyToBackend } from "@/lib/trnProxy";

type Ctx = { params: Promise<{ path: string[] }> };

async function handler(req: NextRequest, ctx: Ctx) {
  const token = await getAccessTokenOrRefresh();

  if (!token) {
    await clearAuthCookies();
    return NextResponse.json(
      { message: "Not authenticated." },
      { status: 401 }
    );
  }

  const { path } = await ctx.params;
  return proxyToBackend(req, path, {
    addHeaders: { authorization: `Bearer ${token}` },
    onUnauthorized: async (requestId) => {
      const refreshed = await refreshAccessToken({ requestId });
      if (!refreshed) {
        await clearAuthCookies();
        return null;
      }
      return { authorization: `Bearer ${refreshed}` };
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
