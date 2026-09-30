import type { MetadataRoute } from "next";

import { analyticsFeatureFlags } from "@/lib/analyticsFeatureFlags";
import { getPublicSiteOrigin } from "@/lib/env";

// AI training and answer crawlers, kept off the whole site. They made about half of all requests
// (Amazonbot alone ~27K in three days, then ClaudeBot and Meta's agent), and nearly all of it was
// summoner profiles, each one a cold computation over the player's match history -- about 30% of the
// database's disk wait on prod's HDD, growing with every summoner ingestion links to. Search engines
// (Googlebot, Bingbot, Applebot) and link previews (facebookexternalhit) are unaffected;
// Google-Extended and Applebot-Extended are those vendors' AI-training opt-outs, not their search.
export const blockedAiCrawlers = [
  "Amazonbot",
  "ClaudeBot",
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
        // Shared-build links are capability URLs: revoking one cannot un-index it, so they must
        // never be crawled in the first place.
        disallow: [
          "/admin/",
          "/api/",
          "/favorites",
          "/login",
          "/lol/builds/shared/",
          ...(buildLab ? [] : ["/lol/builds"]),
        ],
      },
      { userAgent: blockedAiCrawlers, disallow: "/" },
    ],
    sitemap: `${origin}/sitemap.xml`,
    host: origin,
  };
}
