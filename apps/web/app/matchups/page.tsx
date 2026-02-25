import type { components } from "@transcendence/api-client/schema";

import { Card } from "@/components/ui/Card";
import { Badge } from "@/components/ui/Badge";
import { MatchupsExplorerClient } from "@/components/MatchupsExplorerClient";
import { fetchBackendJson } from "@/lib/backendCall";
import { getBackendBaseUrl } from "@/lib/env";
import { fetchChampionMap } from "@/lib/staticData";
import { normalizeTierListEntries } from "@/lib/tierlist";

type TierListResponse = components["schemas"]["TierListResponse"];

export default async function MatchupsIndexPage() {
  const [{ version, champions }, tierListRes] = await Promise.all([
    fetchChampionMap(),
    fetchBackendJson<TierListResponse>(`${getBackendBaseUrl()}/api/analytics/tierlist`, {
      next: { revalidate: 60 * 60 }
    })
  ]);

  const tierEntries = tierListRes.ok
    ? normalizeTierListEntries(tierListRes.body?.entries ?? [])
    : [];

  const popular = tierEntries
    .slice()
    .sort((a, b) => b.games - a.games)
    .slice(0, 120)
    .map((entry) => ({
      championId: entry.championId,
      role: entry.role,
      games: entry.games,
      winRate: entry.winRate
    }));

  return (
    <div className="grid gap-6">
      <header className="grid gap-3">
        <div className="flex flex-wrap items-center gap-2">
          <Badge className="border-primary/40 bg-primary/10 text-primary">
            Matchup Tool
          </Badge>
          <Badge>{popular.length} role pages</Badge>
        </div>
        <h1 className="font-[var(--font-sora)] text-3xl font-semibold tracking-tight">
          Matchup Analysis
        </h1>
        <p className="text-sm text-fg/75">
          Search champions, filter by role, and jump directly to detailed counter pages.
        </p>
      </header>

      <Card className="p-4 md:p-5">
        <MatchupsExplorerClient entries={popular} champions={champions} version={version} />
      </Card>
    </div>
  );
}
