import { describe, expect, it } from "vitest";

import {
  ACCESS_EXPIRES_AT_COOKIE,
  ACCESS_TOKEN_COOKIE,
  REFRESH_TOKEN_COOKIE
} from "@/lib/authCookieShared";
import {
  buildAuthCookieSpecs,
  parseAuthTokenResponse,
  shouldAttemptProxyRefresh
} from "@/lib/proxyAuth";

const fresh = new Date(Date.now() + 10 * 60_000).toISOString();
const stale = new Date(Date.now() + 30_000).toISOString(); // within the 60s refresh window

describe("shouldAttemptProxyRefresh", () => {
  it("does nothing for an anonymous visitor (no refresh token)", () => {
    expect(
      shouldAttemptProxyRefresh(
        { accessToken: null, accessExpiresAtUtc: null, refreshToken: null },
        false
      )
    ).toBe(false);
  });

  it("never refreshes on a prefetch (would burn the single-use refresh token)", () => {
    expect(
      shouldAttemptProxyRefresh(
        { accessToken: "a", accessExpiresAtUtc: stale, refreshToken: "r" },
        true
      )
    ).toBe(false);
  });

  it("refreshes when the access token is stale and a refresh token is present", () => {
    expect(
      shouldAttemptProxyRefresh(
        { accessToken: "a", accessExpiresAtUtc: stale, refreshToken: "r" },
        false
      )
    ).toBe(true);
  });

  it("refreshes when the access token is missing but a refresh token is present", () => {
    expect(
      shouldAttemptProxyRefresh(
        { accessToken: null, accessExpiresAtUtc: null, refreshToken: "r" },
        false
      )
    ).toBe(true);
  });

  it("does nothing when the access token is still fresh", () => {
    expect(
      shouldAttemptProxyRefresh(
        { accessToken: "a", accessExpiresAtUtc: fresh, refreshToken: "r" },
        false
      )
    ).toBe(false);
  });
});

describe("parseAuthTokenResponse", () => {
  it("accepts a well-formed token response", () => {
    expect(
      parseAuthTokenResponse({
        accessToken: "at",
        refreshToken: "rt",
        accessTokenExpiresAtUtc: fresh
      })
    ).toEqual({ accessToken: "at", refreshToken: "rt", accessTokenExpiresAtUtc: fresh });
  });

  it("rejects malformed / partial bodies so cookies are never corrupted", () => {
    expect(parseAuthTokenResponse(null)).toBeNull();
    expect(parseAuthTokenResponse("nope")).toBeNull();
    expect(parseAuthTokenResponse({})).toBeNull();
    expect(parseAuthTokenResponse({ accessToken: "at", refreshToken: "rt" })).toBeNull();
    expect(
      parseAuthTokenResponse({ accessToken: "", refreshToken: "rt", accessTokenExpiresAtUtc: fresh })
    ).toBeNull();
  });
});

describe("buildAuthCookieSpecs", () => {
  it("builds the three auth cookies with the right attributes", () => {
    const specs = buildAuthCookieSpecs(
      { accessToken: "at", refreshToken: "rt", accessTokenExpiresAtUtc: fresh },
      { secure: true }
    );
    const byName = Object.fromEntries(specs.map((s) => [s.name, s]));

    expect(specs).toHaveLength(3);
    for (const s of specs) {
      expect(s).toMatchObject({ httpOnly: true, sameSite: "lax", secure: true, path: "/" });
    }
    expect(byName[ACCESS_TOKEN_COOKIE].value).toBe("at");
    expect(byName[ACCESS_TOKEN_COOKIE].expires).toBeInstanceOf(Date);
    expect(byName[ACCESS_EXPIRES_AT_COOKIE].value).toBe(fresh);
    expect(byName[REFRESH_TOKEN_COOKIE].value).toBe("rt");
    expect(byName[REFRESH_TOKEN_COOKIE].maxAge).toBe(60 * 60 * 24 * 7);
    expect(byName[REFRESH_TOKEN_COOKIE].expires).toBeUndefined();
  });

  it("omits expires when the token expiry is unparseable", () => {
    const specs = buildAuthCookieSpecs(
      { accessToken: "at", refreshToken: "rt", accessTokenExpiresAtUtc: "not-a-date" },
      { secure: false }
    );
    const access = specs.find((s) => s.name === ACCESS_TOKEN_COOKIE);
    expect(access?.expires).toBeUndefined();
    expect(access?.secure).toBe(false);
  });
});
