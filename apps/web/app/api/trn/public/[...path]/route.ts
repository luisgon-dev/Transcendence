import type { NextRequest } from "next/server";

import { proxyToBackend } from "@/lib/trnProxy";

export async function GET(req: NextRequest, ctx: { params: { path: string[] } }) {
  return proxyToBackend(req, ctx.params.path);
}

export async function POST(req: NextRequest, ctx: { params: { path: string[] } }) {
  return proxyToBackend(req, ctx.params.path);
}

export async function PUT(req: NextRequest, ctx: { params: { path: string[] } }) {
  return proxyToBackend(req, ctx.params.path);
}

export async function DELETE(
  req: NextRequest,
  ctx: { params: { path: string[] } }
) {
  return proxyToBackend(req, ctx.params.path);
}

