import { afterEach, describe, expect, it, vi } from "vitest";

import robots, { blockedAiCrawlers } from "./robots";

const connection = vi.hoisted(() => vi.fn(async () => undefined));

vi.mock("next/server", () => ({ connection }));

afterEach(() => {
  connection.mockClear();
  vi.unstubAllEnvs();
});

describe("robots", () => {
  it("keeps crawlers off the Build Lab soft-404 while the flag is off", async () => {
    vi.stubEnv("TRN_FEATURE_BUILD_LAB", "false");

    const rules = generalRule(await robots());

    // The routes call notFound(), but `cacheComponents` commits the prerendered shell first, so a
    // disabled deployment answers 200 with not-found content. That must not get indexed.
    expect(rules.disallow).toContain("/lol/builds");
  });

  it("allows the Build Lab once the flag is on but never the share links", async () => {
    vi.stubEnv("TRN_FEATURE_BUILD_LAB", "true");

    const rules = generalRule(await robots());

    expect(rules.disallow).not.toContain("/lol/builds");
    // A share link is a capability URL: revoking it cannot un-index it.
    expect(rules.disallow).toContain("/lol/builds/shared/");
  });

  it("keeps the pre-existing private surfaces disallowed in both flag states", async () => {
    for (const flag of ["false", "true"]) {
      vi.stubEnv("TRN_FEATURE_BUILD_LAB", flag);

      const rules = generalRule(await robots());

      expect(rules.disallow).toEqual(
        expect.arrayContaining(["/admin/", "/api/", "/favorites", "/login"]),
      );
    }
  });

  it("keeps AI crawlers off the whole site but leaves search engines on the general rule", async () => {
    vi.stubEnv("TRN_FEATURE_BUILD_LAB", "true");

    const result = await robots();
    const rules = result.rules as Array<{
      userAgent: string | string[];
      disallow?: string | string[];
    }>;
    const ai = rules.find((rule) => Array.isArray(rule.userAgent));

    expect(ai?.disallow).toBe("/");
    expect(ai?.userAgent).toEqual(
      expect.arrayContaining([
        "Amazonbot",
        "ClaudeBot",
        "meta-externalagent",
        "GPTBot",
      ]),
    );
    for (const searchEngine of [
      "Googlebot",
      "Bingbot",
      "Applebot",
      "facebookexternalhit",
    ]) {
      expect(blockedAiCrawlers).not.toContain(searchEngine);
    }
    expect(generalRule(result).userAgent).toBe("*");
  });
});

function generalRule(result: Awaited<ReturnType<typeof robots>>) {
  const rules = result.rules as Array<{
    userAgent: string | string[];
    disallow: string[];
  }>;
  const general = rules.find((rule) => rule.userAgent === "*");
  if (!general) throw new Error("robots has no general rule");
  return general;
}
