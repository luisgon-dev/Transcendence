import { timingSafeEqual } from "node:crypto";
import { cookies } from "next/headers";
import { NextResponse, type NextRequest } from "next/server";

import { setAuthCookies, type AuthTokenResponse } from "@/lib/authCookies";
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
import { getAccessTokenOrRefresh } from "@/lib/sessionToken";
import { getTrnClient } from "@/lib/trnClient";

function statesMatch(expected: string | null, actual: string | null): boolean {
  if (!expected || !actual) return false;
  const left = Buffer.from(expected);
  const right = Buffer.from(actual);
  return left.length === right.length && timingSafeEqual(left, right);
}

async function clearRsoCookies() {
  const store = await cookies();
  for (const name of [RSO_STATE_COOKIE, RSO_MODE_COOKIE, RSO_REGION_COOKIE, RSO_RETURN_COOKIE]) {
    store.delete({ name, path: "/api/session/riot" });
  }
}

export async function GET(request: NextRequest) {
  const store = await cookies();
  const mode = normalizeRsoMode(store.get(RSO_MODE_COOKIE)?.value ?? null);
  const region = normalizeLolRegionSlug(store.get(RSO_REGION_COOKIE)?.value ?? null);
  const returnTo = safeRsoReturnPath(store.get(RSO_RETURN_COOKIE)?.value ?? null, mode);
  const expectedState = store.get(RSO_STATE_COOKIE)?.value ?? null;
  const state = request.nextUrl.searchParams.get("state");
  const code = request.nextUrl.searchParams.get("code");
  const providerError = request.nextUrl.searchParams.get("error");

  if (providerError || !code || !statesMatch(expectedState, state)) {
    await clearRsoCookies();
    const reason = providerError === "access_denied" ? "cancelled" : "invalid-state";
    return NextResponse.redirect(
      new URL(appendRsoResult(mode === "link" ? returnTo : "/account/login", "riotError", reason), request.url)
    );
  }

  try {
    const client = getTrnClient();
    if (mode === "link") {
      const token = await getAccessTokenOrRefresh();
      if (!token.ok) {
        await clearRsoCookies();
        return NextResponse.redirect(
          new URL(appendRsoResult("/account/login", "riotError", "session-expired"), request.url)
        );
      }
      const { data, response } = await client.POST("/api/users/me/riot-account/complete", {
        body: { code, region },
        headers: { authorization: `Bearer ${token.accessToken}` }
      });
      await clearRsoCookies();
      return NextResponse.redirect(
        new URL(
          appendRsoResult(returnTo, data ? "riot" : "riotError", data ? "linked" : `link-${response.status}`),
          request.url
        )
      );
    }

    const { data, response } = await client.POST("/api/auth/riot/complete", {
      body: { code, region }
    });
    if (!data?.tokens) {
      await clearRsoCookies();
      return NextResponse.redirect(
        new URL(appendRsoResult("/account/login", "riotError", `login-${response.status}`), request.url)
      );
    }

    await setAuthCookies(data.tokens as AuthTokenResponse);
    await clearRsoCookies();
    return NextResponse.redirect(new URL(appendRsoResult(returnTo, "riot", "signed-in"), request.url));
  } catch {
    await clearRsoCookies();
    return NextResponse.redirect(
      new URL(appendRsoResult(mode === "link" ? returnTo : "/account/login", "riotError", "unavailable"), request.url)
    );
  }
}
