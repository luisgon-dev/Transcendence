import type { MetadataRoute } from "next";

import { analyticsFeatureFlags } from "@/lib/analyticsFeatureFlags";
import { getPublicSiteOrigin } from "@/lib/env";

// AI training and answer crawlers, kept off the whole site. They made about half of all requests
// (Amazonbot alone ~27K in three days, then ClaudeBot and Meta's agent), and nearly all of it was
// summoner profiles, each one a cold computation over the player's match history -- about 30% of the
// database's disk wait on prod's HDD, growing with every summoner ingestion links to. Search engines
// (Googlebot, Bingbot, Applebot) and link previews (facebookexternalhit) are unaffected;
// Google-Extended and Applebot-Extended are those vendors' AI-training opt-outs, not their search.
// Claude-SearchBot and Amzn-SearchBot are the AI-answer search indexers: after the list above went
// in, they made about 99% of summoner-profile requests (99,053 hits on 79,969 distinct summoners in
// two weeks to 2026-10-06), so the profile cache almost never hit.
export const blockedAiCrawlers = [
  "Amazonbot",
  "Amzn-SearchBot",
  "ClaudeBot",
  "Claude-SearchBot",
  "Claude-Web",
  "anthropic-ai",
  "GPTBot",
  "meta-externalagent",
  "CCBot",
  "Bytespider",
  "PerplexityBot",
  "cohere-ai",
  "Diffbot",
  "Google-Extended",
  "Applebot-Extended",
];

export default async function robots(): Promise<MetadataRoute.Robots> {
  const origin = getPublicSiteOrigin();
  // The Build Lab routes call notFound() when the rollout flag is off, but `cacheComponents`
  // commits the prerendered shell before that runs, so a disabled deployment answers 200 with
  // not-found content. Keep crawlers off the soft-404 until the flag is on rather than letting
  // them index it.
  const { buildLab } = await analyticsFeatureFlags();
  return {
    rules: [
      {
        userAgent: "*",
        allow: "/",
        // `/account/` covers sign-in, registration, password reset, and favorites.
        disallow: [
          "/admin/",
          "/api/",
          "/account/",
          ...(buildLab ? [] : ["/lol/builds"]),
        ],
      },
      { userAgent: blockedAiCrawlers, disallow: "/" },
    ],
    sitemap: `${origin}/sitemap.xml`,
    host: origin,
  };
}
