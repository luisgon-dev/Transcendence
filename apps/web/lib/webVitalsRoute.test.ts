import { describe, expect, it } from "vitest";

import { webVitalsRouteTemplate } from "@/lib/webVitalsRoute";

describe("webVitalsRouteTemplate", () => {
  it("removes Riot IDs and resource identifiers", () => {
    expect(webVitalsRouteTemplate("/lol/summoners/na/Kronic-NA1")).toBe(
      "/lol/summoners/[region]/[riotId]"
    );
    expect(webVitalsRouteTemplate("/lol/champions/103")).toBe(
      "/lol/champions/[championId]"
    );
  });

  it("keeps known static routes", () => {
    expect(webVitalsRouteTemplate("/lol/tierlist")).toBe("/lol/tierlist");
  });

  it("collapses unknown and 404 paths to a bounded fallback", () => {
    expect(webVitalsRouteTemplate("/arbitrary/path")).toBe("/_other");
  });
});
