"use client";

import { startTransition, useEffect, useMemo, useRef, useState } from "react";
import { flushSync } from "react-dom";
import Image from "next/image";
import Link from "next/link";

import { TierBadge } from "@/components/TierBadge";
import { WinRateText } from "@/components/WinRateText";
import { Button } from "@/components/ui/Button";
import { Card } from "@/components/ui/Card";
import { cn } from "@/lib/cn";
import { formatGames, formatPercent } from "@/lib/format";
import { roleDisplayLabel } from "@/lib/roles";
import { championIconUrl } from "@/lib/staticData";
import {
  movementClass,
  movementIcon,
  summarizeTierListEntries,
  tierBgClass,
  tierBorderClass,
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

const COLUMNS: { label: string; sortKey?: SortColumn; className: string; hiddenMobile?: boolean }[] = [
  { label: "#", sortKey: "rank", className: "w-10 px-2 text-center md:px-4" },
  { label: "Tier", className: "w-10 px-2" },
  { label: "Champion", className: "px-2 md:px-3" },
  { label: "Role", className: "px-2 md:px-3" },
  { label: "Win Rate", sortKey: "winRate", className: "px-2 text-right md:px-3" },
  { label: "Pick Rate", sortKey: "pickRate", className: "px-3 text-right", hiddenMobile: true },
  { label: "Ban Rate", className: "px-3 text-right", hiddenMobile: true },
  { label: "Games", sortKey: "games", className: "px-2 text-right md:px-3" },
  { label: "Trend", className: "w-16 px-3 text-center", hiddenMobile: true },
  { label: "Counters", className: "px-3 text-right", hiddenMobile: true },
  { label: "Build", className: "px-3 text-right", hiddenMobile: true }
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

function sortLabel(sortCol: SortColumn) {
  switch (sortCol) {
    case "winRate":
      return "Win rate";
    case "pickRate":
      return "Pick rate";
    case "games":
      return "Games";
    case "rank":
    default:
      return "Composite rank";
  }
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

    const grouped: Record<UITierGrade, RowEntry[]> = {
      S: [],
      A: [],
      B: [],
      C: [],
      D: []
    };

    for (const entry of sortedEntries) {
      grouped[entry.tier].push(entry);
    }

    return grouped;
  }, [isDefaultSort, sortedEntries]);

  const visibleTiers = useMemo(
    () => TIER_ORDER.filter((tier) => summary.tierCounts[tier] > 0),
    [summary.tierCounts]
  );

  const leadEntry = sortedEntries[0] ?? null;
  const leadChampion = leadEntry ? champions[String(leadEntry.championId)] : null;
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
      {
        rootMargin: "-18% 0px -58% 0px",
        threshold: [0.2, 0.45, 0.7]
      }
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

  function jumpToTier(tier: UITierGrade) {
    const target = sectionRefs.current[tier];
    if (!target) return;

    target.scrollIntoView({
      behavior: motionReduced() ? "auto" : "smooth",
      block: "start"
    });
  }

  function renderHeader() {
    return (
      <thead className="type-overline text-muted">
        <tr className="border-b border-border/30">
          {COLUMNS.map((column) => {
            const sortable = Boolean(column.sortKey);
            const active = column.sortKey === sortCol;

            return (
              <th
                key={column.label}
                className={cn(
                  "tierlist-sticky-head",
                  column.className,
                  column.hiddenMobile ? "hidden lg:table-cell" : "",
                  "cursor-default py-2.5"
                )}
                aria-sort={
                  sortable && active
                    ? sortDir === "asc"
                      ? "ascending"
                      : "descending"
                    : undefined
                }
              >
                {sortable ? (
                  <button
                    type="button"
                    onClick={() => applySort(column.sortKey!)}
                    className="inline-flex items-center gap-1 select-none transition hover:text-fg/82"
                    aria-label={`Sort by ${column.label}${active ? `, currently ${sortDir === "asc" ? "ascending" : "descending"}` : ""}`}
                  >
                    {column.label}
                    {active ? (
                      <span aria-hidden="true" className="type-overline text-primary">
                        {sortDir === "asc" ? "\u25B2" : "\u25BC"}
                      </span>
                    ) : null}
                  </button>
                ) : (
                  <span className="inline-flex items-center gap-1">{column.label}</span>
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
      <tr key={`${entry.tier}-${entry.role}-${entry.championId}`} className="tierlist-row border-b border-border/20 text-sm">
        <td className="px-2 py-3 text-center text-xs text-muted md:px-4">{entry.rank}</td>
        <td className="px-2 py-3">
          <TierBadge tier={entry.tier} />
        </td>
        <td className="px-2 py-3 md:px-3">
          <Link
            href={`/lol/champions/${entry.championId}?${rowQuery}`}
            className="flex items-center gap-2 hover:underline md:gap-2.5"
          >
            <Image
              src={championIconUrl(version, championSlug)}
              alt={championName}
              width={28}
              height={28}
              className="rounded-md"
            />
            <span className="min-w-0">
              <span className="block truncate font-medium text-fg">{championName}</span>
              {championSubtitle ? (
                <span className="type-caption hidden truncate text-muted md:block">
                  {championSubtitle}
                </span>
              ) : null}
            </span>
          </Link>
        </td>
        <td className="px-2 py-3 text-xs text-muted md:px-3">{roleDisplayLabel(entry.role)}</td>
        <td className="px-2 py-3 text-right md:px-3">
          <WinRateText value={entry.winRate} decimals={2} />
        </td>
        <td className="hidden px-3 py-3 text-right text-fg/70 lg:table-cell">
          {formatPercent(entry.pickRate, { decimals: 1 })}
        </td>
        <td
          className="hidden px-3 py-3 text-right text-muted lg:table-cell"
          title="Ban rate is not exposed by the current analytics API yet."
        >
          N/A
        </td>
        <td className="px-2 py-3 text-right text-fg/70 md:px-3">{formatGames(entry.games)}</td>
        <td className="hidden px-3 py-3 text-center lg:table-cell">
          <span
            className={`text-sm font-medium ${movementClass(entry.movement)}`}
            title={entry.previousTier ? `Previous: ${entry.previousTier}` : undefined}
          >
            {movementIcon(entry.movement)}
          </span>
        </td>
        <td className="hidden px-3 py-3 text-right lg:table-cell">
          <Link
            href={`/lol/matchups/${entry.championId}?${rowQuery}`}
            className="text-xs text-primary hover:underline"
          >
            Analyze
          </Link>
        </td>
        <td className="hidden px-3 py-3 text-right lg:table-cell">
          <Link
            href={`/lol/champions/${entry.championId}?${rowQuery}#builds`}
            className="text-xs text-primary hover:underline"
          >
            Open
          </Link>
        </td>
      </tr>
    );
  }

  return (
    <div className="grid gap-4">
      <Card className="tierlist-toolbar p-4 sm:p-5">
        <div className="flex flex-col gap-4 xl:flex-row xl:items-end xl:justify-between">
          <div className="grid gap-1.5">
            <p className="type-kicker text-primary/88">Board view</p>
            <p className="type-ui text-fg/74">
              {focusTier === "ALL"
                ? `${allSummary.visibleCount} champions ranked for this patch window.`
                : `Showing Tier ${focusTier} champions only.`}
            </p>
            <div className="flex flex-wrap items-center gap-2 pt-1">
              <span className="surface-chip rounded-full px-3 py-1.5 text-xs text-fg/72">
                {summary.totalGames.toLocaleString()} games
              </span>
              {summary.averageWinRate != null ? (
                <span className="surface-chip rounded-full px-3 py-1.5 text-xs text-fg/72">
                  Avg WR {formatPercent(summary.averageWinRate, { decimals: 2 })}
                </span>
              ) : null}
              {leadEntry ? (
                <span className="surface-chip rounded-full px-3 py-1.5 text-xs text-fg/72">
                  Lead {leadChampion?.name ?? `#${leadEntry.championId}`}{" "}
                  {formatPercent(leadEntry.winRate, { decimals: 2 })}
                </span>
              ) : null}
            </div>
          </div>

          <div className="grid gap-2 xl:justify-items-end">
            <p className="type-kicker text-muted">Sort locally</p>
            <div className="flex flex-wrap gap-2 xl:justify-end">
              {(["rank", "winRate", "pickRate", "games"] as const).map((option) => (
                <button
                  key={option}
                  type="button"
                  className="tierlist-filter-pill"
                  data-active={sortCol === option}
                  aria-pressed={sortCol === option}
                  onClick={() => applySort(option)}
                >
                  <span>{sortLabel(option)}</span>
                  {sortCol === option ? (
                    <span className="text-primary">{sortDir === "asc" ? "\u25B2" : "\u25BC"}</span>
                  ) : null}
                </button>
              ))}
            </div>
          </div>
        </div>

        <div className="mt-4 flex flex-col gap-2 lg:flex-row lg:items-center lg:justify-between">
          <div className="flex flex-wrap items-center gap-2">
            <span className="type-kicker mr-1 text-muted">Tier focus</span>
            {(["ALL", ...TIER_ORDER] as const).map((tier) => (
              <button
                key={tier}
                type="button"
                className="tierlist-filter-pill"
                data-active={focusTier === tier}
                aria-pressed={focusTier === tier}
                onClick={() => applyFocusTier(tier)}
              >
                <span>{tier === "ALL" ? "All tiers" : `Tier ${tier}`}</span>
                <span className="text-fg/56">
                  {tier === "ALL" ? allSummary.visibleCount : allSummary.tierCounts[tier]}
                </span>
              </button>
            ))}
          </div>

          {isFilteredView ? (
            <Button variant="ghost" size="sm" onClick={resetView} className="w-fit rounded-full px-4">
              Reset board view
            </Button>
          ) : null}
        </div>
      </Card>

      {summary.visibleCount === 0 ? (
        <Card className="tierlist-table-shell grid gap-3 p-6">
          <p className="type-section text-fg">No champions match this local view.</p>
          <p className="type-ui text-fg/72">
            Reset the board view to bring every tier back into the table.
          </p>
          <Button variant="outline" size="sm" onClick={resetView} className="w-fit">
            Reset board view
          </Button>
        </Card>
      ) : (
        <div className={cn("grid gap-4", showTierRail ? "xl:grid-cols-[200px_minmax(0,1fr)]" : "")}>
          {showTierRail ? (
            <nav className="hidden xl:grid xl:h-fit xl:gap-2 xl:sticky xl:top-24">
              {visibleTiers.map((tier) => {
                const tierEntries = groups?.[tier] ?? [];

                return (
                  <button
                    key={tier}
                    type="button"
                    className={cn("tierlist-tier-rail", tierBorderClass(tier))}
                    data-active={activeTierSection === tier}
                    onClick={() => jumpToTier(tier)}
                  >
                    <div className="flex items-center justify-between gap-3">
                      <span className={cn("font-semibold", tierColorClass(tier))}>Tier {tier}</span>
                      <span className="type-ui text-fg/62">{tierEntries.length}</span>
                    </div>
                    <p className="mt-1 text-xs text-fg/58">
                      Best WR{" "}
                      {tierEntries.length > 0
                        ? formatPercent(
                            Math.max(...tierEntries.map((entry) => entry.winRate)),
                            { decimals: 2 }
                          )
                        : "n/a"}
                    </p>
                  </button>
                );
              })}
            </nav>
          ) : null}

          <Card className="tierlist-table-shell p-0">
            {isDefaultSort && groups ? (
              <div className="grid gap-px bg-border/20">
                {TIER_ORDER.map((tier) => {
                  const tierEntries = groups[tier];
                  if (tierEntries.length === 0) return null;

                  return (
                    <section
                      key={tier}
                      ref={(node) => {
                        sectionRefs.current[tier] = node;
                      }}
                      data-tier={tier}
                      className="tierlist-tier-section bg-surface/12"
                    >
                      <div
                        className={cn(
                          "flex items-center gap-3 border-b border-border/40 px-4 py-3",
                          tierBgClass(tier)
                        )}
                      >
                        <TierBadge tier={tier} size="md" />
                        <span className={cn("text-sm font-semibold", tierColorClass(tier))}>
                          Tier {tier}
                        </span>
                        <span className="text-xs text-muted">
                          {tierEntries.length} champion{tierEntries.length !== 1 ? "s" : ""}
                        </span>
                        <span className="ml-auto hidden text-xs text-fg/58 sm:block">
                          Best WR{" "}
                          {formatPercent(
                            Math.max(...tierEntries.map((entry) => entry.winRate)),
                            { decimals: 2 }
                          )}
                        </span>
                      </div>
                      <div className="tierlist-table-scroll overflow-x-auto">
                        <table className="w-full min-w-0 text-left lg:min-w-[940px]">
                          {renderHeader()}
                          <tbody>{tierEntries.map(renderRow)}</tbody>
                        </table>
                      </div>
                    </section>
                  );
                })}
              </div>
            ) : (
              <div className="tierlist-table-scroll overflow-x-auto">
                <table className="w-full min-w-0 text-left lg:min-w-[940px]">
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
