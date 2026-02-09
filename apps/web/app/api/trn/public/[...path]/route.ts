import type { NextRequest } from "next/server";

import { proxyToBackend } from "@/lib/trnProxy";

type Ctx = { params: Promise<{ path: string[] }> };

export async function GET(req: NextRequest, ctx: Ctx) {
  const { path } = await ctx.params;
  return proxyToBackend(req, path);
}

export async function POST(req: NextRequest, ctx: Ctx) {
  const { path } = await ctx.params;
  return proxyToBackend(req, path);
}

export async function PUT(req: NextRequest, ctx: Ctx) {
  const { path } = await ctx.params;
  return proxyToBackend(req, path);
}

export async function DELETE(
  req: NextRequest,
  ctx: Ctx
) {
  const { path } = await ctx.params;
  return proxyToBackend(req, path);
}

