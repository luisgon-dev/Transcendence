"use client";

import { startTransition, useEffect, useMemo, useRef, useState } from "react";
import { flushSync } from "react-dom";
import Image from "next/image";
import Link from "next/link";

import { TierBadge } from "@/components/TierBadge";
import { Button } from "@/components/ui/Button";
import { Card } from "@/components/ui/Card";
import { DataBar } from "@/components/ui/DataBar";
import { LaneIcon } from "@/components/ui/LaneIcon";
import { TierSpine } from "@/components/ui/TierSpine";
import { cn } from "@/lib/cn";
import { formatGames, formatPercent } from "@/lib/format";
import { roleDisplayLabel } from "@/lib/roles";
import { championIconUrl } from "@/lib/staticData";
import {
  movementClass,
  movementIcon,
  movementLabel,
  summarizeTierListEntries,
  tierBgClass,
  tierColorClass,
  TIER_ORDER,
  type TierListChampionMap,
  type TierListFocusTier,
  type UITierGrade,
  type UITierListEntry
} from "@/lib/tierlist";

type SortColumn = "rank" | "winRate" | "pickRate" | "games";
type SortDir = "asc" | "desc";
type RowEntry = UITierListEntry & { rank: number };

type DocumentWithViewTransition = Document & {
  startViewTransition?: typeof startTransition;
};

// Column visibility is encoded per-column so header and body stay in lockstep.
// Core columns (rank, tier, champion, win rate, games) show at every width; the
// rest reveal progressively — no forced horizontal scroll, no silent drops.
const COLUMNS: {
  label: string;
  sortKey?: SortColumn;
  className: string;
}[] = [
  { label: "#", sortKey: "rank", className: "w-9 px-2 text-center md:px-3" },
  { label: "Tier", className: "w-10 px-2" },
  { label: "Champion", className: "px-2 md:px-3" },
  { label: "Lane", className: "hidden px-2 sm:table-cell md:px-3" },
  { label: "Win Rate", sortKey: "winRate", className: "px-2 text-right md:px-3" },
  { label: "Pick Rate", sortKey: "pickRate", className: "hidden px-3 text-right lg:table-cell" },
  { label: "Games", sortKey: "games", className: "px-2 text-right md:px-3" },
  { label: "Trend", className: "hidden w-14 px-3 text-center lg:table-cell" },
  { label: "Analyze", className: "hidden px-3 text-right md:table-cell" }
];

function motionReduced() {
  return typeof window !== "undefined" && window.matchMedia("(prefers-reduced-motion: reduce)").matches;
}

function startVisualTransition(update: () => void) {
  if (typeof document === "undefined" || motionReduced()) {
    startTransition(update);
    return;
  }

  const viewTransitionDocument = document as DocumentWithViewTransition;
  if (typeof viewTransitionDocument.startViewTransition === "function") {
    viewTransitionDocument.startViewTransition(() => {
      flushSync(update);
    });
    return;
  }

  startTransition(update);
}

export function TierListTable({
  entries,
  champions,
  version,
  rankTierValue,
  activeRegion,
  activePatch
}: {
  entries: UITierListEntry[];
  champions: TierListChampionMap;
  version: string;
  rankTierValue: string | null;
  activeRegion: string;
  activePatch?: string | null;
}) {
  const [sortCol, setSortCol] = useState<SortColumn>("rank");
  const [sortDir, setSortDir] = useState<SortDir>("asc");
  const [focusTier, setFocusTier] = useState<TierListFocusTier>("ALL");
  const [activeTierSection, setActiveTierSection] = useState<UITierGrade>("S");
  const sectionRefs = useRef<Record<UITierGrade, HTMLElement | null>>({
    S: null,
    A: null,
    B: null,
    C: null,
    D: null
  });

  const entriesWithRank = useMemo<RowEntry[]>(
    () => entries.map((entry, index) => ({ ...entry, rank: index + 1 })),
    [entries]
  );

  const filteredEntries = useMemo(
    () =>
      focusTier === "ALL"
        ? entriesWithRank
        : entriesWithRank.filter((entry) => entry.tier === focusTier),
    [entriesWithRank, focusTier]
  );

  const sortedEntries = useMemo(() => {
    if (sortCol === "rank") {
      const sorted = [...filteredEntries].sort((a, b) => a.rank - b.rank);
      return sortDir === "desc" ? sorted.reverse() : sorted;
    }

    const sorted = [...filteredEntries].sort((a, b) => a[sortCol] - b[sortCol]);
    return sortDir === "desc" ? sorted.reverse() : sorted;
  }, [filteredEntries, sortCol, sortDir]);

  const allSummary = useMemo(() => summarizeTierListEntries(entriesWithRank), [entriesWithRank]);
  const summary = useMemo(() => summarizeTierListEntries(filteredEntries), [filteredEntries]);
  const isDefaultSort = sortCol === "rank" && sortDir === "asc";
  const isFilteredView = focusTier !== "ALL" || !isDefaultSort;

  const groups = useMemo(() => {
    if (!isDefaultSort) return null;

    const grouped: Record<UITierGrade, RowEntry[]> = { S: [], A: [], B: [], C: [], D: [] };
    for (const entry of sortedEntries) {
      grouped[entry.tier].push(entry);
    }
    return grouped;
  }, [isDefaultSort, sortedEntries]);

  const visibleTiers = useMemo(
    () => TIER_ORDER.filter((tier) => summary.tierCounts[tier] > 0),
    [summary.tierCounts]
  );

  const showTierRail = isDefaultSort && focusTier === "ALL" && visibleTiers.length > 1;

  useEffect(() => {
    if (visibleTiers.length === 0) return;
    if (!visibleTiers.includes(activeTierSection)) {
      setActiveTierSection(visibleTiers[0]);
    }
  }, [activeTierSection, visibleTiers]);

  useEffect(() => {
    if (!showTierRail || typeof IntersectionObserver === "undefined") return;

    const observer = new IntersectionObserver(
      (observedEntries) => {
        const visibleEntry = observedEntries
          .filter((entry) => entry.isIntersecting)
          .sort((a, b) => b.intersectionRatio - a.intersectionRatio)[0];

        const tier = visibleEntry?.target.getAttribute("data-tier");
        if (tier && TIER_ORDER.includes(tier as UITierGrade)) {
          setActiveTierSection(tier as UITierGrade);
        }
      },
      { rootMargin: "-18% 0px -58% 0px", threshold: [0.2, 0.45, 0.7] }
    );

    const nodes = visibleTiers
      .map((tier) => sectionRefs.current[tier])
      .filter((node): node is HTMLElement => node !== null);

    nodes.forEach((node) => observer.observe(node));
    return () => observer.disconnect();
  }, [showTierRail, visibleTiers]);

  function applySort(nextSortCol: SortColumn) {
    startVisualTransition(() => {
      if (nextSortCol === sortCol) {
        setSortDir((currentDir) => (currentDir === "asc" ? "desc" : "asc"));
        return;
      }
      setSortCol(nextSortCol);
      setSortDir(nextSortCol === "rank" ? "asc" : "desc");
    });
  }

  function applyFocusTier(nextTier: TierListFocusTier) {
    startVisualTransition(() => {
      setFocusTier(nextTier);
      if (nextTier !== "ALL") setActiveTierSection(nextTier);
    });
  }

  function resetView() {
    startVisualTransition(() => {
      setFocusTier("ALL");
      setSortCol("rank");
      setSortDir("asc");
    });
  }

  function jumpToTier(tier: string) {
    const target = sectionRefs.current[tier as UITierGrade];
    if (!target) return;
    target.scrollIntoView({ behavior: motionReduced() ? "auto" : "smooth", block: "start" });
  }

  function renderHeader() {
    return (
      <thead className="type-overline text-muted">
        <tr className="border-b border-border/60">
          {COLUMNS.map((column) => {
            const sortable = Boolean(column.sortKey);
            const active = column.sortKey === sortCol;

            return (
              <th
                key={column.label}
                className={cn("tierlist-sticky-head cursor-default py-2.5", column.className)}
                aria-sort={
                  sortable && active ? (sortDir === "asc" ? "ascending" : "descending") : undefined
                }
              >
                {sortable ? (
                  <button
                    type="button"
                    onClick={() => applySort(column.sortKey!)}
                    className="inline-flex items-center gap-1 select-none transition-colors hover:text-fg"
                    aria-label={`Sort by ${column.label}${active ? `, currently ${sortDir === "asc" ? "ascending" : "descending"}` : ""}`}
                  >
                    {column.label}
                    {active ? (
                      <span aria-hidden="true" className="text-primary">
                        {sortDir === "asc" ? "▲" : "▼"}
                      </span>
                    ) : null}
                  </button>
                ) : (
                  <span>{column.label}</span>
                )}
              </th>
            );
          })}
        </tr>
      </thead>
    );
  }

  function renderRow(entry: RowEntry) {
    const champion = champions[String(entry.championId)];
    const championName = champion?.name ?? `Champion ${entry.championId}`;
    const championSlug = champion?.id ?? "Unknown";
    const championSubtitle = champion?.title ?? "";
    const rowParams = new URLSearchParams({ role: entry.role });
    if (rankTierValue) rowParams.set("rankTier", rankTierValue);
    if (activeRegion !== "ALL") rowParams.set("region", activeRegion);
    if (activePatch) rowParams.set("patch", activePatch);
    const rowQuery = rowParams.toString();

    return (
      <tr
        key={`${entry.tier}-${entry.role}-${entry.championId}`}
        className="tierlist-row border-b border-border/40 text-sm last:border-0"
      >
        <td className="px-2 py-2.5 text-center md:px-3">
          <span className={cn("type-tabular tabular-nums text-xs font-semibold", tierColorClass(entry.tier))}>
            {entry.rank}
          </span>
        </td>
        <td className="px-2 py-2.5">
          <TierBadge tier={entry.tier} />
        </td>
        <td className="px-2 py-2.5 md:px-3">
          <Link
            href={`/lol/champions/${entry.championId}?${rowQuery}`}
            className="group flex items-center gap-2.5"
          >
            <Image
              src={championIconUrl(version, championSlug)}
              alt={championName}
              width={30}
              height={30}
              className="rounded-md ring-1 ring-border/60"
            />
            <span className="min-w-0">
              <span className="block truncate font-semibold text-fg group-hover:text-primary">
                {championName}
              </span>
              {championSubtitle ? (
                <span className="type-caption hidden truncate text-muted md:block">
                  {championSubtitle}
                </span>
              ) : null}
            </span>
          </Link>
        </td>
        <td className="hidden px-2 py-2.5 sm:table-cell md:px-3">
          <span className="inline-flex items-center gap-1.5 text-xs text-muted">
            <LaneIcon role={entry.role} className="size-[18px] shrink-0 text-fg/55" />
            <span className="hidden md:inline">{roleDisplayLabel(entry.role)}</span>
          </span>
        </td>
        <td className="px-2 py-2.5 text-right md:px-3">
          <DataBar value={entry.winRate} decimals={2} className="justify-end" />
        </td>
        <td className="hidden px-3 py-2.5 text-right text-fg/70 lg:table-cell">
          {formatPercent(entry.pickRate, { decimals: 1 })}
        </td>
        <td className="type-tabular px-2 py-2.5 text-right tabular-nums text-fg/70 md:px-3">
          {formatGames(entry.games)}
        </td>
        <td className="hidden px-3 py-2.5 text-center lg:table-cell">
          <span
            className={cn("text-sm font-semibold", movementClass(entry.movement))}
            title={entry.previousTier ? `Previous: ${entry.previousTier}` : movementLabel(entry.movement)}
          >
            {movementIcon(entry.movement)}
            <span className="sr-only">{movementLabel(entry.movement)}</span>
          </span>
        </td>
        <td className="hidden px-3 py-2.5 text-right md:table-cell">
          <Link
            href={`/lol/matchups/${entry.championId}?${rowQuery}`}
            className="type-ui font-medium text-primary hover:underline"
          >
            Analyze
          </Link>
        </td>
      </tr>
    );
  }

  const spineItems = useMemo(
    () =>
      visibleTiers.map((tier) => {
        const tierEntries = groups?.[tier] ?? [];
        const bestWr = tierEntries.length
          ? Math.max(...tierEntries.map((e) => e.winRate))
          : null;
        return {
          tier,
          count: tierEntries.length,
          hint: bestWr != null ? `Best ${formatPercent(bestWr, { decimals: 1 })}` : undefined
        };
      }),
    [visibleTiers, groups]
  );

  return (
    <div className="grid gap-3">
      <div className="flex flex-col gap-3 px-1 sm:flex-row sm:items-center sm:justify-between">
        <div className="flex flex-wrap items-center gap-1.5">
          {(["ALL", ...TIER_ORDER] as const).map((tier) => (
            <button
              key={tier}
              type="button"
              className="tierlist-filter-pill"
              data-active={focusTier === tier}
              aria-pressed={focusTier === tier}
              onClick={() => applyFocusTier(tier)}
            >
              <span className="font-semibold">{tier === "ALL" ? "All" : tier}</span>
              <span className="type-tabular tabular-nums text-muted">
                {tier === "ALL" ? allSummary.visibleCount : allSummary.tierCounts[tier]}
              </span>
            </button>
          ))}
        </div>

        <div className="type-tabular flex flex-wrap items-center gap-x-3 gap-y-1 tabular-nums text-xs text-muted">
          <span>{summary.totalGames.toLocaleString()} games</span>
          {summary.averageWinRate != null ? (
            <span className="border-l border-border pl-3">
              Avg WR {formatPercent(summary.averageWinRate, { decimals: 2 })}
            </span>
          ) : null}
          {isFilteredView ? (
            <Button variant="ghost" size="sm" onClick={resetView} className="ml-1 h-8 px-3">
              Reset
            </Button>
          ) : null}
        </div>
      </div>

      {summary.visibleCount === 0 ? (
        <Card className="grid gap-3 p-6">
          <p className="type-section text-fg">No champions match this view.</p>
          <p className="type-ui text-muted">Reset the board to bring every tier back into the table.</p>
          <Button variant="outline" size="sm" onClick={resetView} className="w-fit">
            Reset board view
          </Button>
        </Card>
      ) : (
        <div className={cn("grid gap-4", showTierRail ? "xl:grid-cols-[180px_minmax(0,1fr)]" : "")}>
          {showTierRail ? (
            <div className="hidden xl:block">
              <TierSpine
                items={spineItems}
                active={activeTierSection}
                onSelect={jumpToTier}
                className="sticky top-24"
              />
            </div>
          ) : null}

          <Card className="overflow-hidden p-0">
            {isDefaultSort && groups ? (
              <div className="grid">
                {TIER_ORDER.map((tier, tierIdx) => {
                  const tierEntries = groups[tier];
                  if (tierEntries.length === 0) return null;

                  return (
                    <section
                      key={tier}
                      ref={(node) => {
                        sectionRefs.current[tier] = node;
                      }}
                      data-tier={tier}
                      className="tierlist-tier-section reveal-up"
                      style={{ animationDelay: `${tierIdx * 55}ms` }}
                    >
                      <div className={cn("flex items-center gap-3 border-y border-border/60 px-4 py-2.5", tierBgClass(tier))}>
                        <TierBadge tier={tier} size="md" />
                        <span className={cn("text-sm font-bold", tierColorClass(tier))}>Tier {tier}</span>
                        <span className="type-tabular tabular-nums text-xs text-muted">
                          {tierEntries.length} champion{tierEntries.length !== 1 ? "s" : ""}
                        </span>
                        <span className="type-tabular ml-auto hidden tabular-nums text-xs text-muted sm:block">
                          Best WR {formatPercent(Math.max(...tierEntries.map((e) => e.winRate)), { decimals: 2 })}
                        </span>
                      </div>
                      <div className="overflow-x-auto">
                        <table className="w-full text-left">
                          {renderHeader()}
                          <tbody>{tierEntries.map(renderRow)}</tbody>
                        </table>
                      </div>
                    </section>
                  );
                })}
              </div>
            ) : (
              <div className="overflow-x-auto">
                <table className="w-full text-left">
                  {renderHeader()}
                  <tbody>{sortedEntries.map(renderRow)}</tbody>
                </table>
              </div>
            )}
          </Card>
        </div>
      )}
    </div>
  );
}
