import type { components } from "@transcendence/api-client/schema";

import {
  ChampionsGridClient,
  type ChampionGridEntry
} from "@/components/ChampionsGridClient";
import { fetchBackendJson } from "@/lib/backendCall";
import { getBackendBaseUrl } from "@/lib/env";
import { DEFAULT_TIERLIST_RANK_TIER } from "@/lib/ranks";
import { fetchChampionMap } from "@/lib/staticData";
import {
  normalizeTierListEntries,
  type UITierGrade
} from "@/lib/tierlist";

type TierListResponse = components["schemas"]["TierListResponse"];

export default async function ChampionsPage() {
  const [{ version, champions }, tierListRes] = await Promise.all([
    fetchChampionMap(),
    fetchBackendJson<TierListResponse>(
      `${getBackendBaseUrl()}/api/analytics/tierlist?rankTier=${encodeURIComponent(DEFAULT_TIERLIST_RANK_TIER)}`,
      { next: { revalidate: 60 * 60 } }
    )
  ]);

  // Build a map of championId -> best tier/role/winRate from the tier list
  const tierMap = new Map<
    number,
    { tier: UITierGrade; role: string; winRate: number; games: number }
  >();

  if (tierListRes.ok && tierListRes.body) {
    const entries = normalizeTierListEntries(tierListRes.body.entries);
    for (const entry of entries) {
      const existing = tierMap.get(entry.championId);
      // Keep the entry with the most games (most relevant role)
      if (!existing || entry.games > existing.games) {
        tierMap.set(entry.championId, {
          tier: entry.tier,
          role: entry.role,
          winRate: entry.winRate,
          games: entry.games
        });
      }
    }
  }

  const list: ChampionGridEntry[] = Object.entries(champions)
    .map(([key, value]) => {
      const id = Number(key);
      const tierInfo = tierMap.get(id);
      return {
        championId: id,
        ...value,
        tier: tierInfo?.tier ?? null,
        winRate: tierInfo?.winRate ?? null,
        primaryRole: tierInfo?.role ?? null
      };
    })
    .sort((a, b) => a.name.localeCompare(b.name));

  return (
    <div className="grid gap-6">
      <header className="grid gap-2">
        <h1 className="font-[var(--font-sora)] text-3xl font-semibold tracking-tight">
          Champions
        </h1>
        <p className="text-sm text-fg/75">
          Builds, matchups, and win rates per role.
        </p>
      </header>

      <ChampionsGridClient champions={list} version={version} />
    </div>
  );
}
