import { describe, expect, it, vi } from "vitest";

import { isSafeMethod, isSameOriginRequest } from "@/lib/bffOrigin";

function request(
  headers: Record<string, string> = {},
  { method = "POST", protocol = "https:" }: { method?: string; protocol?: string } = {}
) {
  return {
    method,
    headers: new Headers(headers),
    nextUrl: { protocol }
  };
}

describe("BFF origin policy", () => {
  it("classifies read-only methods as safe", () => {
    expect(isSafeMethod("GET")).toBe(true);
    expect(isSafeMethod("HEAD")).toBe(true);
    expect(isSafeMethod("OPTIONS")).toBe(true);
    expect(isSafeMethod("POST")).toBe(false);
    expect(isSafeMethod("PUT")).toBe(false);
    expect(isSafeMethod("DELETE")).toBe(false);
  });

  it("allows requests without an Origin header", () => {
    expect(isSameOriginRequest(request({ host: "transcend.kronic.one" }))).toBe(true);
  });

  it("uses the configured canonical origin instead of forwarded headers", () => {
    vi.stubEnv("TRN_PUBLIC_ORIGIN", "https://transcend.kronic.one");

    expect(
      isSameOriginRequest(
        request({
          origin: "https://transcend.kronic.one",
          "x-forwarded-host": "attacker.example"
        })
      )
    ).toBe(true);
    expect(
      isSameOriginRequest(
        request({
          origin: "https://attacker.example",
          "x-forwarded-host": "attacker.example"
        })
      )
    ).toBe(false);
  });

  it("uses host and protocol as a development fallback", () => {
    vi.stubEnv("TRN_PUBLIC_ORIGIN", "");

    expect(
      isSameOriginRequest(
        request({ origin: "http://localhost:3000", host: "localhost:3000" }, { protocol: "http:" })
      )
    ).toBe(true);
    expect(
      isSameOriginRequest(
        request({ origin: "https://attacker.example", host: "localhost:3000" })
      )
    ).toBe(false);
  });

  it("rejects malformed origins and fallbacks without a host", () => {
    vi.stubEnv("TRN_PUBLIC_ORIGIN", "");

    expect(isSameOriginRequest(request({ origin: "not a URL" }))).toBe(false);
    expect(isSameOriginRequest(request({ origin: "https://transcend.kronic.one" }))).toBe(false);
  });
});
