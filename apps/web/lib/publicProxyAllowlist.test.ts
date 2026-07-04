import { describe, expect, it } from "vitest";

import { isAllowedPublicProxyPath } from "@/lib/publicProxyAllowlist";

describe("isAllowedPublicProxyPath", () => {
  describe("allows the real public summoner surface", () => {
    it.each([
      ["lol", ["lol", "summoners", "na", "Faker", "NA1"]],
      ["lol by id", ["lol", "summoners", "abc-123"]],
      ["lol search", ["lol", "summoners", "search"]],
      ["lol matches recent", ["lol", "summoners", "abc-123", "matches", "recent"]],
      ["lol match detail", ["lol", "summoners", "abc-123", "matches", "MATCH_1"]],
      ["lol match timeline", ["lol", "summoners", "abc-123", "matches", "MATCH_1", "timeline"]],
      ["lol rank history", ["lol", "summoners", "abc-123", "stats", "rank-history"]]
    ])("GET %s", (_label, path) => {
      expect(isAllowedPublicProxyPath("GET", path)).toBe(true);
    });

  });

  describe("blocks everything outside the public surface", () => {
    it("rejects non-summoner namespaces", () => {
      expect(isAllowedPublicProxyPath("GET", ["lol", "analytics", "tier-list"])).toBe(false);
      expect(isAllowedPublicProxyPath("GET", ["lol", "champions", "Aatrox"])).toBe(false);
    });

    it("rejects non-public top-level namespaces", () => {
      expect(isAllowedPublicProxyPath("GET", ["admin", "jobs"])).toBe(false);
      expect(isAllowedPublicProxyPath("GET", ["users", "me", "favorites"])).toBe(false);
      expect(isAllowedPublicProxyPath("GET", ["auth", "login"])).toBe(false);
      expect(isAllowedPublicProxyPath("POST", ["auth", "login"])).toBe(false);
    });

    it("rejects POST requests on the anonymous summoner surface", () => {
      expect(isAllowedPublicProxyPath("POST", ["lol", "summoners", "na", "Faker", "NA1"])).toBe(false);
      expect(isAllowedPublicProxyPath("POST", ["lol", "summoners", "na", "Faker", "NA1", "refresh"])).toBe(false);
    });

    it("rejects PUT and DELETE on the public surface", () => {
      expect(isAllowedPublicProxyPath("PUT", ["lol", "summoners", "na", "Faker", "NA1"])).toBe(false);
      expect(isAllowedPublicProxyPath("DELETE", ["lol", "summoners", "abc-123"])).toBe(false);
    });

    it("rejects too-short paths", () => {
      expect(isAllowedPublicProxyPath("GET", [])).toBe(false);
      expect(isAllowedPublicProxyPath("GET", ["lol"])).toBe(false);
    });
  });
});
