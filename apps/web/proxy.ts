import { NextResponse, type NextRequest } from "next/server";

import {
  ACCESS_EXPIRES_AT_COOKIE,
  ACCESS_TOKEN_COOKIE,
  REFRESH_TOKEN_COOKIE
} from "@/lib/authCookieShared";
import { getBackendBaseUrl } from "@/lib/env";
import {
  buildAuthCookieSpecs,
  parseAuthTokenResponse,
  shouldAttemptProxyRefresh,
  type AuthCookieSpec
} from "@/lib/proxyAuth";

// Keep the per-request refresh from delaying navigation if the backend stalls — on
// timeout we fail safe (pass through). Short by design: this only runs when the access
// token is already stale, and a healthy refresh is tens of milliseconds.
const PROXY_REFRESH_TIMEOUT_MS = 4000;

export const config = {
  // Run on page navigations so the access token is refreshed AND persisted before
  // server components render. The header's session check (AccountNav -> getSessionMe)
  // and the admin layout (requireAdminSession) otherwise refresh during render, where
  // cookie writes can't persist — which burns the single-use, rotated refresh token and
  // intermittently logs valid users out. Excludes API routes (route handlers manage
  // their own auth in a writable context), Next internals, and static files. Static files
  // are matched by a fixed extension set rather than "any path ending in `.<word>`", so a
  // future page route that ends in a dotted segment still gets token refresh instead of
  // silently falling back to render-time refresh.
  matcher: [
    "/((?!api|_next/static|_next/image|favicon.ico|icon.svg|.*\\.(?:css|js|mjs|map|json|txt|xml|ico|png|jpe?g|gif|svg|webp|avif|woff2?|ttf|otf|eot|webmanifest)$).*)"
  ]
};

function isVerbose() {
  return (process.env.TRN_ERROR_VERBOSITY ?? "").toLowerCase().trim() === "verbose";
}

function isSummonerPath(pathname: string) {
  return pathname.startsWith("/lol/summoners/");
}

function isPrefetchRequest(req: NextRequest) {
  return (
    req.headers.get("next-router-prefetch") === "1" ||
    req.headers.get("purpose") === "prefetch"
  );
}

function pickHeader(req: NextRequest, key: string) {
  return req.headers.get(key) ?? undefined;
}

/**
 * Best-effort access-token refresh for a page navigation. Returns the new cookie specs
 * to apply, or null to leave cookies untouched.
 *
 * NEVER throws and NEVER clears cookies: on any failure (401, 5xx, network, timeout,
 * malformed body) the request passes through unchanged, so a transient backend issue
 * can't log users out. Dead-session cleanup stays the job of the BFF route handlers,
 * which clear cookies in their own writable context.
 */
async function refreshSessionCookies(
  req: NextRequest,
  requestId: string
): Promise<AuthCookieSpec[] | null> {
  try {
    const refreshToken = req.cookies.get(REFRESH_TOKEN_COOKIE)?.value ?? null;
    const accessToken = req.cookies.get(ACCESS_TOKEN_COOKIE)?.value ?? null;
    const accessExpiresAtUtc = req.cookies.get(ACCESS_EXPIRES_AT_COOKIE)?.value ?? null;

    if (
      !shouldAttemptProxyRefresh(
        { accessToken, accessExpiresAtUtc, refreshToken },
        isPrefetchRequest(req)
      )
    ) {
      return null;
    }

    const res = await fetch(`${getBackendBaseUrl()}/api/auth/refresh`, {
      method: "POST",
      headers: { "content-type": "application/json", "x-trn-request-id": requestId },
      body: JSON.stringify({ refreshToken }),
      cache: "no-store",
      signal: AbortSignal.timeout(PROXY_REFRESH_TIMEOUT_MS)
    });

    // 401 -> the refresh token is definitively dead; leave it for a route handler to
    // clear (clearing here, on every request, would mass-log-out on a spurious 401).
    // Anything else (5xx / gateway) is transient — also leave untouched.
    if (!res.ok) return null;

    const parsed = parseAuthTokenResponse(await res.json());
    if (!parsed) return null;

    return buildAuthCookieSpecs(parsed, { secure: process.env.NODE_ENV === "production" });
  } catch {
    return null; // network / timeout / parse — fail safe
  }
}

export async function proxy(req: NextRequest) {
  const requestId = req.headers.get("x-trn-request-id") ?? crypto.randomUUID();
  const verbose = isVerbose();

  const refreshed = await refreshSessionCookies(req, requestId);

  // Forward refreshed cookies to THIS request so server components read the fresh access
  // token (and therefore don't refresh again during render, which would present the now
  // already-rotated refresh token). NOTE: this "refresh + persist before render" guarantee
  // holds within a single request only. Concurrent navigations carrying the same stale
  // token can still each refresh; single-use rotation lets one win and 401s the rest, and
  // because we never clear on 401 the losers just pass through and self-heal next request.
  if (refreshed) {
    for (const spec of refreshed) req.cookies.set(spec.name, spec.value);
  }

  const requestHeaders = new Headers(req.headers);
  requestHeaders.set("x-trn-request-id", requestId);

  const res = NextResponse.next({ request: { headers: requestHeaders } });
  res.headers.set("x-trn-request-id", requestId);

  // Persist refreshed cookies to the browser.
  if (refreshed) {
    for (const spec of refreshed) {
      res.cookies.set({
        name: spec.name,
        value: spec.value,
        httpOnly: spec.httpOnly,
        sameSite: spec.sameSite,
        secure: spec.secure,
        path: spec.path,
        ...(spec.expires ? { expires: spec.expires } : {}),
        ...(spec.maxAge != null ? { maxAge: spec.maxAge } : {})
      });
    }
  }

  if (verbose && isSummonerPath(req.nextUrl.pathname)) {
    console.log(
      JSON.stringify({
        level: "info",
        msg: "proxy request",
        requestId,
        pathname: req.nextUrl.pathname,
        search: req.nextUrl.search,
        refreshed: Boolean(refreshed),
        headers: {
          host: pickHeader(req, "host"),
          "x-forwarded-host": pickHeader(req, "x-forwarded-host"),
          "x-forwarded-proto": pickHeader(req, "x-forwarded-proto"),
          "x-forwarded-for": pickHeader(req, "x-forwarded-for"),
          "x-real-ip": pickHeader(req, "x-real-ip"),
          "user-agent": pickHeader(req, "user-agent")
        }
      })
    );
  }

  return res;
}
