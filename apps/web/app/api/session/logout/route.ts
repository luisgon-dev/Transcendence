import { NextResponse } from "next/server";

import { clearAuthCookies, getAuthCookies } from "@/lib/authCookies";
import { getTrnClient } from "@/lib/trnClient";

export async function POST() {
  const { refreshToken } = await getAuthCookies();

  if (refreshToken) {
    try {
      const client = getTrnClient();
      await client.POST("/api/auth/logout", {
        body: { refreshToken }
      });
    } catch {
      // Cookie clear is still required even if backend revoke fails.
    }
  }

  await clearAuthCookies();
  return new NextResponse(null, { status: 204 });
}

