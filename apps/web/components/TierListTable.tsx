"use client";

import { useMemo, useState } from "react";
import Image from "next/image";
import Link from "next/link";

import { TierBadge } from "@/components/TierBadge";
import { WinRateText } from "@/components/WinRateText";
import { Card } from "@/components/ui/Card";
import { formatGames, formatPercent } from "@/lib/format";
import { roleDisplayLabel } from "@/lib/roles";
import { championIconUrl } from "@/lib/staticData";
import {
  movementClass,
  movementIcon,
  tierBgClass,
  tierColorClass,
  TIER_ORDER,
  type UITierGrade,
  type UITierListEntry
} from "@/lib/tierlist";

type SortColumn = "rank" | "winRate" | "pickRate" | "games";
type SortDir = "asc" | "desc";

type ChampionMap = Record<string, { name: string; id: string; title?: string }>;

export function TierListTable({
  entries,
  champions,
  version,
  rankTierValue,
  activeRegion
}: {
  entries: UITierListEntry[];
  champions: ChampionMap;
  version: string;
  rankTierValue: string | null;
  activeRegion: string;
}) {
  const [sortCol, setSortCol] = useState<SortColumn>("rank");
  const [sortDir, setSortDir] = useState<SortDir>("asc");

  const handleSort = (col: SortColumn) => {
    if (col === sortCol) {
      setSortDir((d) => (d === "asc" ? "desc" : "asc"));
    } else {
      setSortCol(col);
      setSortDir(col === "rank" ? "asc" : "desc");
    }
  };

  // Assign ranks based on original order (composite score)
  const entriesWithRank = useMemo(
    () => entries.map((e, i) => ({ ...e, rank: i + 1 })),
    [entries]
  );

  const sortedEntries = useMemo(() => {
    if (sortCol === "rank") {
      const sorted = [...entriesWithRank].sort((a, b) => a.rank - b.rank);
      return sortDir === "desc" ? sorted.reverse() : sorted;
    }
    const sorted = [...entriesWithRank].sort((a, b) => {
      const aVal = a[sortCol];
      const bVal = b[sortCol];
      return aVal - bVal;
    });
    return sortDir === "desc" ? sorted.reverse() : sorted;
  }, [entriesWithRank, sortCol, sortDir]);

  const isDefaultSort = sortCol === "rank" && sortDir === "asc";

  // Group by tier for default view
  const groups = useMemo(() => {
    if (!isDefaultSort) return null;
    const g: Record<UITierGrade, typeof sortedEntries> = {
      S: [], A: [], B: [], C: [], D: []
    };
    for (const e of sortedEntries) g[e.tier].push(e);
    return g;
  }, [sortedEntries, isDefaultSort]);

  const columns: { label: string; sortKey?: SortColumn; className: string; hiddenMobile?: boolean }[] = [
    { label: "#", sortKey: "rank", className: "w-10 px-2 py-2 text-center md:px-4" },
    { label: "Tier", className: "w-10 px-2 py-2" },
    { label: "Champion", className: "px-2 py-2 md:px-3" },
    { label: "Role", className: "px-2 py-2 md:px-3" },
    { label: "Win Rate", sortKey: "winRate", className: "px-2 py-2 text-right md:px-3" },
    { label: "Pick Rate", sortKey: "pickRate", className: "px-3 py-2 text-right", hiddenMobile: true },
    { label: "Ban Rate", className: "px-3 py-2 text-right", hiddenMobile: true },
    { label: "Games", sortKey: "games", className: "px-2 py-2 text-right md:px-3" },
    { label: "Trend", className: "w-16 px-3 py-2 text-center", hiddenMobile: true },
    { label: "Counters", className: "px-3 py-2 text-right", hiddenMobile: true },
    { label: "Build", className: "px-3 py-2 text-right", hiddenMobile: true }
  ];

  function renderHeader() {
    return (
      <thead className="text-[11px] uppercase tracking-wider text-muted">
        <tr className="border-b border-border/30">
          {columns.map((col) => {
            const sortable = !!col.sortKey;
            const active = col.sortKey === sortCol;
            return (
              <th
                key={col.label}
                className={`${col.className} ${col.hiddenMobile ? "hidden lg:table-cell" : ""} cursor-default`}
                aria-sort={sortable && active ? (sortDir === "asc" ? "ascending" : "descending") : undefined}
              >
                {sortable ? (
                  <button
                    type="button"
                    onClick={() => handleSort(col.sortKey!)}
                    className="inline-flex items-center gap-1 select-none hover:text-fg/80"
                    aria-label={`Sort by ${col.label}${active ? `, currently ${sortDir === "asc" ? "ascending" : "descending"}` : ""}`}
                  >
                    {col.label}
                    {active && (
                      <span aria-hidden="true" className="text-primary text-[10px]">
                        {sortDir === "asc" ? "\u25B2" : "\u25BC"}
                      </span>
                    )}
                  </button>
                ) : (
                  <span className="inline-flex items-center gap-1">{col.label}</span>
                )}
              </th>
            );
          })}
        </tr>
      </thead>
    );
  }

  function renderRow(e: (typeof entriesWithRank)[number]) {
    const champ = champions[String(e.championId)];
    const champName = champ?.name ?? `Champion ${e.championId}`;
    const champSlug = champ?.id ?? "Unknown";
    const championSubtitle = champ?.title ?? "";

    return (
      <tr
        key={`${e.tier}-${e.role}-${e.championId}`}
        className="border-b border-border/20 transition hover:bg-white/[0.06]"
      >
        <td className="px-2 py-2.5 text-center text-xs text-muted md:px-4">{e.rank}</td>
        <td className="px-2 py-2.5">
          <TierBadge tier={e.tier} />
        </td>
        <td className="px-2 py-2.5 md:px-3">
          <Link
            href={`/lol/champions/${e.championId}?role=${encodeURIComponent(e.role)}${rankTierValue ? `&rankTier=${encodeURIComponent(rankTierValue)}` : ""}${activeRegion !== "ALL" ? `&region=${encodeURIComponent(activeRegion)}` : ""}`}
            className="flex items-center gap-2 hover:underline md:gap-2.5"
          >
            <Image
              src={championIconUrl(version, champSlug)}
              alt={champName}
              width={28}
              height={28}
              className="rounded-md"
            />
            <span className="min-w-0">
              <span className="block truncate font-medium text-fg">{champName}</span>
              {championSubtitle ? (
                <span className="hidden truncate text-[11px] text-muted md:block">{championSubtitle}</span>
              ) : null}
            </span>
          </Link>
        </td>
        <td className="px-2 py-2.5 text-xs text-muted md:px-3">{roleDisplayLabel(e.role)}</td>
        <td className="px-2 py-2.5 text-right md:px-3">
          <WinRateText value={e.winRate} decimals={2} />
        </td>
        <td className="hidden px-3 py-2.5 text-right text-fg/70 lg:table-cell">
          {formatPercent(e.pickRate, { decimals: 1 })}
        </td>
        <td
          className="hidden px-3 py-2.5 text-right text-muted lg:table-cell"
          title="Ban rate is not exposed by the current analytics API yet."
        >
          N/A
        </td>
        <td className="px-2 py-2.5 text-right text-fg/70 md:px-3">{formatGames(e.games)}</td>
        <td className="hidden px-3 py-2.5 text-center lg:table-cell">
          <span
            className={`text-sm font-medium ${movementClass(e.movement)}`}
            title={e.previousTier ? `Previous: ${e.previousTier}` : undefined}
          >
            {movementIcon(e.movement)}
          </span>
        </td>
        <td className="hidden px-3 py-2.5 text-right lg:table-cell">
          <Link
            href={`/lol/matchups/${e.championId}?role=${encodeURIComponent(e.role)}${rankTierValue ? `&rankTier=${encodeURIComponent(rankTierValue)}` : ""}`}
            className="text-xs text-primary hover:underline"
          >
            Analyze
          </Link>
        </td>
        <td className="hidden px-3 py-2.5 text-right lg:table-cell">
          <Link
            href={`/lol/champions/${e.championId}?role=${encodeURIComponent(e.role)}${rankTierValue ? `&rankTier=${encodeURIComponent(rankTierValue)}` : ""}#builds`}
            className="text-xs text-primary hover:underline"
          >
            Open
          </Link>
        </td>
      </tr>
    );
  }

  if (isDefaultSort && groups) {
    return (
      <Card className="overflow-hidden p-0">
        {TIER_ORDER.map((tier) => {
          const tierEntries = groups[tier];
          if (tierEntries.length === 0) return null;

          return (
            <div key={tier}>
              <div
                className={`flex items-center gap-3 border-b border-border/40 px-4 py-2.5 ${tierBgClass(tier)}`}
              >
                <TierBadge tier={tier} size="md" />
                <span className={`text-sm font-semibold ${tierColorClass(tier)}`}>
                  Tier {tier}
                </span>
                <span className="text-xs text-muted">
                  {tierEntries.length} champion{tierEntries.length !== 1 ? "s" : ""}
                </span>
              </div>
              <div className="overflow-x-auto">
                <table className="w-full min-w-0 lg:min-w-[940px] text-left text-sm">
                  {renderHeader()}
                  <tbody>{tierEntries.map(renderRow)}</tbody>
                </table>
              </div>
            </div>
          );
        })}
      </Card>
    );
  }

  // Flat sorted view
  return (
    <Card className="overflow-hidden p-0">
      <div className="overflow-x-auto">
        <table className="w-full min-w-0 lg:min-w-[940px] text-left text-sm">
          {renderHeader()}
          <tbody>{sortedEntries.map(renderRow)}</tbody>
        </table>
      </div>
    </Card>
  );
}
