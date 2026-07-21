import type { MetadataRoute } from "next";

import { getPublicSiteOrigin } from "@/lib/env";

export default function robots(): MetadataRoute.Robots {
  const origin = getPublicSiteOrigin();
  return {
    rules: {
      userAgent: "*",
      allow: "/",
      disallow: ["/admin/", "/api/", "/favorites", "/login"]
    },
    sitemap: `${origin}/sitemap.xml`,
    host: origin
  };
}
