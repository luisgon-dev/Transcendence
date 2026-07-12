import { NextResponse, type NextRequest } from "next/server";

import { setAuthCookies, type AuthTokenResponse } from "@/lib/authCookies";
import { resolveClientIp } from "@/lib/clientIp";
import { getTrnClient } from "@/lib/trnClient";

type LoginBody = { email?: string; password?: string };

export async function POST(req: NextRequest) {
  const body = (await req.json().catch(() => null)) as LoginBody | null;
  if (!body?.email || !body?.password) {
    return NextResponse.json(
      { message: "Email and password are required." },
      { status: 400 }
    );
  }

  const client = getTrnClient();
  // Forward the real client IP so the backend's auth rate limiter partitions per-attacker instead of
  // collapsing every login into one global window keyed on the BFF's own address.
  const clientIp = resolveClientIp(req.headers);
  const { data, error, response } = await client.POST("/api/auth/login", {
    body: { email: body.email, password: body.password },
    ...(clientIp ? { headers: { "x-forwarded-for": clientIp } } : {})
  });

  if (!data) {
    const message =
      (error as { detail?: string; title?: string } | undefined)?.detail ??
      (error as { detail?: string; title?: string } | undefined)?.title ??
      "Login failed.";
    return NextResponse.json(
      { message },
      { status: response.status }
    );
  }

  const token = data as AuthTokenResponse;
  await setAuthCookies(token);
  return NextResponse.json({ ok: true });
}

