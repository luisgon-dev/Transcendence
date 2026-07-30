import type { MetadataRoute } from "next";

import { analyticsFeatureFlags } from "@/lib/analyticsFeatureFlags";
import { getPublicSiteOrigin } from "@/lib/env";

export default async function robots(): Promise<MetadataRoute.Robots> {
  const origin = getPublicSiteOrigin();
  // The Build Lab routes call notFound() when the rollout flag is off, but `cacheComponents`
  // commits the prerendered shell before that runs, so a disabled deployment answers 200 with
  // not-found content. Keep crawlers off the soft-404 until the flag is on rather than letting
  // them index it.
  const { buildLab } = await analyticsFeatureFlags();
  return {
    rules: {
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
        ...(buildLab ? [] : ["/lol/builds"])
      ]
    },
    sitemap: `${origin}/sitemap.xml`,
    host: origin
  };
}
