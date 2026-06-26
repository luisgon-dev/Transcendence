import type { NextRequest } from "next/server";
import { NextResponse } from "next/server";

import { isAllowedPublicProxyPath } from "@/lib/publicProxyAllowlist";
import { proxyToBackend } from "@/lib/trnProxy";

type Ctx = { params: Promise<{ path: string[] }> };

async function handler(req: NextRequest, ctx: Ctx) {
  const { path } = await ctx.params;
  if (!isAllowedPublicProxyPath(req.method, path)) {
    return NextResponse.json({ message: "Not found." }, { status: 404 });
  }

  return proxyToBackend(req, path);
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
