import { describe, expect, it } from "vitest";

import { isAllowedAppProxyPath } from "@/lib/appProxyAllowlist";

describe("isAllowedAppProxyPath", () => {
  it("allows the intended app-only reads and multi-search write", () => {
    expect(isAllowedAppProxyPath("GET", ["summoners", "na", "Name", "Tag", "live-game"]))
      .toBe(true);
    expect(isAllowedAppProxyPath("POST", ["lol", "summoners", "multi-search"]))
      .toBe(true);
  });

  it("rejects methods and paths outside the narrow allowlist", () => {
    expect(isAllowedAppProxyPath("GET", ["lol", "summoners", "multi-search"]))
      .toBe(false);
    expect(isAllowedAppProxyPath("POST", ["lol", "summoners", "refresh"]))
      .toBe(false);
    expect(isAllowedAppProxyPath("DELETE", ["lol", "summoners", "multi-search"]))
      .toBe(false);
  });
});
