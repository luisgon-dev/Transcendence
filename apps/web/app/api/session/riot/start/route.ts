import { randomBytes } from "node:crypto";
import { cookies } from "next/headers";
import { NextResponse, type NextRequest } from "next/server";

import { normalizeLolRegionSlug } from "@/lib/lolRegions";
import {
  appendRsoResult,
  normalizeRsoMode,
  RSO_MODE_COOKIE,
  RSO_REGION_COOKIE,
  RSO_RETURN_COOKIE,
  RSO_STATE_COOKIE,
  safeRsoReturnPath
} from "@/lib/riotRso";
import { getTrnClient } from "@/lib/trnClient";

export async function GET(request: NextRequest) {
  const mode = normalizeRsoMode(request.nextUrl.searchParams.get("mode"));
  const region = normalizeLolRegionSlug(request.nextUrl.searchParams.get("region"));
  const returnTo = safeRsoReturnPath(request.nextUrl.searchParams.get("returnTo"), mode);
  const state = randomBytes(32).toString("base64url");

  try {
    const { data, response } = await getTrnClient().POST("/api/auth/riot/authorize", {
      body: { state }
    });
    if (!data?.authorizationUrl) {
      const path = appendRsoResult(
        mode === "link" ? returnTo : "/account/login",
        "riotError",
        response.status === 503 ? "unavailable" : "start-failed"
      );
      return NextResponse.redirect(new URL(path, request.url));
    }

    const store = await cookies();
    const secure = process.env.NODE_ENV === "production";
    const cookie = { httpOnly: true, sameSite: "lax" as const, secure, path: "/api/session/riot", maxAge: 600 };
    store.set(RSO_STATE_COOKIE, state, cookie);
    store.set(RSO_MODE_COOKIE, mode, cookie);
    store.set(RSO_REGION_COOKIE, region, cookie);
    store.set(RSO_RETURN_COOKIE, returnTo, cookie);
    return NextResponse.redirect(data.authorizationUrl);
  } catch {
    const path = appendRsoResult(
      mode === "link" ? returnTo : "/account/login",
      "riotError",
      "unavailable"
    );
    return NextResponse.redirect(new URL(path, request.url));
  }
}
