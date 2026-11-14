"use client";

import Image from "next/image";
import { useState } from "react";
import { AbilityPill } from "@/components/ui/AbilityPill";
import { Button } from "@/components/ui/Button";
import { DataBar } from "@/components/ui/DataBar";
import { abilityLetter, buildLabRegionLabel, formatCompactCount, formatLift, formatPercent, isTerminalBuildLabFamily, liftToneClass,
  type BuildLabSection, type BuildLabStage, type BuildLabOption, type BuildLabResponse, type BuildLabSummary, type BuildLabState } from "@/lib/buildLab";
import { cn } from "@/lib/cn";
import { itemIconUrl, runeIconUrl, summonerSpellIconUrl } from "@/lib/staticData";

export type ItemLookup = Record<string, { name: string; plaintext?: string }>;
export type RuneLookup = Record<string, { name: string; icon: string }>;
export type SpellLookup = Record<string, { id: string; name: string }>;
export type Lookups = {
  items: ItemLookup;
  runes: RuneLookup;
  spells: SpellLookup;
  itemVersion: string;
  spellVersion: string;
};

function entityName(id: number, section: BuildLabSection, lookups: Lookups) {
  if (section === "items") return lookups.items[String(id)]?.name ?? `Item ${id}`;
  if (section === "runes") return lookups.runes[String(id)]?.name ?? `Rune ${id}`;
  if (section === "skills") return abilityLetter(id);
  return lookups.spells[String(id)]?.name ?? `Spell ${id}`;
}

function entityIcon(id: number, section: BuildLabSection, lookups: Lookups) {
  if (section === "items") return itemIconUrl(lookups.itemVersion, id);
  if (section === "runes") return runeIconUrl(lookups.runes[String(id)]?.icon ?? "");
  return summonerSpellIconUrl(lookups.spellVersion, lookups.spells[String(id)]?.id ?? "");
}

/** "Doran's Ring + 2× Health Potion": a starter set repeats items, and the repeat is the point. */
function optionName(ids: number[], section: BuildLabSection, lookups: Lookups) {
  // A max order reads as a priority; its letters repeat nothing, and "Q + W + E" would say nothing.
  if (section === "skills") return ids.map(abilityLetter).join(" › ");
  const counts = new Map<number, number>();
  for (const id of ids) counts.set(id, (counts.get(id) ?? 0) + 1);
  return [...counts]
    .map(([id, count]) => `${count > 1 ? `${count}× ` : ""}${entityName(id, section, lookups)}`)
    .join(" + ");
}

/**
 * What tells a rune page apart. Pages sharing a keystone usually differ only in a rune or two near the
 * end, which a full listing truncates away, so every page but the most played one lists only the
 * runes it swaps in relative to it.
 */
function runePageDetail(ids: number[], reference: number[], lookups: Lookups) {
  if (ids === reference) return ids.slice(1).map((id) => entityName(id, "runes", lookups)).join(" · ");
  const swaps = ids.filter((id) => !reference.includes(id));
  return swaps.length === 0
    ? "Same runes, different order"
    : `Swaps in ${swaps.map((id) => entityName(id, "runes", lookups)).join(" · ")}`;
}

/**
 * One icon per distinct id, with a count badge for repeats: a starter with two Health Potions shows
 * the potion once, marked 2×, instead of a fan of overlapping identical icons.
 */
function Icons({
  ids,
  section,
  lookups,
  size = 32,
  max = 3
}: {
  ids: number[];
  section: BuildLabSection;
  lookups: Lookups;
  size?: number;
  max?: number;
}) {
  if (section === "skills") {
    return (
      <span className="flex shrink-0 items-center gap-1">
        {ids.map((id, index) => (
          <AbilityPill key={index} letter={abilityLetter(id)} emphasis={index === 0} />
        ))}
      </span>
    );
  }
  const counts = new Map<number, number>();
  for (const id of ids) counts.set(id, (counts.get(id) ?? 0) + 1);
  const distinct = [...counts];
  return (
    <span className="flex shrink-0 items-center gap-1">
      {distinct.slice(0, max).map(([id, count]) => (
        <span key={id} className="relative shrink-0">
          <Image
            src={entityIcon(id, section, lookups)}
            alt=""
            width={size}
            height={size}
            style={{ width: size, height: size }}
            className={cn(
              "rounded-control border border-border/60 bg-surface-2 object-cover",
              section === "runes" && "rounded-full p-0.5"
            )}
          />
          {count > 1 ? (
            <span className="absolute -bottom-1 -right-1 rounded-[0.3rem] border border-border bg-surface px-1 text-[0.625rem] font-semibold leading-4 tabular-nums text-fg">
              {count}×
            </span>
          ) : null}
        </span>
      ))}
      {distinct.length > max ? (
        <span className="text-xs font-medium tabular-nums text-muted">+{distinct.length - max}</span>
      ) : null}
    </span>
  );
}

/**
 * The answer before the drill-down: the recommended choice at every decision, items followed as a
 * path. "Follow this build" loads it into the lab so every step below is read under it.
 */
export function RecommendedBuild({
  summary,
  lookups,
  onFollow
}: {
  summary: BuildLabSummary;
  lookups: Lookups;
  onFollow: () => void;
}) {
  const pieces: { label: string; option: BuildLabOption; section: BuildLabSection; ids: number[] }[] = [];
  if (summary.starter) pieces.push({ label: "Start", option: summary.starter, section: "items", ids: summary.starter.actionIds });
  summary.items.forEach((item, index) =>
    pieces.push({ label: `Item ${index + 1}`, option: item, section: "items", ids: item.actionIds })
  );
  if (summary.boots) pieces.push({ label: "Boots", option: summary.boots, section: "items", ids: summary.boots.actionIds });
  if (summary.runePage)
    pieces.push({ label: "Runes", option: summary.runePage, section: "runes", ids: summary.runePage.actionIds.slice(0, 1) });
  if (summary.spellPair)
    pieces.push({ label: "Spells", option: summary.spellPair, section: "spells", ids: summary.spellPair.actionIds });
  if (summary.skillPriority)
    pieces.push({ label: "Max", option: summary.skillPriority, section: "skills", ids: summary.skillPriority.actionIds });
  if (pieces.length === 0) return null;

  return (
    <section aria-labelledby="recommended-build" className="border-b border-border/50 px-4 py-4">
      <div className="flex flex-wrap items-baseline justify-between gap-2">
        <h2 id="recommended-build" className="type-section">Recommended build</h2>
        <Button size="sm" variant="outline" onClick={onFollow}>
          Follow this build
        </Button>
      </div>
      <p className="mt-1 text-xs text-muted">
        The best common, well-sampled choice at each step; items follow on from each other.
      </p>
      <ol className="mt-3 grid grid-cols-2 gap-2 sm:grid-cols-[repeat(auto-fill,minmax(11rem,1fr))]">
        {pieces.map((piece) => (
          <li
            key={piece.label}
            className="flex min-w-0 items-center gap-2 rounded-control border border-border/60 bg-surface-2/50 px-2.5 py-2"
          >
            <Icons ids={piece.ids} section={piece.section} lookups={lookups} size={28} max={2} />
            <div className="min-w-0">
              <p className="text-[0.6875rem] text-muted">{piece.label}</p>
              {/* A skill order is already spelled out by its key caps. */}
              <p className={cn("truncate text-xs font-semibold text-fg", piece.section === "skills" && "sr-only")}>
                {piece.section === "runes"
                  ? entityName(piece.ids[0], "runes", lookups)
                  : optionName(piece.option.actionIds, piece.section, lookups)}
              </p>
              <p className="text-[0.6875rem] tabular-nums text-muted">
                {formatPercent(piece.option.adjustedWinRate)} · {formatPercent(piece.option.pickRate)} pick
              </p>
            </div>
          </li>
        ))}
      </ol>
    </section>
  );
}

/** What the user has locked or picked in this section, in build order. */
export function BuildSummary({
  state,
  lookups,
  onUndo,
  onClear
}: {
  state: BuildLabState;
  lookups: Lookups;
  onUndo: () => void;
  onClear: () => void;
}) {
  const groups: { label: string; ids: number[] }[] = [];
  if (state.section === "items") {
    if (state.starter.length > 0) groups.push({ label: "Start", ids: state.starter });
    state.itemPath.forEach((id, index) => groups.push({ label: `Item ${index + 1}`, ids: [id] }));
    if (state.boots) groups.push({ label: "Boots", ids: [state.boots] });
  } else if (state.section === "runes") {
    if (state.runePage.length > 0) groups.push({ label: "Page", ids: state.runePage });
    else if (state.keystone) groups.push({ label: "Keystone", ids: [state.keystone] });
  } else if (state.section === "skills") {
    if (state.skillPriority.length > 0) groups.push({ label: "Max", ids: state.skillPriority });
    if (state.skillStart.length > 0) groups.push({ label: "Start", ids: state.skillStart });
  } else if (state.spellPair.length > 0) {
    groups.push({ label: "Spells", ids: state.spellPair });
  }
  if (groups.length === 0) return null;

  return (
    <div className="flex flex-wrap items-center gap-2 border-b border-border/45 px-4 py-3">
      <span className="type-kicker mr-1 text-muted">Your build</span>
      {groups.map((group) => (
        <span
          key={`${group.label}-${group.ids.join("-")}`}
          className="inline-flex items-center gap-1.5 rounded-control border border-border/60 bg-surface-2 px-2 py-1 text-xs text-fg/80"
        >
          <span className="text-muted">{group.label}</span>
          <Icons ids={group.ids} section={state.section} lookups={lookups} size={22} />
          {group.ids.length === 1 ? entityName(group.ids[0], state.section, lookups) : null}
        </span>
      ))}
      <Button size="sm" variant="ghost" className="ml-auto" onClick={onUndo}>
        Undo
      </Button>
      <Button size="sm" variant="ghost" onClick={onClear}>
        Clear
      </Button>
    </div>
  );
}

// One column template for every stage, so the stacked stages line up. Raw win rate and timing are
// the least important columns and are only shown where there is room for them.
// The adjusted column holds DataBar, whose bar is clamp(56px, 9vw, 104px) plus a gap and the value:
// ~146px at 1024 (the widest md viewport before xl) and ~158px at its cap, so the column is sized to
// those. At 8.75rem it spilled by 3px on a 1024px screen -- caught by e2e/build-lab-layout.spec.ts.
const ROW_GRID =
  "md:grid-cols-[minmax(0,1fr)_9.5rem_4.75rem_4.25rem_4.5rem_5.25rem] xl:grid-cols-[minmax(0,1fr)_10.5rem_5.5rem_5.5rem_5rem_5rem_4.5rem_5.5rem]";
const COLLAPSED_OPTIONS = 8;

function Stat({ label, className, children }: { label: string; className?: string; children: React.ReactNode }) {
  return (
    <div role="cell" className={cn("min-w-0 whitespace-nowrap text-sm tabular-nums md:text-right", className)}>
      <p className="text-[0.6875rem] text-muted md:hidden">{label}</p>
      {children}
    </div>
  );
}

export function StageList({
  stage,
  section,
  lookups,
  onSelect
}: {
  stage: BuildLabStage;
  section: BuildLabSection;
  lookups: Lookups;
  onSelect: (stage: BuildLabStage, option: BuildLabOption) => void;
}) {
  const [expanded, setExpanded] = useState(false);
  if (stage.options.length === 0) {
    return <p className="px-4 py-8 text-sm text-muted">No choice here has enough games yet.</p>;
  }
  const terminal = isTerminalBuildLabFamily(stage.family);
  const referencePage = stage.options.reduce((best, option) =>
    option.games > best.games ? option : best
  ).actionIds;
  const conditions = stage.family === "ITEM" || (stage.family === "RUNE" && stage.stage === 1);
  const timed = stage.family === "ITEM" || stage.family === "BOOTS";
  // Collapsed, a stage shows only choices with enough games to trust -- unless every choice here is
  // thin, in which case hiding them would leave nothing. Options arrive ranked with the low-sample
  // ones last, so expanding keeps the order.
  const trusted = stage.options.filter((option) => !option.isLowSample);
  const collapsed = (trusted.length > 0 ? trusted : stage.options).slice(0, COLLAPSED_OPTIONS);
  const visible = expanded ? stage.options : collapsed;
  const hidden = stage.options.length - collapsed.length;
  const hiddenLowSample = trusted.length > 0 ? stage.options.length - trusted.length : 0;

  // Rows are grids rather than a <table> so a phone gets a card (name and action on one line, the
  // numbers in a strip beneath) from the same cells a wide screen lays out in columns: the stat group
  // is `display: contents` from md up, so its cells join the row's own grid.
  return (
    <div role="table" aria-label={`${stage.label}: gold-adjusted win rate, lift, pick rate and games per choice`}>
      <div
        role="row"
        className={cn(
          "hidden gap-x-3 border-y border-border/55 bg-surface-2/45 px-4 py-2 text-xs font-medium text-muted md:grid",
          ROW_GRID
        )}
      >
        <span role="columnheader">Choice</span>
        <span role="columnheader">Adjusted win rate</span>
        <span role="columnheader" className="text-right">vs. average</span>
        <span role="columnheader" className="hidden text-right xl:block">Raw win rate</span>
        <span role="columnheader" className="text-right">Pick rate</span>
        <span role="columnheader" className="text-right">Games</span>
        <span role="columnheader" className="hidden text-right xl:block">Timing</span>
        <span role="columnheader" className="sr-only">Select</span>
      </div>
      <div role="rowgroup" className="divide-y divide-border/30 border-t border-border/30 md:border-t-0">
        {visible.map((option) => (
          <div
            key={option.actionKey}
            role="row"
            className={cn(
              "grid grid-cols-[minmax(0,1fr)_auto] items-center gap-x-3 gap-y-2.5 px-4 py-3 hover:bg-surface-2/30",
              ROW_GRID,
              option.isLowSample && "text-fg/70"
            )}
          >
            <div role="cell" className="flex min-w-0 items-center gap-3 overflow-hidden">
              {/* A page is identified by its keystone; the line under the name says what else differs,
                  so a fan of every rune would only crowd the name out of a narrow row. */}
              <Icons
                ids={stage.family === "RUNE_PAGE" ? option.actionIds.slice(0, 1) : option.actionIds}
                section={section}
                lookups={lookups}
              />
              <div className="min-w-0">
                <p
                  className={cn(
                    "text-sm font-semibold leading-snug text-fg [overflow-wrap:anywhere]",
                    section === "skills" && "sr-only"
                  )}
                >
                  {/* A rune page is named for its keystone; the rest of the page is what tells two
                      pages with the same keystone apart, so it is listed underneath. */}
                  {stage.family === "RUNE_PAGE"
                    ? entityName(option.actionIds[0], section, lookups)
                    : optionName(option.actionIds, section, lookups)}
                </p>
                {stage.family === "RUNE_PAGE" && option.actionIds.length > 1 ? (
                  <p className="mt-0.5 truncate text-xs text-muted">
                    {runePageDetail(option.actionIds, referencePage, lookups)}
                  </p>
                ) : null}
                {option.isLowSample ? (
                  <p className="mt-0.5 text-[0.6875rem] font-medium text-muted">Few games</p>
                ) : null}
              </div>
            </div>

            <div className="col-span-2 row-start-2 grid grid-cols-[minmax(0,1.55fr)_minmax(0,1.15fr)_minmax(0,0.9fr)_minmax(0,0.9fr)] gap-x-2.5 md:contents">
              <div role="cell" className="min-w-0">
                <p className="text-[0.6875rem] text-muted md:hidden">Adjusted</p>
                {/* A thin sample's bar would read as confidently as a solid one; muting it keeps the
                    eye on the rows whose interval actually supports the number. */}
                <DataBar
                  value={option.adjustedWinRate}
                  className={option.isLowSample ? "opacity-45" : undefined}
                />
                <p className="mt-0.5 text-[0.6875rem] tabular-nums text-muted">
                  {formatPercent(option.confidenceLow)}–{formatPercent(option.confidenceHigh)}
                </p>
              </div>
              <Stat label="vs. avg" className={cn("font-semibold", liftToneClass(option.lift, option.isLowSample))}>
                {formatLift(option.lift)}
              </Stat>
              <Stat label="Raw" className="hidden text-fg/72 xl:block">
                {formatPercent(option.winRate)}
              </Stat>
              <Stat label="Pick" className="text-fg/72">
                {formatPercent(option.pickRate)}
              </Stat>
              <Stat label="Games" className="text-fg/72">
                {formatCompactCount(option.games)}
                {timed && option.averageTimingMinutes != null ? (
                  <span className="block text-[0.6875rem] text-muted xl:hidden">
                    {option.averageTimingMinutes.toFixed(1)}m
                  </span>
                ) : null}
              </Stat>
              <Stat label="Timing" className="hidden text-fg/72 xl:block">
                {timed && option.averageTimingMinutes != null
                  ? `${option.averageTimingMinutes.toFixed(1)}m`
                  : "—"}
              </Stat>
            </div>

            <div role="cell" className="col-start-2 row-start-1 justify-self-end md:col-start-auto md:row-start-auto">
              {terminal || conditions ? (
                <Button size="sm" variant="outline" onClick={() => onSelect(stage, option)}>
                  {conditions ? "Lock" : "Pick"}
                </Button>
              ) : null}
            </div>
          </div>
        ))}
      </div>
      {hidden > 0 ? (
        <div className="border-t border-border/30 px-4 py-2">
          <Button size="sm" variant="ghost" onClick={() => setExpanded((value) => !value)}>
            {expanded
              ? "Show fewer"
              : hiddenLowSample > 0
                ? `Show ${hidden} more · ${hiddenLowSample} with few games`
                : `Show ${hidden} more`}
          </Button>
        </div>
      ) : null}
    </div>
  );
}


export function stageNote(stage: BuildLabStage, response: BuildLabResponse) {
  if (stage.isFallback) {
    // Deeper steps are never split by matchup or region at all, so this reads true for both cases:
    // the narrow population is too thin here, whether or not it was counted.
    return response.context.opponentChampionId
      ? "Matchup too thin at this step — showing all opponents"
      : `${buildLabRegionLabel(response.context.requestedRegion)} too thin at this step — showing all regions`;
  }
  if (stage.scope === "MATCHUP") return "This matchup";
  if (stage.scope === "REGION") return buildLabRegionLabel(response.context.requestedRegion);
  return null;
}
