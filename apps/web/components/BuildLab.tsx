"use client";

import Image from "next/image";
import Link from "next/link";
import { useEffect, useMemo, useState } from "react";
import { useRouter } from "next/navigation";

import { Button } from "@/components/ui/Button";
import { EmptyState } from "@/components/ui/EmptyState";
import { Input } from "@/components/ui/Input";
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
  buildLabSelectionGroups,
  clearBuildLabSelection,
  formatCompactCount,
  formatPercent,
  formatWpa,
  humanizeToken,
  isTerminalBuildLabFamily,
  savedRuneSelections,
  selectBuildLabCandidate,
  undoLastBuildLabSelection,
  bucketLabel,
  bucketToneClass,
  wpaToneClass,
  type AdjustedActionEstimate,
  type BuildLabMode,
  type BuildLabResponse,
  type BuildLabSection,
  type BuildLabState
} from "@/lib/buildLab";
import { cn } from "@/lib/cn";
import { rankTierDisplayLabel } from "@/lib/ranks";
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

const MODE_LABELS: Record<BuildLabMode, string> = {
  supported: "Best Supported",
  impact: "Highest Impact",
  common: "Most Common"
};

const SECTION_LABELS: Record<BuildLabSection, string> = {
  items: "Items",
  runes: "Runes",
  spells: "Spells"
};

function entityName(
  id: number,
  section: BuildLabSection,
  items: ItemLookup,
  runes: RuneLookup,
  spells: SpellLookup
) {
  if (section === "items") return items[String(id)]?.name ?? `Item ${id}`;
  if (section === "runes") return runes[String(id)]?.name ?? `Rune ${id}`;
  return spells[String(id)]?.name ?? `Spell ${id}`;
}

function entityIcon(
  id: number,
  section: BuildLabSection,
  itemVersion: string,
  spellVersion: string,
  runes: RuneLookup,
  spells: SpellLookup
) {
  if (section === "items") return itemIconUrl(itemVersion, id);
  if (section === "runes") return runeIconUrl(runes[String(id)]?.icon ?? "");
  return summonerSpellIconUrl(spellVersion, spells[String(id)]?.id ?? "");
}

function CandidateIcons({
  candidate,
  section,
  itemVersion,
  spellVersion,
  runes,
  spells
}: {
  candidate: AdjustedActionEstimate;
  section: BuildLabSection;
  itemVersion: string;
  spellVersion: string;
  runes: RuneLookup;
  spells: SpellLookup;
}) {
  return (
    <span className="flex shrink-0 -space-x-1.5">
      {candidate.actionIds.slice(0, 3).map((id) => (
        <Image
          key={id}
          src={entityIcon(id, section, itemVersion, spellVersion, runes, spells)}
          alt=""
          width={34}
          height={34}
          className={cn(
            "size-[34px] rounded-control border-2 border-surface bg-surface-2 object-cover",
            section === "runes" && "rounded-full p-0.5"
          )}
        />
      ))}
    </span>
  );
}

function SelectedPath({
  state,
  items,
  runes,
  spells,
  itemVersion,
  spellVersion,
  terminal,
  onUndo,
  onClear
}: {
  state: BuildLabState;
  items: ItemLookup;
  runes: RuneLookup;
  spells: SpellLookup;
  itemVersion: string;
  spellVersion: string;
  terminal: boolean;
  onUndo: () => void;
  onClear: () => void;
}) {
  const groups = buildLabSelectionGroups(state);
  if (groups.length === 0) return null;

  return (
    <div className="flex flex-wrap items-center gap-2 border-b border-border/45 px-4 py-3">
      <span className="type-kicker mr-1 text-muted">
        {terminal ? "Complete selection" : "Locked prefix"}
      </span>
      {groups.map((group, index) => (
        <span
          key={`${group.join("-")}-${index}`}
          className="inline-flex items-center gap-1.5 rounded-control border border-border/60 bg-surface-2 px-2 py-1 text-xs text-fg/80"
        >
          {group.map((id) => (
            <span key={id} className="inline-flex items-center gap-1.5">
              <Image
                src={entityIcon(id, state.section, itemVersion, spellVersion, runes, spells)}
                alt=""
                width={22}
                height={22}
                className="size-[22px] rounded"
              />
              {entityName(id, state.section, items, runes, spells)}
            </span>
          ))}
        </span>
      ))}
      <Button size="sm" variant="ghost" className="ml-auto" onClick={onUndo}>
        Undo last selection
      </Button>
      <Button size="sm" variant="ghost" onClick={onClear}>
        Clear
      </Button>
    </div>
  );
}

function EvidenceDetails({
  candidate,
  requestedRegion
}: {
  candidate: AdjustedActionEstimate;
  requestedRegion: string;
}) {
  return (
    <details className="mt-2 text-xs text-muted">
      <summary className="cursor-pointer font-medium text-fg/65">Evidence details</summary>
      <dl className="mt-2 grid gap-1 border-l border-border/60 pl-3">
        <div className="flex flex-wrap gap-x-1.5">
          <dt>Compared against:</dt>
          <dd className="text-fg/72">{candidate.baselineDefinition}</dd>
        </div>
        <div className="flex flex-wrap gap-x-1.5">
          <dt>Raw observed win rate:</dt>
          <dd className="tabular-nums text-fg/72">{formatPercent(candidate.rawWinRate)}</dd>
        </div>
        <div className="flex flex-wrap gap-x-1.5">
          <dt>Observed games / effective sample:</dt>
          <dd className="tabular-nums text-fg/72">
            {formatCompactCount(candidate.observedCount)} /{" "}
            {formatCompactCount(candidate.effectiveSampleSize)}
          </dd>
        </div>
        <div className="flex flex-wrap gap-x-1.5">
          <dt>Evidence quality:</dt>
          <dd className="text-fg/72">{humanizeToken(candidate.evidenceQuality)}</dd>
        </div>
        <div className="flex flex-wrap gap-x-1.5">
          <dt>Estimated in:</dt>
          <dd className="text-fg/72">
            {buildLabRegionLabel(candidate.regionScope)}
            {candidate.fallbackScope === "GLOBAL_FALLBACK" && requestedRegion !== "GLOBAL"
              ? ` (no publishable ${requestedRegion} cell for this choice)`
              : ""}
          </dd>
        </div>
        {candidate.unavailableReason ? (
          <div className="flex flex-wrap gap-x-1.5">
            <dt className="sr-only">Withheld because:</dt>
            <dd>{candidate.unavailableReason}</dd>
          </div>
        ) : null}
      </dl>
    </details>
  );
}

function StageTable({
  stage,
  candidates,
  state,
  requestedRegion,
  items,
  runes,
  spells,
  itemVersion,
  spellVersion,
  onSelect
}: {
  stage: { family: string; label: string };
  candidates: AdjustedActionEstimate[];
  state: BuildLabState;
  requestedRegion: string;
  items: ItemLookup;
  runes: RuneLookup;
  spells: SpellLookup;
  itemVersion: string;
  spellVersion: string;
  onSelect: (family: string, candidate: AdjustedActionEstimate) => void;
}) {
  if (candidates.length === 0) {
    return (
      <p className="px-4 py-8 text-sm text-muted">No candidate choices passed into this stage.</p>
    );
  }

  const terminal = isTerminalBuildLabFamily(stage.family);

  // One dense table at every breakpoint (it scrolls inside its own container on small screens) —
  // compressed cards used to drop the average purchase timing entirely.
  return (
    <div className="overflow-x-auto">
      <table className="w-full min-w-[46rem] text-left text-sm">
        <caption className="sr-only">
          {stage.label}: estimated lift for each modeled choice, with its 95% interval and sample.
        </caption>
        <thead>
          <tr className="border-b border-border/55 bg-surface-2/45 text-xs text-muted">
            <th scope="col" className="px-4 py-2.5 font-medium">Choice</th>
            <th scope="col" className="px-3 py-2.5 text-right font-medium">Adjusted WPA</th>
            <th scope="col" className="px-3 py-2.5 text-right font-medium">95% interval</th>
            <th scope="col" className="px-3 py-2.5 text-right font-medium">Observed / ESS</th>
            <th scope="col" className="px-3 py-2.5 text-right font-medium">Pick rate</th>
            <th scope="col" className="px-3 py-2.5 text-right font-medium">Timing</th>
            <th scope="col" className="px-4 py-2.5 text-right font-medium">
              <span className="sr-only">Select</span>
            </th>
          </tr>
        </thead>
        <tbody>
          {candidates.map((candidate) => (
            <tr
              key={candidate.actionKey}
              className="border-b border-border/30 last:border-0 hover:bg-surface-2/30"
            >
              <td className="px-4 py-3">
                <div className="flex items-center gap-3">
                  <CandidateIcons
                    candidate={candidate}
                    section={state.section}
                    itemVersion={itemVersion}
                    spellVersion={spellVersion}
                    runes={runes}
                    spells={spells}
                  />
                  <div className="min-w-0">
                    <p className="flex flex-wrap items-center gap-x-2 gap-y-1 font-semibold text-fg">
                      {candidate.actionIds
                        .map((id) => entityName(id, state.section, items, runes, spells))
                        .join(" + ")}
                      {candidate.fallbackScope === "GLOBAL_FALLBACK" &&
                      requestedRegion !== "GLOBAL" ? (
                        <span className="rounded-control border border-warning/35 bg-warning/10 px-1.5 py-0.5 text-[0.6875rem] font-medium text-warning">
                          Global cell
                        </span>
                      ) : null}
                      {terminal ? (
                        <span className="rounded-control border border-border/60 px-1.5 py-0.5 text-[0.6875rem] font-medium text-muted">
                          Complete selection
                        </span>
                      ) : null}
                    </p>
                    <EvidenceDetails candidate={candidate} requestedRegion={requestedRegion} />
                  </div>
                </div>
              </td>
              <td
                className={cn(
                  "px-3 py-3 text-right font-semibold tabular-nums",
                  candidate.isPublishable
                    ? wpaToneClass(candidate.adjustedWpa)
                    : candidate.evidenceTier === "BUCKETED"
                      ? bucketToneClass(candidate.evidenceBucket)
                      : "text-muted"
                )}
              >
                {/* A bucketed cell states the direction its posterior supports and withholds the
                    number its interval cannot. Only a descriptive cell says nothing. */}
                {candidate.isPublishable
                  ? formatWpa(candidate.adjustedWpa)
                  : candidate.evidenceTier === "BUCKETED"
                    ? bucketLabel(candidate.evidenceBucket)
                    : "Insufficient evidence"}
              </td>
              <td className="px-3 py-3 text-right tabular-nums text-fg/72">
                {candidate.isPublishable
                  ? `${formatWpa(candidate.confidenceLow)} to ${formatWpa(candidate.confidenceHigh)}`
                  : candidate.evidenceTier === "BUCKETED"
                    ? "Direction only"
                    : "—"}
              </td>
              <td className="px-3 py-3 text-right tabular-nums text-fg/72">
                {formatCompactCount(candidate.observedCount)} /{" "}
                {formatCompactCount(candidate.effectiveSampleSize)}
              </td>
              <td className="px-3 py-3 text-right tabular-nums text-fg/72">
                {formatPercent(candidate.pickRate)}
              </td>
              <td className="px-3 py-3 text-right tabular-nums text-fg/72">
                {candidate.averageTimingMinutes == null
                  ? "—"
                  : `${candidate.averageTimingMinutes.toFixed(1)}m`}
              </td>
              <td className="px-4 py-3 text-right">
                <Button
                  size="sm"
                  variant="outline"
                  onClick={() => onSelect(stage.family, candidate)}
                >
                  {terminal ? "Select" : "Lock"}
                </Button>
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
  const [saveName, setSaveName] = useState("");
  const [saveStatus, setSaveStatus] = useState<string | null>(null);
  const [saveLimitReached, setSaveLimitReached] = useState(false);
  const [savedBuildId, setSavedBuildId] = useState<string | null>(null);
  const [shareId, setShareId] = useState<string | null>(null);

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
    () => buildLabRegionOptions(response.provenance.includedRegions, state.region),
    [response.provenance.includedRegions, state.region]
  );

  useEffect(() => {
    const controller = new AbortController();
    const query = buildLabRequestQuery(state);
    setLoading(true);
    setRequestError(null);
    fetch(`/api/trn/public/lol/analytics/build-lab/${championId}?${query.toString()}`, {
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
  }, [championId, state]);

  // Incremental selection uses replace: locking six items must not bury the previous page under
  // six history entries. Only the champion switch (a different route) pushes.
  function updateState(next: BuildLabState) {
    setState(next);
    setSelectionError(null);
    setLinkIssues([]);
    router.replace(buildLabPermalink(championId, next), { scroll: false });
  }

  function selectCandidate(family: string, candidate: AdjustedActionEstimate) {
    const result = selectBuildLabCandidate(state, family, candidate.actionIds);
    if (result.error) {
      setSelectionError(result.error);
      return;
    }
    updateState(result.state);
  }

  async function saveBuild() {
    if (!saveName.trim()) {
      setSaveStatus("Name this build first.");
      return;
    }
    setSaveStatus("Saving…");
    const result = await fetch("/api/trn/user/users/me/lol/saved-builds", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        name: saveName.trim(),
        championId,
        role: state.role,
        opponentChampionId: state.opponentChampionId ?? null,
        patch: state.patch ?? response.context.effectivePatch,
        region: state.region ?? "GLOBAL",
        rankingMode: state.mode,
        itemPath: state.itemPath,
        runeSelections: savedRuneSelections(state),
        spell1Id: state.spellPair[0] ?? null,
        spell2Id: state.spellPair[1] ?? null
      })
    });
    if (result.status === 401) {
      setSaveStatus("Sign in to save this build.");
      return;
    }
    if (!result.ok) {
      const detail = await problemDetail(result);
      if (result.status === 409) {
        setSaveLimitReached(true);
        setSaveStatus(
          detail ?? "You have reached the saved-build limit. Delete one before saving another."
        );
        return;
      }
      setSaveStatus(detail ?? "This build could not be saved.");
      return;
    }
    setSaveLimitReached(false);
    const saved = (await result.json()) as { id: string };
    setSavedBuildId(saved.id);
    setSaveStatus("Saved privately.");
  }

  async function shareBuild() {
    if (!savedBuildId) return;
    const result = await fetch(`/api/trn/user/users/me/lol/saved-builds/${savedBuildId}/share`, {
      method: "POST"
    });
    if (!result.ok) {
      setSaveStatus((await problemDetail(result)) ?? "The share link could not be created.");
      return;
    }
    const shared = (await result.json()) as { shareId: string };
    setShareId(shared.shareId);
    const url = `${window.location.origin}/lol/builds/shared/${shared.shareId}`;
    await navigator.clipboard?.writeText(url);
    setSaveStatus("Read-only share link copied.");
  }

  const stages = response.stages;
  const activeStageData = stages[Math.min(activeStage, Math.max(stages.length - 1, 0))];
  const requestedRegion = response.context.requestedRegion || "GLOBAL";
  const selectionGroups = buildLabSelectionGroups(state);
  const terminalSelection =
    state.section === "spells"
      ? state.spellPair.length > 0
      : state.section === "runes" && (state.runePage?.length ?? 0) > 0;
  const patchBorrowed =
    Boolean(response.context.requestedPatch) &&
    Boolean(response.context.effectivePatch) &&
    response.context.requestedPatch !== response.context.effectivePatch;
  const patchOptions = [
    {
      value: "current",
      label: `Current${response.context.effectivePatch ? ` · ${response.context.effectivePatch}` : ""}`
    },
    ...response.provenance.includedPatches.map((patch) => ({ value: patch, label: patch }))
  ].filter(
    (option, index, options) =>
      options.findIndex((candidate) => candidate.value === option.value) === index
  );
  const rankScopeLabel = rankTierDisplayLabel(response.provenance.rankScope || "EMERALD_PLUS");

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
            <p className="type-kicker text-primary">Build Lab · {rankScopeLabel} Solo/Duo</p>
            <h1 className="type-page-title mt-1 truncate">{championName}</h1>
            <p className="mt-1 text-sm text-muted">
              Estimated lift versus realistic alternatives in comparable decisions.
            </p>
          </div>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <Link href={`/lol/champions/${championId}?role=${state.role}`} className="text-sm font-medium text-fg/70 hover:text-fg">
            Champion overview
          </Link>
          <Link href="/account/saved-builds" className="text-sm font-medium text-fg/70 hover:text-fg">
            Saved builds
          </Link>
        </div>
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
              options={BUILD_LAB_ROLES.map((role) => ({ value: role, label: role === "UTILITY" ? "Support" : role[0] + role.slice(1).toLowerCase() }))}
              onValueChange={(role) => updateState({ ...state, role: role as BuildLabState["role"], itemPath: [], itemLocks: [], runeSelections: [], runePage: [], spellPair: [] })}
              ariaLabel="Role"
              className="w-full"
            />
          </label>
          <label className="grid gap-1.5">
            <span className="type-kicker text-muted">Lane opponent</span>
            <Select
              value={state.opponentChampionId ? String(state.opponentChampionId) : "none"}
              options={opponentOptions}
              onValueChange={(value) => updateState({ ...state, opponentChampionId: value === "none" ? undefined : Number(value) })}
              ariaLabel="Lane opponent"
              className="w-full"
            />
          </label>
          <label className="grid gap-1.5">
            <span className="type-kicker text-muted">Region</span>
            <Select
              value={state.region ?? "GLOBAL"}
              options={regionOptions}
              onValueChange={(region) => updateState({ ...state, region })}
              ariaLabel="Region"
              className="w-full"
            />
          </label>
          <label className="grid gap-1.5">
            <span className="type-kicker text-muted">Patch</span>
            <Select
              value={state.patch ?? "current"}
              options={patchOptions}
              onValueChange={(patch) => updateState({ ...state, patch: patch === "current" ? undefined : patch })}
              ariaLabel="Patch"
              className="w-full"
            />
          </label>
        </div>
        <div className="flex flex-col gap-3 lg:flex-row lg:items-center lg:justify-between">
          <SegmentedControl
            value={state.section}
            onValueChange={(section) => updateState({ ...state, section })}
            options={BUILD_LAB_SECTIONS.map((section) => ({ value: section, label: SECTION_LABELS[section] }))}
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
            {loading ? "Recalculating…" : buildLabRegionLabel(response.context.effectiveRegion)}
          </span>
          <span className="text-xs text-muted">
            Patch {response.context.effectivePatch || "pending"}
          </span>
          <span className="text-xs text-muted">{rankScopeLabel} Ranked Solo/Duo</span>
          {response.provenance.sourceCutoffUtc ? (
            <span className="text-xs text-muted">
              Data through {new Date(response.provenance.sourceCutoffUtc).toLocaleDateString()}
            </span>
          ) : null}
          {patchBorrowed ? (
            <span className="rounded-control border border-border/60 bg-surface-2 px-2 py-1 text-xs text-fg/75">
              Patch {response.context.requestedPatch} requested · modeled inside the{" "}
              {response.context.effectivePatch} generation
            </span>
          ) : null}
          {requestedRegion !== "GLOBAL" && response.context.effectiveRegion === "GLOBAL" ? (
            <span className="rounded-control border border-warning/35 bg-warning/10 px-2 py-1 text-xs text-warning">
              No {requestedRegion} cell passed the gates; showing the global baseline
            </span>
          ) : null}
        </div>

        {linkIssues.length > 0 ? (
          <div className="border-b border-border/45 bg-warning/10 px-4 py-2.5 text-xs text-warning" role="status">
            {linkIssues.map((issue) => (
              <p key={issue}>{issue}</p>
            ))}
          </div>
        ) : null}

        <SelectedPath
          state={state}
          items={items}
          runes={runes}
          spells={spells}
          itemVersion={itemVersion}
          spellVersion={spellVersion}
          terminal={terminalSelection}
          onUndo={() => updateState(undoLastBuildLabSelection(state))}
          onClear={() => updateState(clearBuildLabSelection(state))}
        />

        {terminalSelection ? (
          <p className="border-b border-border/45 bg-surface-2/25 px-4 py-2.5 text-xs text-muted">
            This is a complete selection: the model does not condition any further decision on it,
            so the stages below stay at their published scope.
          </p>
        ) : null}

        {selectionError ? (
          <p
            className="border-b border-border/45 bg-warning/10 px-4 py-2.5 text-xs text-warning"
            role="alert"
          >
            {selectionError}
          </p>
        ) : null}

        {response.pathEstimate ? (
          <div className="grid gap-3 border-b border-border/45 bg-surface-2/30 px-4 py-4 sm:grid-cols-4">
            <div>
              <p className="type-kicker text-muted">Complete path lift</p>
              <p
                className={cn(
                  "mt-1 text-xl font-semibold tabular-nums",
                  response.pathEstimate.isPublishable
                    ? wpaToneClass(response.pathEstimate.adjustedLift)
                    : "text-muted"
                )}
              >
                {response.pathEstimate.isPublishable
                  ? formatWpa(response.pathEstimate.adjustedLift)
                  : "Insufficient evidence"}
              </p>
            </div>
            <div>
              <p className="type-kicker text-muted">Estimated win probability</p>
              <p className="mt-1 text-lg font-semibold tabular-nums text-fg">{formatPercent(response.pathEstimate.estimatedWinProbability)}</p>
            </div>
            <div>
              <p className="type-kicker text-muted">95% interval</p>
              <p className="mt-1 text-sm tabular-nums text-fg/78">{formatWpa(response.pathEstimate.confidenceLow)} to {formatWpa(response.pathEstimate.confidenceHigh)}</p>
            </div>
            <div>
              <p className="type-kicker text-muted">Observed / ESS</p>
              <p className="mt-1 text-sm tabular-nums text-fg/78">{formatCompactCount(response.pathEstimate.observedCount)} / {formatCompactCount(response.pathEstimate.effectiveSampleSize)}</p>
            </div>
          </div>
        ) : null}

        {requestError ? (
          <EmptyState title="Build Lab could not recalculate" description={requestError} className="m-4" />
        ) : !response.available && stages.length === 0 && selectionGroups.length > 0 ? (
          <EmptyState
            title="No modeled stage for this exact selection"
            description="The promoted generation holds no conditioned decision for this precise prefix. Undo the last selection to return to a modeled stage."
            action={
              <Button
                size="sm"
                variant="outline"
                onClick={() => updateState(undoLastBuildLabSelection(state))}
              >
                Undo last selection
              </Button>
            }
            className="m-4"
          />
        ) : !response.available ? (
          <EmptyState
            title="Insufficient evidence"
            description={response.unavailableReason ?? "This champion-role context is still shadow-running and has not passed the publication gates."}
            action={
              <Button
                size="sm"
                variant="outline"
                onClick={() =>
                  updateState({
                    ...state,
                    opponentChampionId: undefined,
                    itemPath: [],
                    itemLocks: [],
                    runeSelections: [],
                    runePage: [],
                    spellPair: []
                  })
                }
              >
                Try the broader champion-role context
              </Button>
            }
            className="m-4"
          />
        ) : (
          <>
            {state.section === "items" && state.itemPath.length === 0 ? (
              <p className="border-b border-border/45 bg-surface-2/25 px-4 py-2.5 text-xs text-muted">
                Stages unlock as you lock a choice: starting sets and first-item paths are modeled
                from an empty board, boots and later legendary slots only exist conditioned on what
                came before.
              </p>
            ) : null}

            <div className="border-b border-border/45 px-3 py-2 md:hidden">
              <SegmentedControl
                value={String(Math.min(activeStage, Math.max(stages.length - 1, 0)))}
                onValueChange={(value) => setActiveStage(Number(value))}
                options={stages.map((stage, index) => ({
                  value: String(index),
                  label: stage.label
                }))}
                ariaLabel="Decision stage"
                className="max-w-full overflow-x-auto"
              />
            </div>

            <div className="hidden divide-y divide-border/50 md:block">
              {stages.map((stage) => (
                <section key={`${stage.family}-${stage.stage}`}>
                  <div className="flex items-center justify-between px-4 py-3">
                    <h2 className="type-section">{stage.label}</h2>
                    <span className="text-xs text-muted">{stage.candidates.length} evaluated choices</span>
                  </div>
                  <StageTable
                    stage={stage}
                    candidates={stage.candidates}
                    state={state}
                    requestedRegion={requestedRegion}
                    items={items}
                    runes={runes}
                    spells={spells}
                    itemVersion={itemVersion}
                    spellVersion={spellVersion}
                    onSelect={selectCandidate}
                  />
                </section>
              ))}
            </div>

            {activeStageData ? (
              <section className="md:hidden">
                <div className="flex items-center justify-between px-4 py-3">
                  <h2 className="type-section">{activeStageData.label}</h2>
                  <span className="text-xs text-muted">
                    {activeStageData.candidates.length} evaluated choices
                  </span>
                </div>
                <StageTable
                  stage={activeStageData}
                  candidates={activeStageData.candidates}
                  state={state}
                  requestedRegion={requestedRegion}
                  items={items}
                  runes={runes}
                  spells={spells}
                  itemVersion={itemVersion}
                  spellVersion={spellVersion}
                  onSelect={selectCandidate}
                />
              </section>
            ) : null}
          </>
        )}
      </section>

      <section className="grid gap-4 border-t border-border/55 pt-5 lg:grid-cols-[1fr_auto] lg:items-end">
        <div>
          <p className="type-kicker text-muted">Save this configuration</p>
          <div className="mt-2 flex max-w-2xl flex-col gap-2 sm:flex-row">
            <Input
              value={saveName}
              onChange={(event) => setSaveName(event.target.value)}
              placeholder={`${championName} ${state.role.toLowerCase()} build`}
              aria-label="Saved build name"
            />
            <Button onClick={saveBuild}>Save privately</Button>
            {savedBuildId ? <Button variant="outline" onClick={shareBuild}>Share read-only</Button> : null}
          </div>
          {saveStatus ? (
            <p className="mt-2 text-xs text-muted" role="status">
              {saveStatus}{" "}
              {saveStatus.includes("Sign in") ? (
                <Link href="/account/login" className="font-semibold text-primary">Sign in</Link>
              ) : null}
              {saveLimitReached ? (
                <Link href="/account/saved-builds" className="font-semibold text-primary">
                  Manage saved builds
                </Link>
              ) : null}
            </p>
          ) : null}
          {shareId ? (
            <Link href={`/lol/builds/shared/${shareId}`} className="mt-2 block break-all text-xs font-medium text-primary">
              /lol/builds/shared/{shareId}
            </Link>
          ) : null}
        </div>
        <p className="max-w-xl text-xs leading-5 text-muted lg:text-right">
          Adjusted WPA is an estimate, not a guarantee or player score. Individual action values are
          not added together; selected item paths are re-estimated as a complete conditioned path.
        </p>
      </section>
    </div>
  );
}
