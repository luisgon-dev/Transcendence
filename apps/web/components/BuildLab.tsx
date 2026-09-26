"use client";

import Image from "next/image";
import Link from "next/link";
import { useEffect, useMemo, useState } from "react";
import { useRouter } from "next/navigation";

import { Button } from "@/components/ui/Button";
import { DataBar } from "@/components/ui/DataBar";
import { EmptyState } from "@/components/ui/EmptyState";
import { SegmentedControl } from "@/components/ui/SegmentedControl";
import { Select } from "@/components/ui/Select";
import {
  BUILD_LAB_MODES,
  BUILD_LAB_ROLES,
  BUILD_LAB_SECTIONS,
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
  type BuildLabState
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

const MODE_LABELS: Record<BuildLabMode, string> = {
  supported: "Best supported",
  impact: "Highest lift",
  common: "Most common"
};

const SECTION_LABELS: Record<BuildLabSection, string> = {
  items: "Items",
  runes: "Runes",
  spells: "Spells"
};

function entityName(id: number, section: BuildLabSection, lookups: Lookups) {
  if (section === "items") return lookups.items[String(id)]?.name ?? `Item ${id}`;
  if (section === "runes") return lookups.runes[String(id)]?.name ?? `Rune ${id}`;
  return lookups.spells[String(id)]?.name ?? `Spell ${id}`;
}

function entityIcon(id: number, section: BuildLabSection, lookups: Lookups) {
  if (section === "items") return itemIconUrl(lookups.itemVersion, id);
  if (section === "runes") return runeIconUrl(lookups.runes[String(id)]?.icon ?? "");
  return summonerSpellIconUrl(lookups.spellVersion, lookups.spells[String(id)]?.id ?? "");
}

/** "Doran's Ring + 2× Health Potion": a starter set repeats items, and the repeat is the point. */
function optionName(ids: number[], section: BuildLabSection, lookups: Lookups) {
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

function Icons({
  ids,
  section,
  lookups,
  size = 34
}: {
  ids: number[];
  section: BuildLabSection;
  lookups: Lookups;
  size?: number;
}) {
  return (
    <span className="flex shrink-0 -space-x-1.5">
      {/* Keyed by position: a starter set repeats ids (two Health Potions). */}
      {ids.slice(0, 4).map((id, index) => (
        <Image
          key={`${id}-${index}`}
          src={entityIcon(id, section, lookups)}
          alt=""
          width={size}
          height={size}
          style={{ width: size, height: size }}
          className={cn(
            "rounded-control border-2 border-surface bg-surface-2 object-cover",
            section === "runes" && "rounded-full p-0.5"
          )}
        />
      ))}
    </span>
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

function StageTable({
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
  if (stage.options.length === 0) {
    return <p className="px-4 py-8 text-sm text-muted">No choice here has enough games yet.</p>;
  }
  const terminal = isTerminalBuildLabFamily(stage.family);
  const referencePage = stage.options.reduce((best, option) =>
    option.games > best.games ? option : best
  ).actionIds;
  const conditions = stage.family === "ITEM" || (stage.family === "RUNE" && stage.stage === 1);

  // One dense table at every breakpoint (it scrolls inside its own container on small screens).
  return (
    <div className="overflow-x-auto">
      {/* Fixed layout and one column set for every family, so the stages stacked on the page line up. */}
      <table className="w-full min-w-[52rem] table-fixed text-left text-sm">
        <colgroup>
          <col />
          <col className="w-[10.5rem]" />
          <col className="w-[6.5rem]" />
          <col className="w-[8.5rem]" />
          <col className="w-[6.5rem]" />
          <col className="w-[5.5rem]" />
          <col className="w-[5.5rem]" />
          <col className="w-[5rem]" />
          <col className="w-[5.5rem]" />
        </colgroup>
        <caption className="sr-only">
          {stage.label}: gold-adjusted win rate, its 95% interval, raw win rate, pick rate and games
          for each choice.
        </caption>
        <thead>
          <tr className="border-b border-border/55 bg-surface-2/45 text-xs text-muted">
            <th scope="col" className="px-4 py-2.5 font-medium">Choice</th>
            <th scope="col" className="px-3 py-2.5 font-medium">Adjusted win rate</th>
            <th scope="col" className="px-3 py-2.5 text-right font-medium">vs. average</th>
            <th scope="col" className="px-3 py-2.5 text-right font-medium">95% interval</th>
            <th scope="col" className="px-3 py-2.5 text-right font-medium">Raw win rate</th>
            <th scope="col" className="px-3 py-2.5 text-right font-medium">Pick rate</th>
            <th scope="col" className="px-3 py-2.5 text-right font-medium">Games</th>
            <th scope="col" className="px-3 py-2.5 text-right font-medium">Timing</th>
            <th scope="col" className="px-4 py-2.5 text-right font-medium">
              <span className="sr-only">Select</span>
            </th>
          </tr>
        </thead>
        <tbody>
          {stage.options.map((option) => (
            <tr
              key={option.actionKey}
              className={cn(
                "border-b border-border/30 last:border-0 hover:bg-surface-2/30",
                option.isLowSample && "text-fg/70"
              )}
            >
              <td className="px-4 py-3">
                <div className="flex items-center gap-3">
                  <Icons ids={option.actionIds} section={section} lookups={lookups} />
                  <div className="min-w-0">
                    <p className="font-semibold text-fg">
                      {/* A rune page is named for its keystone; the rest of the page is what tells
                          two pages with the same keystone apart, so it is listed underneath. */}
                      {stage.family === "RUNE_PAGE"
                        ? entityName(option.actionIds[0], section, lookups)
                        : optionName(option.actionIds, section, lookups)}
                      {option.isLowSample ? (
                        <span className="ml-2 rounded-control border border-border/60 px-1.5 py-0.5 text-[0.6875rem] font-medium text-muted">
                          Few games
                        </span>
                      ) : null}
                    </p>
                    {stage.family === "RUNE_PAGE" && option.actionIds.length > 1 ? (
                      <p className="mt-0.5 truncate text-xs text-muted">
                        {runePageDetail(option.actionIds, referencePage, lookups)}
                      </p>
                    ) : null}
                  </div>
                </div>
              </td>
              <td className="px-3 py-3">
                {/* A thin sample's bar would read as confidently as a solid one; muting it keeps the
                    eye on the rows whose interval actually supports the number. */}
                <DataBar
                  value={option.adjustedWinRate}
                  className={option.isLowSample ? "opacity-45" : undefined}
                />
              </td>
              <td
                className={cn(
                  "px-3 py-3 text-right font-semibold tabular-nums",
                  liftToneClass(option.lift, option.isLowSample)
                )}
              >
                {formatLift(option.lift)}
              </td>
              <td className="px-3 py-3 text-right tabular-nums text-fg/72">
                {formatPercent(option.confidenceLow)} – {formatPercent(option.confidenceHigh)}
              </td>
              <td className="px-3 py-3 text-right tabular-nums text-fg/72">
                {formatPercent(option.winRate)}
              </td>
              <td className="px-3 py-3 text-right tabular-nums text-fg/72">
                {formatPercent(option.pickRate)}
              </td>
              <td className="px-3 py-3 text-right tabular-nums text-fg/72">
                {formatCompactCount(option.games)}
              </td>
              <td className="px-3 py-3 text-right tabular-nums text-fg/72">
                {option.averageTimingMinutes == null
                  ? "—"
                  : `${option.averageTimingMinutes.toFixed(1)}m`}
              </td>
              <td className="px-4 py-3 text-right">
                {terminal || conditions ? (
                  <Button size="sm" variant="outline" onClick={() => onSelect(stage, option)}>
                    {conditions ? "Lock" : "Pick"}
                  </Button>
                ) : null}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
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
  const [activeStage, setActiveStage] = useState(0);
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
        setActiveStage(0);
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
  const activeStageData = stages[Math.min(activeStage, Math.max(stages.length - 1, 0))];
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
    <div className="grid gap-5">
      <header className="flex flex-col gap-5 border-b border-border/60 pb-5 lg:flex-row lg:items-end lg:justify-between">
        <div className="flex min-w-0 items-center gap-4">
          <Image
            src={championIconUrl(version, championSlug)}
            alt=""
            width={64}
            height={64}
            className="size-14 rounded-card border border-border/60 sm:size-16"
          />
          <div className="min-w-0">
            <p className="type-kicker text-primary">Build Lab · Ranked Solo/Duo</p>
            <h1 className="type-page-title mt-1 truncate">{championName}</h1>
            <p className="mt-1 text-sm text-muted">
              Every build choice, with its win rate adjusted for the gold lead it was made with.
            </p>
          </div>
        </div>
        <Link
          href={`/lol/champions/${championId}?role=${state.role}`}
          className="text-sm font-medium text-fg/70 hover:text-fg"
        >
          Champion overview
        </Link>
      </header>

      <section aria-label="Build Lab context" className="grid gap-3 border-b border-border/50 pb-5">
        <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-5">
          <label className="grid gap-1.5">
            <span className="type-kicker text-muted">Champion</span>
            <Select
              value={String(championId)}
              options={championOptions}
              onValueChange={(value) => router.push(buildLabPermalink(Number(value), state))}
              ariaLabel="Champion"
              className="w-full"
            />
          </label>
          <label className="grid gap-1.5">
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
                  spellPair: []
                })
              }
              ariaLabel="Role"
              className="w-full"
            />
          </label>
          <label className="grid gap-1.5">
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
          <label className="grid gap-1.5">
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
          <label className="grid gap-1.5">
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
        <div className="flex flex-col gap-3 lg:flex-row lg:items-center lg:justify-between">
          <SegmentedControl
            value={state.section}
            onValueChange={(section) => updateState({ ...state, section })}
            options={BUILD_LAB_SECTIONS.map((section) => ({
              value: section,
              label: SECTION_LABELS[section]
            }))}
            ariaLabel="Analytics section"
            className="overflow-x-auto"
          />
          <SegmentedControl
            value={state.mode}
            onValueChange={(mode) => updateState({ ...state, mode })}
            options={BUILD_LAB_MODES.map((mode) => ({ value: mode, label: MODE_LABELS[mode] }))}
            ariaLabel="Ranking mode"
            className="overflow-x-auto"
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
          <>
            <div className="border-b border-border/45 px-3 py-2 md:hidden">
              <SegmentedControl
                value={String(Math.min(activeStage, Math.max(stages.length - 1, 0)))}
                onValueChange={(value) => setActiveStage(Number(value))}
                options={stages.map((stage, index) => ({ value: String(index), label: stage.label }))}
                ariaLabel="Decision stage"
                className="max-w-full overflow-x-auto"
              />
            </div>

            {[
              { list: stages, className: "hidden divide-y divide-border/50 md:block" },
              { list: activeStageData ? [activeStageData] : [], className: "md:hidden" }
            ].map(({ list, className }) => (
              <div key={className} className={className}>
                {list.map((stage) => {
                  const note = stageNote(stage, response);
                  return (
                    <section key={`${stage.family}-${stage.stage}`}>
                      <div className="flex flex-wrap items-center justify-between gap-2 px-4 py-3">
                        <h2 className="type-section">{stage.label}</h2>
                        <span className="flex flex-wrap items-center gap-2 text-xs text-muted">
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
                          {formatCompactCount(stage.games)} games · {formatPercent(stage.winRate)}{" "}
                          win rate
                        </span>
                      </div>
                      <StageTable
                        stage={stage}
                        section={state.section}
                        lookups={lookups}
                        onSelect={selectOption}
                      />
                    </section>
                  );
                })}
              </div>
            ))}
          </>
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
