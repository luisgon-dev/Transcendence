"use client";

import Image from "next/image";
import Link from "next/link";
import { useEffect, useMemo, useState } from "react";
import { useRouter } from "next/navigation";

import { BuildSummary, RecommendedBuild, StageList, stageNote, type Lookups, type ItemLookup, type RuneLookup, type SpellLookup } from "@/components/BuildLabResults";
import { Button } from "@/components/ui/Button";
import { EmptyState } from "@/components/ui/EmptyState";
import { SegmentedControl } from "@/components/ui/SegmentedControl";
import { Select } from "@/components/ui/Select";
import {
  BUILD_LAB_MODES,
  BUILD_LAB_ROLES,
  BUILD_LAB_SECTIONS,
  formatCompactCount,
  formatPercent,
  buildLabPermalink,
  buildLabRegionLabel,
  buildLabRegionOptions,
  buildLabRequestQuery,
  clearBuildLabSelection,
  hasBuildLabSelection,
  selectBuildLabOption,
  undoLastBuildLabSelection,
  type BuildLabMode,
  type BuildLabOption,
  type BuildLabResponse,
  type BuildLabSection,
  type BuildLabStage,
  type BuildLabState,
} from "@/lib/buildLab";
import { cn } from "@/lib/cn";
import {
  championIconUrl
} from "@/lib/staticData";

type ChampionOption = { championId: number; slug: string; name: string };
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
            className="grid w-full grid-cols-4 md:inline-flex md:w-auto"
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
                ? `Patches ${coverage.includedPatches.join(", ")} · older games count less only where the patch changed them`
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
