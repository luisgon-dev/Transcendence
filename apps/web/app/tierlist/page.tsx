import Image from "next/image";
import Link from "next/link";
import type { components } from "@transcendence/api-client/schema";

import { BackendErrorCard } from "@/components/BackendErrorCard";
import { FilterBar } from "@/components/FilterBar";
import { TierBadge } from "@/components/TierBadge";
import { WinRateText } from "@/components/WinRateText";
import { Badge } from "@/components/ui/Badge";
import { Card } from "@/components/ui/Card";
import { fetchBackendJson } from "@/lib/backendCall";
import { getBackendBaseUrl, getErrorVerbosity } from "@/lib/env";
import { formatGames, formatPercent } from "@/lib/format";
import { roleDisplayLabel } from "@/lib/roles";
import { championIconUrl, fetchChampionMap } from "@/lib/staticData";
import {
  movementClass,
  movementIcon,
  normalizeTierListEntries,
  tierBgClass,
  tierColorClass,
  TIER_ORDER,
  type UITierGrade,
  type UITierListEntry
} from "@/lib/tierlist";

type TierListResponse = components["schemas"]["TierListResponse"];

export default async function TierListPage({
  searchParams
}: {
  searchParams?: Promise<{ role?: string; rankTier?: string }>;
}) {
  const resolvedSearchParams = searchParams ? await searchParams : undefined;
  const qs = new URLSearchParams();
  const roleParam = (resolvedSearchParams?.role ?? "").toUpperCase();
  const rankParam = (resolvedSearchParams?.rankTier ?? "").toUpperCase();

  if (roleParam && roleParam !== "ALL") qs.set("role", roleParam);
  if (rankParam && rankParam !== "ALL") qs.set("rankTier", rankParam);

  const verbosity = getErrorVerbosity();
  const res = await fetchBackendJson<TierListResponse>(
    `${getBackendBaseUrl()}/api/analytics/tierlist?${qs.toString()}`,
    { next: { revalidate: 60 * 60 } }
  );

  if (!res.ok) {
    return (
      <BackendErrorCard
        title="Tier List"
        message={
          res.errorKind === "timeout"
            ? "Timed out reaching the backend."
            : res.errorKind === "unreachable"
              ? "We are having trouble reaching the backend."
              : "Failed to load tier list from the backend."
        }
        requestId={res.requestId}
        detail={
          verbosity === "verbose"
            ? JSON.stringify({ status: res.status, errorKind: res.errorKind }, null, 2)
            : null
        }
      />
    );
  }

  const tierlist = res.body!;
  const { version, champions } = await fetchChampionMap();
  const normalizedEntries = normalizeTierListEntries(tierlist.entries);

  const groups: Record<UITierGrade, UITierListEntry[]> = {
    S: [],
    A: [],
    B: [],
    C: [],
    D: []
  };

  for (const e of normalizedEntries) {
    groups[e.tier].push(e);
  }

  const rankTierValue =
    typeof tierlist.rankTier === "string" && tierlist.rankTier.toLowerCase() !== "all"
      ? tierlist.rankTier
      : null;

  // Precompute rank offset for each tier so we can display global rank
  const tierRankOffset: Record<UITierGrade, number> = { S: 0, A: 0, B: 0, C: 0, D: 0 };
  let offset = 0;
  for (const tier of TIER_ORDER) {
    tierRankOffset[tier] = offset;
    offset += groups[tier].length;
  }

  return (
    <div className="grid gap-6">
      <header className="flex flex-col gap-3">
        <h1 className="font-[var(--font-sora)] text-3xl font-semibold tracking-tight">
          Tier List
        </h1>

        <div className="flex flex-wrap items-center gap-2">
          <Badge className="border-primary/40 bg-primary/10 text-primary">
            Patch {tierlist.patch ?? "Unknown"}
          </Badge>
          <Badge>{roleDisplayLabel(tierlist.role ?? "ALL")}</Badge>
          <Badge>{rankTierValue ?? "All Ranks"}</Badge>
          <Badge>{normalizedEntries.length} champions</Badge>
        </div>

        <FilterBar
          activeRole={roleParam || "ALL"}
          activeRank={rankParam?.toLowerCase() || "all"}
          baseHref="/tierlist"
          patch={tierlist.patch}
        />
      </header>

      <Card className="overflow-hidden p-0">
        {TIER_ORDER.map((tier) => {
          const entries = groups[tier];
          if (entries.length === 0) return null;

          return (
            <div key={tier}>
              {/* Tier header row */}
              <div
                className={`flex items-center gap-3 border-b border-border/40 px-4 py-2.5 ${tierBgClass(tier)}`}
              >
                <TierBadge tier={tier} size="md" />
                <span className={`text-sm font-semibold ${tierColorClass(tier)}`}>
                  Tier {tier}
                </span>
                <span className="text-xs text-muted">
                  {entries.length} champion{entries.length !== 1 ? "s" : ""}
                </span>
              </div>

              {/* Champion rows */}
              <div className="overflow-x-auto">
                <table className="w-full min-w-[640px] text-left text-sm">
                  <thead className="text-[11px] uppercase tracking-wider text-muted">
                    <tr className="border-b border-border/30">
                      <th className="w-10 px-4 py-2 text-center">#</th>
                      <th className="w-10 px-2 py-2">Tier</th>
                      <th className="px-3 py-2">Champion</th>
                      <th className="px-3 py-2">Role</th>
                      <th className="px-3 py-2 text-right">Win Rate</th>
                      <th className="px-3 py-2 text-right">Pick Rate</th>
                      <th className="px-3 py-2 text-right">Games</th>
                      <th className="w-16 px-3 py-2 text-center">Trend</th>
                    </tr>
                  </thead>
                  <tbody>
                    {entries.map((e, idx) => {
                      const rank = tierRankOffset[tier] + idx + 1;
                      const champ = champions[String(e.championId)];
                      const champName = champ?.name ?? `Champion ${e.championId}`;
                      const champSlug = champ?.id ?? "Unknown";

                      return (
                        <tr
                          key={`${tier}-${e.role}-${e.championId}`}
                          className="border-b border-border/20 transition hover:bg-white/[0.03]"
                        >
                          <td className="px-4 py-2.5 text-center text-xs text-muted">
                            {rank}
                          </td>
                          <td className="px-2 py-2.5">
                            <TierBadge tier={e.tier} />
                          </td>
                          <td className="px-3 py-2.5">
                            <Link
                              href={`/champions/${e.championId}?role=${encodeURIComponent(e.role)}${rankTierValue ? `&rankTier=${encodeURIComponent(rankTierValue)}` : ""}`}
                              className="flex items-center gap-2.5 hover:underline"
                            >
                              <Image
                                src={championIconUrl(version, champSlug)}
                                alt={champName}
                                width={28}
                                height={28}
                                className="rounded-md"
                              />
                              <span className="truncate font-medium text-fg">
                                {champName}
                              </span>
                            </Link>
                          </td>
                          <td className="px-3 py-2.5 text-xs text-muted">
                            {roleDisplayLabel(e.role)}
                          </td>
                          <td className="px-3 py-2.5 text-right">
                            <WinRateText value={e.winRate} decimals={2} />
                          </td>
                          <td className="px-3 py-2.5 text-right text-fg/70">
                            {formatPercent(e.pickRate, { decimals: 1 })}
                          </td>
                          <td className="px-3 py-2.5 text-right text-fg/70">
                            {formatGames(e.games)}
                          </td>
                          <td className="px-3 py-2.5 text-center">
                            <span
                              className={`text-sm font-medium ${movementClass(e.movement)}`}
                              title={
                                e.previousTier
                                  ? `Previous: ${e.previousTier}`
                                  : undefined
                              }
                            >
                              {movementIcon(e.movement)}
                            </span>
                          </td>
                        </tr>
                      );
                    })}
                  </tbody>
                </table>
              </div>
            </div>
          );
        })}
      </Card>
    </div>
  );
}
