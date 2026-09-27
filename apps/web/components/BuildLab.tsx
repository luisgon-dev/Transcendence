"use client";

import Image from "next/image";
import Link from "next/link";
import { useEffect, useMemo, useState } from "react";
import { useRouter } from "next/navigation";

import { AbilityPill } from "@/components/ui/AbilityPill";
import { Button } from "@/components/ui/Button";
import { DataBar } from "@/components/ui/DataBar";
import { EmptyState } from "@/components/ui/EmptyState";
import { SegmentedControl } from "@/components/ui/SegmentedControl";
import { Select } from "@/components/ui/Select";
import {
  BUILD_LAB_MODES,
  BUILD_LAB_ROLES,
  BUILD_LAB_SECTIONS,
  abilityLetter,
  buildLabPermalink,
  buildLabRegionLabel,
  buildLabRegionOptions,
  buildLabRequestQuery,
  clearBuildLabSelection,
  formatCompactCount,
  formatLift,
  formatPercent,
  hasBuildLabSelection,
  isTerminalBuildLabFamily,
  liftToneClass,
  selectBuildLabOption,
  undoLastBuildLabSelection,
  type BuildLabMode,
  type BuildLabOption,
  type BuildLabResponse,
  type BuildLabSection,
  type BuildLabStage,
  type BuildLabState,
  type BuildLabSummary
} from "@/lib/buildLab";
import { cn } from "@/lib/cn";
import {
  championIconUrl,
  itemIconUrl,
  runeIconUrl,
  summonerSpellIconUrl
} from "@/lib/staticData";

type ChampionOption = { championId: number; slug: string; name: string };
type ItemLookup = Record<string, { name: string; plaintext?: string }>;
type RuneLookup = Record<string, { name: string; icon: string }>;
type SpellLookup = Record<string, { id: string; name: string }>;
type Lookups = {
  items: ItemLookup;
  runes: RuneLookup;
  spells: SpellLookup;
  itemVersion: string;
  spellVersion: string;
};

// Full labels from sm up; a phone splits the control three ways and the long ones would wrap.
const MODE_LABELS: Record<BuildLabMode, { short: string; full: string }> = {
  supported: { short: "Supported", full: "Best supported" },
  impact: { short: "Top lift", full: "Highest lift" },
  common: { short: "Common", full: "Most common" }
};

const SECTION_LABELS: Record<BuildLabSection, string> = {
  items: "Items",
  runes: "Runes",
  spells: "Spells",
  skills: "Skills"
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
function RecommendedBuild({
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
      <ol className="mt-3 grid grid-cols-2 gap-2 sm:grid-cols-3 lg:grid-cols-4 xl:grid-cols-8">
        {pieces.map((piece) => (
          <li
            key={piece.label}
            className="flex min-w-0 items-center gap-2 rounded-control border border-border/60 bg-surface-2/50 px-2.5 py-2"
          >
            <Icons ids={piece.ids} section={piece.section} lookups={lookups} size={28} max={2} />
            <div className="min-w-0">
              <p className="text-[0.6875rem] text-muted">{piece.label}</p>
              <p className="truncate text-xs font-semibold text-fg">
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
function BuildSummary({
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
const ROW_GRID =
  "md:grid-cols-[minmax(0,1fr)_8.75rem_4.75rem_4.25rem_4.5rem_5.25rem] xl:grid-cols-[minmax(0,1fr)_10rem_5.5rem_5.5rem_5rem_5rem_4.5rem_5.5rem]";
const COLLAPSED_OPTIONS = 8;

function Stat({ label, className, children }: { label: string; className?: string; children: React.ReactNode }) {
  return (
    <div role="cell" className={cn("min-w-0 whitespace-nowrap text-sm tabular-nums md:text-right", className)}>
      <p className="text-[0.6875rem] text-muted md:hidden">{label}</p>
      {children}
    </div>
  );
}

function StageList({
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
                <p className="text-sm font-semibold leading-snug text-fg [overflow-wrap:anywhere]">
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

async function problemDetail(response: Response) {
  if (!(response.headers.get("content-type") ?? "").includes("json")) return null;
  try {
    const body = (await response.json()) as { detail?: string; title?: string };
    return body.detail?.trim() || body.title?.trim() || null;
  } catch {
    return null;
  }
}

// Rendered on the server and again in the browser, so it must not depend on either one's time zone
// or locale: a locale-formatted local time differs between the two and breaks hydration.
const COUNTED_AT_FORMAT = new Intl.DateTimeFormat("en-US", {
  month: "short",
  day: "numeric",
  hour: "2-digit",
  minute: "2-digit",
  hourCycle: "h23",
  timeZone: "UTC"
});

function formatCountedAt(value: string) {
  return `${COUNTED_AT_FORMAT.format(new Date(value))} UTC`;
}

function stageNote(stage: BuildLabStage, response: BuildLabResponse) {
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

export function BuildLab({
  championId,
  championSlug,
  championName,
  champions,
  version,
  itemVersion,
  items,
  runes,
  spellVersion,
  spells,
  initialState,
  initialResponse,
  initialIssues = []
}: {
  championId: number;
  championSlug: string;
  championName: string;
  champions: ChampionOption[];
  version: string;
  itemVersion: string;
  items: ItemLookup;
  runes: RuneLookup;
  spellVersion: string;
  spells: SpellLookup;
  initialState: BuildLabState;
  initialResponse: BuildLabResponse;
  initialIssues?: string[];
}) {
  const router = useRouter();
  const [state, setState] = useState(initialState);
  const [response, setResponse] = useState(initialResponse);
  const [loading, setLoading] = useState(false);
  const [requestError, setRequestError] = useState<string | null>(null);
  const [selectionError, setSelectionError] = useState<string | null>(null);
  const [linkIssues, setLinkIssues] = useState<string[]>(initialIssues);
  const lookups: Lookups = { items, runes, spells, itemVersion, spellVersion };

  const championOptions = useMemo(
    () => champions.map((champion) => ({ value: String(champion.championId), label: champion.name })),
    [champions]
  );
  const opponentOptions = useMemo(
    () => [
      { value: "none", label: "Any lane opponent" },
      ...champions
        .filter((champion) => champion.championId !== championId)
        .map((champion) => ({ value: String(champion.championId), label: champion.name }))
    ],
    [championId, champions]
  );
  const regionOptions = useMemo(
    () => buildLabRegionOptions(response.coverage.includedRegions, state.region),
    [response.coverage.includedRegions, state.region]
  );
  // The request depends only on what conditions a decision, so a terminal pick does not refetch.
  const requestKey = buildLabRequestQuery(state).toString();

  useEffect(() => {
    const controller = new AbortController();
    setLoading(true);
    setRequestError(null);
    fetch(`/api/trn/public/lol/analytics/build-lab/${championId}?${requestKey}`, {
      cache: "no-store",
      signal: controller.signal
    })
      .then(async (result) => {
        if (!result.ok) {
          throw new Error(
            (await problemDetail(result)) ?? "Build Lab could not recalculate this context."
          );
        }
        return (await result.json()) as BuildLabResponse;
      })
      .then((body) => {
        setResponse(body);
      })
      .catch((error: unknown) => {
        if (error instanceof DOMException && error.name === "AbortError") return;
        setRequestError(error instanceof Error ? error.message : "Build Lab could not be loaded.");
      })
      .finally(() => {
        if (!controller.signal.aborted) setLoading(false);
      });
    return () => controller.abort();
  }, [championId, requestKey]);

  // Incremental selection uses replace: locking six items must not bury the previous page under six
  // history entries. Only the champion switch (a different route) pushes.
  function updateState(next: BuildLabState) {
    setState(next);
    setSelectionError(null);
    setLinkIssues([]);
    router.replace(buildLabPermalink(championId, next), { scroll: false });
  }

  function selectOption(stage: BuildLabStage, option: BuildLabOption) {
    const result = selectBuildLabOption(state, stage.family, stage.stage, option.actionIds);
    if (result.error) {
      setSelectionError(result.error);
      return;
    }
    updateState(result.state);
  }

  const stages = response.stages;
  const coverage = response.coverage;
  const patchOptions = [
    { value: "recent", label: "Recent patches" },
    ...coverage.includedPatches.map((patch) => ({ value: patch, label: patch })),
    ...(state.patch && !coverage.includedPatches.includes(state.patch)
      ? [{ value: state.patch, label: state.patch }]
      : [])
  ];
  const pooled = !state.patch && coverage.includedPatches.length > 1;

  return (
    // min-w-0: a grid item defaults to min-width:auto, which let a wide child widen the whole page.
    <div className="grid min-w-0 gap-5 [&>*]:min-w-0">
      <header className="flex items-start justify-between gap-4 border-b border-border/60 pb-5">
        <div className="flex min-w-0 items-center gap-3 sm:gap-4">
          <Image
            src={championIconUrl(version, championSlug)}
            alt=""
            width={64}
            height={64}
            className="size-14 rounded-card border border-border/60 sm:size-16"
          />
          <div className="min-w-0">
            <p className="type-kicker text-primary">
              Build Lab<span className="hidden sm:inline"> · Ranked Solo/Duo</span>
            </p>
            <h1 className="type-page-title mt-1 truncate">{championName}</h1>
            <p className="mt-1 hidden text-sm text-muted sm:block">
              Every build choice, with its win rate adjusted for the gold lead it was made with.
            </p>
          </div>
        </div>
        <Link
          href={`/lol/champions/${championId}?role=${state.role}`}
          className="shrink-0 pt-1 text-sm font-medium text-fg/70 hover:text-fg"
        >
          <span className="sm:hidden">Overview</span>
          <span className="hidden sm:inline">Champion overview</span>
        </Link>
      </header>

      <section aria-label="Build Lab context" className="grid gap-3 border-b border-border/50 pb-5">
        <div className="grid grid-cols-2 gap-x-3 gap-y-3 lg:grid-cols-5">
          <label className="grid min-w-0 gap-1.5">
            <span className="type-kicker text-muted">Champion</span>
            <Select
              value={String(championId)}
              options={championOptions}
              onValueChange={(value) => router.push(buildLabPermalink(Number(value), state))}
              ariaLabel="Champion"
              className="w-full"
            />
          </label>
          <label className="grid min-w-0 gap-1.5">
            <span className="type-kicker text-muted">Role</span>
            <Select
              value={state.role}
              options={BUILD_LAB_ROLES.map((role) => ({
                value: role,
                label: role === "UTILITY" ? "Support" : role[0] + role.slice(1).toLowerCase()
              }))}
              onValueChange={(role) =>
                updateState({
                  ...state,
                  role: role as BuildLabState["role"],
                  itemPath: [],
                  starter: [],
                  boots: undefined,
                  keystone: undefined,
                  runePage: [],
                  spellPair: [],
                  skillPriority: [],
                  skillStart: []
                })
              }
              ariaLabel="Role"
              className="w-full"
            />
          </label>
          <label className="grid min-w-0 gap-1.5">
            <span className="type-kicker text-muted">Lane opponent</span>
            <Select
              value={state.opponentChampionId ? String(state.opponentChampionId) : "none"}
              options={opponentOptions}
              onValueChange={(value) =>
                updateState({
                  ...state,
                  opponentChampionId: value === "none" ? undefined : Number(value)
                })
              }
              ariaLabel="Lane opponent"
              className="w-full"
            />
          </label>
          <label className="grid min-w-0 gap-1.5">
            <span className="type-kicker text-muted">Region</span>
            <Select
              value={state.region ?? "ALL"}
              options={regionOptions}
              onValueChange={(region) =>
                updateState({ ...state, region: region === "ALL" ? undefined : region })
              }
              ariaLabel="Region"
              className="w-full"
            />
          </label>
          <label className="col-span-2 grid min-w-0 gap-1.5 lg:col-span-1">
            <span className="type-kicker text-muted">Patch</span>
            <Select
              value={state.patch ?? "recent"}
              options={patchOptions}
              onValueChange={(patch) =>
                updateState({ ...state, patch: patch === "recent" ? undefined : patch })
              }
              ariaLabel="Patch"
              className="w-full"
            />
          </label>
        </div>
        <div className="flex flex-col gap-3 md:flex-row md:items-center md:justify-between">
          <SegmentedControl
            value={state.section}
            onValueChange={(section) => updateState({ ...state, section })}
            options={BUILD_LAB_SECTIONS.map((section) => ({
              value: section,
              label: SECTION_LABELS[section]
            }))}
            ariaLabel="Analytics section"
            className="grid w-full grid-cols-3 md:inline-flex md:w-auto"
          />
          <SegmentedControl
            value={state.mode}
            onValueChange={(mode) => updateState({ ...state, mode })}
            options={BUILD_LAB_MODES.map((mode) => ({
              value: mode,
              "aria-label": MODE_LABELS[mode].full,
              label: (
                <>
                  <span className="sm:hidden">{MODE_LABELS[mode].short}</span>
                  <span className="hidden sm:inline">{MODE_LABELS[mode].full}</span>
                </>
              )
            }))}
            ariaLabel="Ranking mode"
            className="grid w-full grid-cols-3 md:inline-flex md:w-auto"
          />
        </div>
      </section>

      <section className="overflow-hidden rounded-card border border-border/60 bg-surface">
        <div className="flex flex-wrap items-center gap-x-5 gap-y-2 border-b border-border/50 px-4 py-3">
          <span className="type-kicker text-fg/70" aria-live="polite">
            {loading ? "Recalculating…" : buildLabRegionLabel(response.context.requestedRegion)}
          </span>
          <span className="text-xs text-muted">
            {coverage.includedPatches.length === 0
              ? "No patches counted yet"
              : pooled
                ? `Patches ${coverage.includedPatches.join(", ")} · older patches weigh less`
                : `Patch ${coverage.includedPatches[0]}`}
          </span>
          <span className="text-xs text-muted">
            {formatCompactCount(coverage.countedMatches)} games · all tracked ranks
          </span>
          {coverage.lastCountedAtUtc ? (
            <span className="text-xs text-muted">
              Updated {formatCountedAt(coverage.lastCountedAtUtc)}
            </span>
          ) : null}
        </div>

        {linkIssues.length > 0 ? (
          <div
            className="border-b border-border/45 bg-warning/10 px-4 py-2.5 text-xs text-warning"
            role="status"
          >
            {linkIssues.map((issue) => (
              <p key={issue}>{issue}</p>
            ))}
          </div>
        ) : null}

        {response.available && response.summary ? (
          <RecommendedBuild
            summary={response.summary}
            lookups={lookups}
            onFollow={() => {
              const summary = response.summary!;
              updateState({
                ...state,
                section: "items",
                itemPath: summary.items.map((item) => item.actionIds[0]),
                starter: summary.starter?.actionIds ?? state.starter,
                boots: summary.boots?.actionIds[0] ?? state.boots,
                runePage: summary.runePage?.actionIds ?? state.runePage,
                keystone: summary.runePage?.actionIds[0] ?? state.keystone,
                spellPair: summary.spellPair?.actionIds ?? state.spellPair,
                skillPriority: summary.skillPriority?.actionIds ?? state.skillPriority
              });
            }}
          />
        ) : null}

        <BuildSummary
          state={state}
          lookups={lookups}
          onUndo={() => updateState(undoLastBuildLabSelection(state))}
          onClear={() => updateState(clearBuildLabSelection(state))}
        />

        {selectionError ? (
          <p
            className="border-b border-border/45 bg-warning/10 px-4 py-2.5 text-xs text-warning"
            role="alert"
          >
            {selectionError}
          </p>
        ) : null}

        {requestError ? (
          <EmptyState
            title="Build Lab could not recalculate"
            description={requestError}
            className="m-4"
          />
        ) : !response.available ? (
          <EmptyState
            title="Not enough games yet"
            description={
              response.unavailableReason ??
              "No counted games match this champion and role yet."
            }
            action={
              hasBuildLabSelection(state) || state.opponentChampionId || state.region ? (
                <Button
                  size="sm"
                  variant="outline"
                  onClick={() =>
                    updateState({
                      ...clearBuildLabSelection(state),
                      opponentChampionId: undefined,
                      region: undefined
                    })
                  }
                >
                  Show all games for this role
                </Button>
              ) : undefined
            }
            className="m-4"
          />
        ) : (
          <div className="divide-y divide-border/50">
            {stages.map((stage) => {
              const note = stageNote(stage, response);
              return (
                <section key={`${stage.family}-${stage.stage}`} aria-labelledby={`stage-${stage.family}-${stage.stage}`}>
                  <div className="flex flex-wrap items-baseline justify-between gap-x-3 gap-y-1.5 px-4 pb-2.5 pt-4">
                    <h2 id={`stage-${stage.family}-${stage.stage}`} className="type-section">
                      {stage.label}
                    </h2>
                    <span className="flex flex-wrap items-center gap-2 text-xs tabular-nums text-muted">
                      {note ? (
                        <span
                          className={cn(
                            "rounded-control border px-1.5 py-0.5",
                            stage.isFallback
                              ? "border-warning/35 bg-warning/10 text-warning"
                              : "border-border/60"
                          )}
                        >
                          {note}
                        </span>
                      ) : null}
                      {formatCompactCount(stage.games)} games · {formatPercent(stage.winRate)} win rate
                    </span>
                  </div>
                  <StageList
                    stage={stage}
                    section={state.section}
                    lookups={lookups}
                    onSelect={selectOption}
                  />
                </section>
              );
            })}
          </div>
        )}
      </section>

      <p className="max-w-3xl text-xs leading-5 text-muted">
        Adjusted win rate compares each choice with the games where the same decision was made at the
        same gold lead or deficit, so an item usually bought while ahead is not credited for the lead.
        It is still an observed rate, not a guarantee: choices marked “Few games” have wide intervals.
        Matchup and regional numbers lean on all games until they have enough of their own.
      </p>
    </div>
  );
}
