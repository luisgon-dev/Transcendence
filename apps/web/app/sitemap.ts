import type { MetadataRoute } from "next";

import { fetchBackendJson } from "@/lib/backendCall";
import { getBackendBaseUrl, getPublicSiteOrigin } from "@/lib/env";
import type { LeaderboardResponse } from "@/lib/leaderboards";
import { platformRegionToSlug } from "@/lib/lolRegions";
import { encodeRiotIdPath } from "@/lib/riotid";
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
  { path: "/lol/leaderboards", changeFrequency: "hourly", priority: 0.9 },
  { path: "/lol/multi-search", changeFrequency: "weekly", priority: 0.8 },
  { path: "/lol/live", changeFrequency: "weekly", priority: 0.8 },
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

  const regionalBoards = await Promise.all(
    ["na", "euw", "eune", "kr"].map((region) =>
      fetchBackendJson<LeaderboardResponse>(
        `${getBackendBaseUrl()}/api/lol/leaderboards?region=${region}&queue=solo&limit=50`,
        { next: { revalidate: 60 * 60 } }
      )
    )
  );
  const seenProfiles = new Set<string>();
  for (const board of regionalBoards) {
    if (!board.ok || !board.body) continue;
    const region = platformRegionToSlug(board.body.region);
    for (const player of board.body.entries) {
      const path = `/lol/summoners/${region}/${encodeRiotIdPath(player)}`;
      if (seenProfiles.has(path)) continue;
      seenProfiles.add(path);
      entries.push({
        url: `${origin}${path}`,
        lastModified: player.updatedAtUtc ? new Date(player.updatedAtUtc) : now,
        changeFrequency: "daily",
        priority: 0.7
      });
    }
  }

  return entries;
}
