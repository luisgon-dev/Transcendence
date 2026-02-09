import type { NextRequest } from "next/server";
import { NextResponse } from "next/server";

import { getBackendApiKey } from "@/lib/env";
import { proxyToBackend } from "@/lib/trnProxy";

function resolveApiKey() {
  try {
    return { ok: true as const, value: getBackendApiKey() };
  } catch (err: unknown) {
    return {
      ok: false as const,
      value: err instanceof Error ? err.message : "Missing API key."
    };
  }
}

type Ctx = { params: Promise<{ path: string[] }> };

async function handler(req: NextRequest, ctx: Ctx) {
  const key = resolveApiKey();
  if (!key.ok) {
    console.error("Missing backend API key for AppOnly proxy:", key.value);
    if (process.env.NODE_ENV === "production") {
      return NextResponse.json({ message: "Service unavailable." }, { status: 503 });
    }
    return NextResponse.json({ message: key.value }, { status: 500 });
  }

  const { path } = await ctx.params;
  return proxyToBackend(req, path, {
    addHeaders: { "X-API-Key": key.value }
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
