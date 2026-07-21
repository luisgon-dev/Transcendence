import type { MetadataRoute } from "next";

import { getPublicSiteOrigin } from "@/lib/env";
import { fetchChampionMap } from "@/lib/staticData";

export const revalidate = 86400;

const STATIC_ROUTES: Array<{
  path: string;
  changeFrequency: MetadataRoute.Sitemap[number]["changeFrequency"];
  priority: number;
}> = [
  { path: "/", changeFrequency: "daily", priority: 1 },
  { path: "/lol", changeFrequency: "daily", priority: 0.9 },
  { path: "/lol/tierlist", changeFrequency: "hourly", priority: 1 },
  { path: "/lol/champions", changeFrequency: "daily", priority: 0.9 },
  { path: "/lol/multi-search", changeFrequency: "weekly", priority: 0.8 },
  { path: "/lol/pro-builds", changeFrequency: "daily", priority: 0.8 }
];

export default async function sitemap(): Promise<MetadataRoute.Sitemap> {
  const origin = getPublicSiteOrigin();
  const now = new Date();
  const entries: MetadataRoute.Sitemap = STATIC_ROUTES.map((route) => ({
    url: `${origin}${route.path}`,
    lastModified: now,
    changeFrequency: route.changeFrequency,
    priority: route.priority
  }));

  try {
    const { champions } = await fetchChampionMap();
    entries.push(
      ...Object.keys(champions).map((championId) => ({
        url: `${origin}/lol/champions/${championId}`,
        lastModified: now,
        changeFrequency: "daily" as const,
        priority: 0.8
      }))
    );
  } catch {
    // Static routes remain crawlable when Data Dragon is temporarily unavailable.
  }

  return entries;
}
