import { NextResponse, type NextRequest } from "next/server";

import { getBackendBaseUrl } from "@/lib/env";
import { setAuthCookies, type AuthTokenResponse } from "@/lib/authCookies";

type RegisterBody = { email?: string; password?: string };

export async function POST(req: NextRequest) {
  const body = (await req.json().catch(() => null)) as RegisterBody | null;
  if (!body?.email || !body?.password) {
    return NextResponse.json(
      { message: "Email and password are required." },
      { status: 400 }
    );
  }

  const res = await fetch(`${getBackendBaseUrl()}/api/auth/register`, {
    method: "POST",
    headers: { "content-type": "application/json" },
    body: JSON.stringify({ email: body.email, password: body.password })
  });

  if (!res.ok) {
    const message = await res.text().catch(() => "");
    return NextResponse.json(
      { message: message || "Registration failed." },
      { status: res.status }
    );
  }

  const token = (await res.json()) as AuthTokenResponse;
  setAuthCookies(token);
  return NextResponse.json({ ok: true });
}

