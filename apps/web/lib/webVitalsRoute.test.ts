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
    expect(webVitalsRouteTemplate("/lol/builds")).toBe("/lol/builds");
    expect(webVitalsRouteTemplate("/account/saved-builds")).toBe("/account/saved-builds");
    expect(webVitalsRouteTemplate("/admin/analytics/build-lab")).toBe(
      "/admin/analytics/build-lab"
    );
  });

  it("matches shared build links before the championId pattern", () => {
    expect(webVitalsRouteTemplate("/lol/builds/shared/8f3c1d2e")).toBe(
      "/lol/builds/shared/[shareId]"
    );
    expect(webVitalsRouteTemplate("/lol/builds/103")).toBe("/lol/builds/[championId]");
    // "shared" alone is not a share link; it stays on the championId bucket rather than leaking an id.
    expect(webVitalsRouteTemplate("/lol/builds/shared")).toBe("/lol/builds/[championId]");
  });

  it("collapses unknown and 404 paths to a bounded fallback", () => {
    expect(webVitalsRouteTemplate("/arbitrary/path")).toBe("/_other");
  });

  it("accepts only the bounded templates emitted by the client", () => {
    expect(isWebVitalsRouteTemplate("/lol/champions/[championId]")).toBe(true);
    expect(isWebVitalsRouteTemplate("/lol/builds/shared/[shareId]")).toBe(true);
    expect(isWebVitalsRouteTemplate("/lol/builds/[championId]")).toBe(true);
    expect(isWebVitalsRouteTemplate("/_other")).toBe(true);
    expect(isWebVitalsRouteTemplate("/arbitrary/path")).toBe(false);
    expect(isWebVitalsRouteTemplate("/lol/champions/[championId]/unexpected")).toBe(false);
  });
});
