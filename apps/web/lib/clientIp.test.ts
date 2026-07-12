import { describe, expect, it } from "vitest";

import { resolveClientIp } from "@/lib/clientIp";

const h = (init: Record<string, string>) => new Headers(init);

describe("resolveClientIp", () => {
  it("prefers cf-connecting-ip above everything", () => {
    expect(
      resolveClientIp(
        h({
          "cf-connecting-ip": "9.9.9.9",
          "x-real-ip": "8.8.8.8",
          "x-forwarded-for": "1.1.1.1"
        })
      )
    ).toBe("9.9.9.9");
  });

  it("falls back to x-real-ip (nginx $remote_addr)", () => {
    expect(resolveClientIp(h({ "x-real-ip": "8.8.8.8", "x-forwarded-for": "1.1.1.1, 2.2.2.2" }))).toBe(
      "8.8.8.8"
    );
  });

  it("uses the rightmost x-forwarded-for entry (the edge-appended hop)", () => {
    expect(resolveClientIp(h({ "x-forwarded-for": "5.6.7.8" }))).toBe("5.6.7.8");
  });

  it("ignores a client-forged leftmost x-forwarded-for entry", () => {
    // A client sends X-Forwarded-For: 1.2.3.4; nginx appends the real peer 5.6.7.8.
    expect(resolveClientIp(h({ "x-forwarded-for": "1.2.3.4, 5.6.7.8" }))).toBe("5.6.7.8");
  });

  it("trims whitespace and skips empty entries", () => {
    expect(resolveClientIp(h({ "x-forwarded-for": " 1.2.3.4 ,  , 5.6.7.8 " }))).toBe("5.6.7.8");
  });

  it("returns null when no trusted source is present", () => {
    expect(resolveClientIp(h({}))).toBeNull();
  });
});
