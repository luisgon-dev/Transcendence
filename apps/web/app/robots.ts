import type { MetadataRoute } from "next";

import { getPublicSiteOrigin } from "@/lib/env";

export default function robots(): MetadataRoute.Robots {
  const origin = getPublicSiteOrigin();
  return {
    rules: {
      userAgent: "*",
      allow: "/",
      // Shared-build links are capability URLs: revoking one cannot un-index it, so they must
      // never be crawled in the first place.
      disallow: ["/admin/", "/api/", "/favorites", "/login", "/lol/builds/shared/"]
    },
    sitemap: `${origin}/sitemap.xml`,
    host: origin
  };
}
