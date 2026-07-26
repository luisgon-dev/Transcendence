import { describe, expect, it } from "vitest";

import { isWebVitalsRouteTemplate, webVitalsRouteTemplate } from "@/lib/webVitalsRoute";

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

  it("accepts only the bounded templates emitted by the client", () => {
    expect(isWebVitalsRouteTemplate("/lol/champions/[championId]")).toBe(true);
    expect(isWebVitalsRouteTemplate("/_other")).toBe(true);
    expect(isWebVitalsRouteTemplate("/arbitrary/path")).toBe(false);
    expect(isWebVitalsRouteTemplate("/lol/champions/[championId]/unexpected")).toBe(false);
  });
});
