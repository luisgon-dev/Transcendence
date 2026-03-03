import { describe, expect, it } from "vitest";

import { normalizeProxyPath } from "@/lib/proxyPath";

describe("normalizeProxyPath", () => {
  it("accepts regular path segments", () => {
    const normalized = normalizeProxyPath(["summoners", "na1", "name", "tag", "live-game"]);
    expect(normalized).toEqual(["summoners", "na1", "name", "tag", "live-game"]);
  });

  it("rejects dot segments", () => {
    expect(normalizeProxyPath(["summoners", "..", "x"])).toBeNull();
    expect(normalizeProxyPath(["summoners", ".", "x"])).toBeNull();
  });

  it("rejects slash-containing segments", () => {
    expect(normalizeProxyPath(["summoners", "na1/test"])).toBeNull();
    expect(normalizeProxyPath(["summoners", "na1\\test"])).toBeNull();
  });
});
