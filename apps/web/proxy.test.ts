import { NextRequest } from "next/server";
import { afterEach, describe, expect, it, vi } from "vitest";

import {
  ACCESS_EXPIRES_AT_COOKIE,
  ACCESS_TOKEN_COOKIE,
  REFRESH_TOKEN_COOKIE
} from "@/lib/authCookieShared";
import { proxy } from "@/proxy";

const STALE = "2020-01-01T00:00:00Z";

function makeReq(
  cookies: Record<string, string>,
  headers: Record<string, string> = {}
): NextRequest {
  const cookieHeader = Object.entries(cookies)
    .map(([k, v]) => `${k}=${v}`)
    .join("; ");
  return new NextRequest("http://localhost:3000/lol/tierlist", {
    headers: cookieHeader ? { cookie: cookieHeader, ...headers } : headers
  });
}

function okTokenResponse() {
  const future = new Date(Date.now() + 15 * 60_000).toISOString();
  return new Response(
    JSON.stringify({
      accessToken: "NEW_ACCESS",
      refreshToken: "NEW_REFRESH",
      accessTokenExpiresAtUtc: future
    }),
    { status: 200, headers: { "content-type": "application/json" } }
  );
}

afterEach(() => {
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
});

describe("proxy token refresh", () => {
  it("refreshes a stale session and writes new cookies to BOTH request (SSR) and response (browser)", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => okTokenResponse()));

    const req = makeReq({
      [REFRESH_TOKEN_COOKIE]: "R1",
      [ACCESS_TOKEN_COOKIE]: "OLD",
      [ACCESS_EXPIRES_AT_COOKIE]: STALE
    });
    const res = await proxy(req);

    expect(fetch).toHaveBeenCalledOnce();
    // Browser persistence (Set-Cookie on the response):
    expect(res.cookies.get(ACCESS_TOKEN_COOKIE)?.value).toBe("NEW_ACCESS");
    expect(res.cookies.get(REFRESH_TOKEN_COOKIE)?.value).toBe("NEW_REFRESH");
    // Forwarded to the current render (request cookies mutated) so SSR doesn't re-refresh:
    expect(req.cookies.get(ACCESS_TOKEN_COOKIE)?.value).toBe("NEW_ACCESS");
    expect(req.cookies.get(REFRESH_TOKEN_COOKIE)?.value).toBe("NEW_REFRESH");
  });

  it("passes through unchanged on a 401 — never clears cookies", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => new Response("no", { status: 401 })));

    const req = makeReq({ [REFRESH_TOKEN_COOKIE]: "DEAD", [ACCESS_EXPIRES_AT_COOKIE]: STALE });
    const res = await proxy(req);

    expect(fetch).toHaveBeenCalledOnce();
    expect(res.cookies.get(ACCESS_TOKEN_COOKIE)).toBeUndefined();
    expect(res.cookies.get(REFRESH_TOKEN_COOKIE)).toBeUndefined();
    expect(req.cookies.get(REFRESH_TOKEN_COOKIE)?.value).toBe("DEAD"); // untouched
  });

  it("passes through unchanged when the backend errors (fail-safe)", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn(async () => {
        throw new Error("network down");
      })
    );

    const req = makeReq({ [REFRESH_TOKEN_COOKIE]: "R1", [ACCESS_EXPIRES_AT_COOKIE]: STALE });
    const res = await proxy(req);

    expect(res.cookies.get(ACCESS_TOKEN_COOKIE)).toBeUndefined();
  });

  it("does not refresh on a prefetch request", async () => {
    const fetchMock = vi.fn(async () => okTokenResponse());
    vi.stubGlobal("fetch", fetchMock);

    const req = makeReq(
      { [REFRESH_TOKEN_COOKIE]: "R1", [ACCESS_EXPIRES_AT_COOKIE]: STALE },
      { "next-router-prefetch": "1" }
    );
    await proxy(req);

    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("does not refresh an anonymous request (no refresh token)", async () => {
    const fetchMock = vi.fn(async () => okTokenResponse());
    vi.stubGlobal("fetch", fetchMock);

    await proxy(makeReq({}));

    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("under a concurrent same-token race, the 401 loser never clears cookies (no logout)", async () => {
    // Two near-simultaneous navigations present the same stale refresh token. Single-use
    // rotation lets one win (200) and 401s the other. The loser must pass through without
    // clearing — the invariant the cross-request concurrency safety rests on.
    vi.stubGlobal(
      "fetch",
      vi
        .fn()
        .mockResolvedValueOnce(okTokenResponse()) // winner rotates R1 -> R2
        .mockResolvedValueOnce(new Response("revoked", { status: 401 })) // loser: R1 already dead
    );

    const winnerReq = makeReq({ [REFRESH_TOKEN_COOKIE]: "R1", [ACCESS_EXPIRES_AT_COOKIE]: STALE });
    const loserReq = makeReq({ [REFRESH_TOKEN_COOKIE]: "R1", [ACCESS_EXPIRES_AT_COOKIE]: STALE });
    const winnerRes = await proxy(winnerReq);
    const loserRes = await proxy(loserReq);

    expect(winnerRes.cookies.get(REFRESH_TOKEN_COOKIE)?.value).toBe("NEW_REFRESH");
    // Loser: no rotation persisted, and crucially no clear — cookies untouched.
    expect(loserRes.cookies.get(ACCESS_TOKEN_COOKIE)).toBeUndefined();
    expect(loserRes.cookies.get(REFRESH_TOKEN_COOKIE)).toBeUndefined();
    expect(loserReq.cookies.get(REFRESH_TOKEN_COOKIE)?.value).toBe("R1");
  });

  it("always forwards an x-trn-request-id header to the response", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => okTokenResponse()));
    const res = await proxy(makeReq({}));
    expect(res.headers.get("x-trn-request-id")).toBeTruthy();
  });
});
