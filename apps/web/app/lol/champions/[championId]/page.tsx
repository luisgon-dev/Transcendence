import Image from "next/image";
import Link from "next/link";
import { cache, Suspense } from "react";
import type { components } from "@transcendence/api-client";

import { BackendErrorCard } from "@/components/BackendErrorCard";
import { AnalyticsSampleBanner } from "@/components/AnalyticsSampleBanner";
import { BuildBreakdown } from "@/components/BuildBreakdown";
import { FilterBar } from "@/components/FilterBar";
import { MatchupsTable, type MatchupRow } from "@/components/MatchupsTable";
import { ItemBuildDisplay } from "@/components/ItemBuildDisplay";
import { RuneTreeDisplay } from "@/components/RuneTreeDisplay";
import { StatsBar } from "@/components/StatsBar";
import { TierStoryBadge } from "@/components/TierStoryBadge";
import { UpdatedAgo } from "@/components/UpdatedAgo";
import { WinRateText } from "@/components/WinRateText";
import { Card } from "@/components/ui/Card";
import { DataBar } from "@/components/ui/DataBar";
import { Skeleton } from "@/components/ui/Skeleton";
import { fetchBackendJson } from "@/lib/backendCall";
import { resolveAnalyticsRegion } from "@/lib/analyticsRegions";
import { pickMostSevereAnalyticsSample, type AnalyticsSampleLike } from "@/lib/analyticsSample";
import { getBackendBaseUrl, getErrorVerbosity } from "@/lib/env";
import { formatGames } from "@/lib/format";
import { fetchLolAnalyticsPatches } from "@/lib/lolAnalyticsPatches";
import { normalizeAnalyticsPatch } from "@/lib/lolPatchFilters";
import { resolveDefaultedRankTier, rankTierDisplayLabel, rankTierLadderOrdinal } from "@/lib/ranks";
import { roleDisplayLabel } from "@/lib/roles";
import {
  championIconUrl,
  fetchChampionMap,
  fetchItemMap,
  fetchRunesReforged,
  fetchSummonerSpellMap
} from "@/lib/staticData";
import { formatEbStory } from "@/lib/confidence";
import { decodeGrade, formatStrengthDelta } from "@/lib/tierlist";

type ChampionWinRateDto = components["schemas"]["ChampionWinRateDto"];
// `computedAtUtc` is the ISO-8601 UTC timestamp (or null) of when the precomputed
// analytics for the patch were last refreshed; powers the "Updated N min ago" label.
type ChampionWinRateSummary = components["schemas"]["ChampionWinRateSummary"] & {
  computedAtUtc?: string | null;
};
type ChampionProfileAnalyticsResponse = components["schemas"]["ChampionProfileAnalyticsResponse"];
type MatchupEntryDto = components["schemas"]["MatchupEntryDto"];

type ChampionSearchParams = { role?: string; rankTier?: string; region?: string; patch?: string; sort?: string };

const ROLES = ["TOP", "JUNGLE", "MIDDLE", "BOTTOM", "UTILITY"] as const;

function matchupVerdict(winRate: number | null | undefined): string {
  const pct = (winRate ?? 0) * 100;
  if (pct >= 52) return "Favored";
  if (pct < 48) return "Unfavored";
  return "Even";
}

function normalizeRole(role: string | null | undefined) {
  if (!role) return null;
  const upper = role.toUpperCase();
  return ROLES.includes(upper as (typeof ROLES)[number]) ? upper : null;
}

function pickMostPlayedRole(summary: ChampionWinRateSummary | null) {
  if (!summary?.byRoleTier?.length) return null;
  const gamesByRole = new Map<string, number>();
  for (const entry of summary.byRoleTier ?? []) {
    if (!entry.role) continue;
    const role = entry.role.toUpperCase();
    gamesByRole.set(role, (gamesByRole.get(role) ?? 0) + (entry.games ?? 0));
  }
  const sorted = [...gamesByRole.entries()].sort((a, b) => b[1] - a[1]);
  if (sorted.length === 0) return null;
  const candidate = sorted[0][0];
  return normalizeRole(candidate);
}

function pickBestEntry(
  winrates: ChampionWinRateSummary | null,
  role: string
): ChampionWinRateDto | null {
  if (!winrates?.byRoleTier?.length) return null;
  const forRole = (winrates.byRoleTier ?? []).filter(
    (e) => (e.role ?? "").toUpperCase() === role.toUpperCase()
  );
  if (forRole.length === 0) return null;
  return forRole.reduce((best, cur) => ((cur.games ?? 0) > (best.games ?? 0) ? cur : best));
}

/**
 * Loads the champion profile + the static maps the data sections need. Wrapped in cache()
 * so the two streamed regions (hero meta + sections) below share a single profile fetch and
 * a single static-data transform within one render instead of fetching/parsing twice.
 */
const loadChampionData = cache(async (championId: number, sp: ChampionSearchParams) => {
  const { activeRegion, activeRegionLabel, options: regionOptions } =
    await resolveAnalyticsRegion(sp.region);

  const explicitRole = normalizeRole(sp.role);
  // Champion pages default to Emerald+ unless a rank is in the URL; an explicit
  // ?rankTier=all resolves to null (all ranks). See resolveDefaultedRankTier.
  const normalizedRankTier = resolveDefaultedRankTier(sp.rankTier);
  const selectedPatch = normalizeAnalyticsPatch(sp.patch);

  const winrateQuery = new URLSearchParams();
  if (normalizedRankTier) winrateQuery.set("rankTier", normalizedRankTier);
  if (activeRegion !== "ALL") winrateQuery.set("region", activeRegion);
  if (selectedPatch) winrateQuery.set("patch", selectedPatch);
  if (explicitRole) winrateQuery.set("role", explicitRole);
  const profileQuery = winrateQuery.toString() ? `?${winrateQuery.toString()}` : "";

  const [itemStatic, runeStatic, spellStatic, patchOptions, profileRes] = await Promise.all([
    fetchItemMap(),
    fetchRunesReforged(),
    fetchSummonerSpellMap(),
    fetchLolAnalyticsPatches(),
    fetchBackendJson<ChampionProfileAnalyticsResponse>(
      `${getBackendBaseUrl()}/api/lol/analytics/champions/${championId}/profile${profileQuery}`,
      { next: { revalidate: 60 * 60 } }
    )
  ]);

  return {
    activeRegion,
    activeRegionLabel,
    regionOptions,
    explicitRole,
    normalizedRankTier,
    selectedPatch,
    itemStatic,
    runeStatic,
    spellStatic,
    patchOptions,
    profileRes
  };
});

export default async function ChampionDetailPage({
  params,
  searchParams
}: {
  params: Promise<{ championId: string }>;
  searchParams?: Promise<ChampionSearchParams>;
}) {
  const resolvedParams = await params;
  const resolvedSearchParams = (searchParams ? await searchParams : undefined) ?? {};
  const championId = Number(resolvedParams.championId);
  if (!Number.isFinite(championId) || championId <= 0) {
    return (
      <BackendErrorCard
        title="Champion"
        message="Invalid champion id."
      />
    );
  }

  // Identity (cached static data) — paints the shell immediately while the profile streams in.
  const { version, champions } = await fetchChampionMap();
  const champ = champions[String(championId)];
  const champName = champ?.name ?? `Champion ${championId}`;
  const champSlug = champ?.id ?? "Unknown";
  const splashUrl = `https://ddragon.leagueoflegends.com/cdn/img/champion/splash/${champSlug}_0.jpg`;

  return (
    <div className="grid gap-8">
      {/* ── Champion Header (identity is instant; meta streams) ── */}
      <header className="page-hero relative p-5 md:p-8">
        <div
          className="pointer-events-none absolute inset-0 opacity-30"
          style={{
            backgroundImage: `linear-gradient(to right, var(--t-bg) 20%, color-mix(in oklch, var(--t-bg), transparent 18%) 45%, transparent 100%), url(${splashUrl})`,
            backgroundSize: "cover",
            backgroundPosition: "top right"
          }}
        />

        <div className="relative flex flex-col gap-5">
          <div className="flex items-start gap-3 sm:items-center sm:gap-4">
            <Image
              src={championIconUrl(version, champSlug)}
              alt={champName}
              width={64}
              height={64}
              className="h-12 w-12 rounded-xl border border-border/60 sm:h-16 sm:w-16"
            />
            <div className="min-w-0">
              <h1 className="type-page-title">{champName}</h1>
              {champ?.title ? (
                <p className="mt-0.5 text-xs uppercase tracking-wide text-muted">{champ.title}</p>
              ) : null}
            </div>
          </div>

          <Suspense fallback={<ChampionHeroMetaSkeleton />}>
            <ChampionHeroMeta championId={championId} searchParams={resolvedSearchParams} />
          </Suspense>
        </div>
      </header>

      <Suspense fallback={<ChampionSectionsSkeleton />}>
        <ChampionSections
          championId={championId}
          champName={champName}
          version={version}
          champions={champions}
          searchParams={resolvedSearchParams}
        />
      </Suspense>
    </div>
  );
}

// ── Streamed: tier badge + meta line + stats + filters ──────────────────────
async function ChampionHeroMeta({
  championId,
  searchParams
}: {
  championId: number;
  searchParams: ChampionSearchParams;
}) {
  const data = await loadChampionData(championId, searchParams);
  const {
    activeRegion,
    activeRegionLabel,
    regionOptions,
    explicitRole,
    normalizedRankTier,
    selectedPatch,
    patchOptions,
    profileRes
  } = data;

  if (!profileRes.ok) {
    // The full error renders in the sections region; keep the hero meta quiet on failure.
    return null;
  }

  const profile = profileRes.body!;
  const winrates = profile.winRates ?? null;
  const builds = profile.builds ?? null;
  const matchups = profile.matchups ?? null;
  const effectiveRole =
    normalizeRole(profile.effectiveRole) ?? explicitRole ?? pickMostPlayedRole(winrates) ?? "MIDDLE";
  const heroEntry = pickBestEntry(winrates, effectiveRole);
  // The hero grade is the SAME grade the tier list shows (read off the profile payload), not a locally
  // re-derived one — so a champion reads identically on the list and on its own page.
  const heroGrade = decodeGrade(profile.grade);
  const heroTier = heroGrade?.tier ?? null;
  const sampleNotice = pickMostSevereAnalyticsSample(
    (winrates as { sample?: unknown } | null)?.sample as AnalyticsSampleLike,
    (builds as { sample?: unknown } | null)?.sample as AnalyticsSampleLike,
    (matchups as { sample?: unknown } | null)?.sample as AnalyticsSampleLike
  );
  const linkParams = new URLSearchParams();
  if (normalizedRankTier) linkParams.set("rankTier", normalizedRankTier);
  if (activeRegion !== "ALL") linkParams.set("region", activeRegion);
  if (selectedPatch) linkParams.set("patch", selectedPatch);
  const linkQuery = linkParams.toString();
  const sharedFilterParams = selectedPatch ? { patch: selectedPatch } : {};

  return (
    <>
      <div className="flex flex-wrap items-center gap-2">
        {heroTier ? (
          <TierStoryBadge
            tier={heroTier}
            story={
              heroGrade
                ? formatEbStory({
                    winRate: heroGrade.winRate,
                    roleBaseline: heroGrade.roleBaseline,
                    strengthScore: heroGrade.strengthScore,
                    games: heroGrade.games
                  })
                : null
            }
            delta={heroGrade ? formatStrengthDelta(heroGrade.strengthScore) : null}
            lowSample={heroGrade?.isLowSample}
          />
        ) : (
          <span className="rounded-control border border-border/60 px-2 py-1 text-xs font-medium text-muted">
            Unrated
          </span>
        )}
        <p className="text-sm text-muted">
          {roleDisplayLabel(effectiveRole)} &middot; {rankTierDisplayLabel(normalizedRankTier ?? "all")} &middot; {activeRegionLabel}
        </p>
        {(winrates as ChampionWinRateSummary | null)?.computedAtUtc ? (
          <UpdatedAgo
            className="text-xs"
            timestamp={(winrates as ChampionWinRateSummary | null)?.computedAtUtc}
          />
        ) : null}
      </div>
      <div className="-mt-2 flex flex-wrap items-center gap-2 text-xs">
        <Link
          href="#matchups"
          className="rounded-lg border border-border/60 bg-surface-2/50 px-2.5 py-1 font-medium text-fg/80 transition-colors hover:bg-surface-2/80"
        >
          Matchups
        </Link>
        <Link
          href={`/lol/pro-builds/${championId}${linkQuery ? `?${linkQuery}` : ""}`}
          className="rounded-lg border border-primary/40 bg-primary/10 px-2.5 py-1 font-medium text-primary transition-colors hover:bg-primary/20"
        >
          Pro Builds
        </Link>
      </div>

      {/* ── Stats Bar ── */}
      <StatsBar
        tier={heroTier}
        winRate={heroGrade?.winRate ?? heroEntry?.winRate}
        pickRate={heroGrade?.pickRate ?? heroEntry?.pickRate}
        games={heroGrade?.games ?? heroEntry?.games}
      />

      {/* ── Filters ── */}
      <FilterBar
        roles={ROLES}
        activeRole={effectiveRole}
        activeRank={normalizedRankTier ?? "all"}
        regionOptions={regionOptions}
        activeRegion={activeRegion}
        patchOptions={patchOptions}
        activePatch={selectedPatch}
        extraParams={sharedFilterParams}
        explicitAllRank
        baseHref={`/lol/champions/${championId}`}
      />
      <div className="mt-3">
        <AnalyticsSampleBanner sample={sampleNotice} />
      </div>
    </>
  );
}

// ── Streamed: win-rate table + builds + matchups ────────────────────────────
async function ChampionSections({
  championId,
  champName,
  version,
  champions,
  searchParams
}: {
  championId: number;
  champName: string;
  version: string;
  champions: Record<string, { id: string; name: string; title?: string }>;
  searchParams: ChampionSearchParams;
}) {
  const data = await loadChampionData(championId, searchParams);
  const {
    activeRegion,
    explicitRole,
    normalizedRankTier,
    selectedPatch,
    itemStatic,
    runeStatic,
    spellStatic,
    profileRes
  } = data;

  if (!profileRes.ok) {
    const kind = profileRes.errorKind;
    const verbosity = getErrorVerbosity();
    return (
      <BackendErrorCard
        title={champName}
        message={
          kind === "timeout"
            ? "This page is taking too long to load."
            : kind === "unreachable"
              ? "We couldn't load champion data right now."
              : "We couldn't load champion data."
        }
        requestId={profileRes.requestId}
        detail={
          verbosity === "verbose"
            ? JSON.stringify(
                {
                  profile: { status: profileRes.status, errorKind: profileRes.errorKind }
                },
                null,
                2
              )
            : null
        }
      />
    );
  }

  const itemVersion = itemStatic.version;
  const items = itemStatic.items;
  const runeById = runeStatic.runeById;
  const styleById = runeStatic.styleById;
  const runeTrees = runeStatic.trees;

  const profile = profileRes.body!;
  const winrates = profile.winRates ?? null;
  const builds = profile.builds ?? null;
  const matchups = profile.matchups ?? null;
  const effectiveRole =
    normalizeRole(profile.effectiveRole) ?? explicitRole ?? pickMostPlayedRole(winrates) ?? "MIDDLE";
  // Show only the active role's win rates (broken down by rank) so the table stays compact and
  // doesn't dominate the page — other roles are reachable via the role filter. When a role is in the
  // URL the backend already returns just that role, so this is a no-op there; on the default view it
  // narrows the multi-role result to the most-played role (effectiveRole).
  const winrateRows = (winrates?.byRoleTier ?? []).filter(
    (w) => (w.role ?? "").toUpperCase() === effectiveRole.toUpperCase()
  );
  const buildRows = builds?.builds ?? [];
  const globalCoreItems = builds?.globalCoreItems ?? [];
  const counters = matchups?.counters ?? [];
  const favorableMatchups = matchups?.favorableMatchups ?? [];
  // Confidence threshold for build variants (omit -> Confidence falls back to DEFAULT_MIN_GAMES).
  const buildMinGames = builds?.sample?.minimumRecommendedSampleSize;
  const allMatchups = [...counters, ...favorableMatchups]
    .filter((m): m is MatchupEntryDto => Boolean(m?.opponentChampionId))
    .filter(
      (entry, idx, rows) =>
        rows.findIndex((candidate) => candidate.opponentChampionId === entry.opponentChampionId) === idx
    )
    // Default order: toughest first (ascending win rate). The table re-sorts client-side from here.
    .sort((a, b) => (a.winRate ?? 0) - (b.winRate ?? 0));
  const matchupRows: MatchupRow[] = allMatchups.map((entry) => {
    const opponentId = entry.opponentChampionId ?? 0;
    const opponent = champions[String(opponentId)];
    return {
      opponentChampionId: opponentId,
      winRate: entry.winRate ?? null,
      games: entry.games ?? null,
      opponentSlug: opponent?.id ?? "Unknown",
      opponentName: opponent?.name ?? `Champion ${opponentId}`,
      verdict: matchupVerdict(entry.winRate)
    };
  });
  const linkParams = new URLSearchParams();
  if (normalizedRankTier) linkParams.set("rankTier", normalizedRankTier);
  if (activeRegion !== "ALL") linkParams.set("region", activeRegion);
  if (selectedPatch) linkParams.set("patch", selectedPatch);
  const linkQuery = linkParams.toString();

  return (
    <>
      {/* ── Win rate by rank — compact strip. Keeps the per-rank win-rate signal (with the
             DataBar's sample-size whisker) without a full table pushing the builds below the
             fold. Overall win/pick/matches live in the hero StatsBar; per-rank pick rate and a
             separate games column were the non-essential weight, so they're dropped here
             (games rides the whisker + the row title). ── */}
      <section className="surface-subtle rounded-card px-4 py-3">
        <div className="flex flex-wrap items-center gap-x-6 gap-y-2.5">
          <span className="type-overline shrink-0 text-muted">
            Win rate by rank
            <span className="ml-1.5 text-fg/45">{roleDisplayLabel(effectiveRole)}</span>
          </span>
          {!winrates ? (
            <span className="type-note text-muted">Win-rate data is unavailable right now.</span>
          ) : winrateRows.length === 0 ? (
            <span className="type-note text-muted">No games for the selected patch yet.</span>
          ) : (
            <div className="flex flex-wrap items-center gap-x-6 gap-y-2.5">
              {winrateRows
                .slice()
                .sort((a, b) => rankTierLadderOrdinal(a.rankTier) - rankTierLadderOrdinal(b.rankTier))
                .map((w) => (
                  <span
                    key={`${w.role ?? "ALL"}-${w.rankTier ?? "all"}`}
                    className="inline-flex items-center gap-2"
                    title={`${rankTierDisplayLabel(w.rankTier ?? "all")} · ${formatGames(w.games)} games`}
                  >
                    <span className="type-caption w-[4.25rem] shrink-0 truncate text-muted">
                      {rankTierDisplayLabel(w.rankTier ?? "all")}
                    </span>
                    <DataBar value={w.winRate} games={w.games} decimals={1} />
                  </span>
                ))}
            </div>
          )}
        </div>
      </section>

      {/* ── Builds + Matchups (balanced two-up; matchups owned by the single sortable table) ── */}
      <div className="grid min-w-0 gap-6 lg:grid-cols-2 lg:items-start">
        {/* ── Builds ── */}
        <Card className="min-w-0 p-5" id="builds">
          <h2 className="type-section">
            Builds
          </h2>
          {!builds ? (
            <div className="mt-2">
              <p className="text-sm text-fg/75">Build data is unavailable right now.</p>
              <p className="mt-1 text-xs text-muted">Try selecting a different region or check back after patch data has been processed.</p>
            </div>
          ) : buildRows.length === 0 ? (
            <p className="mt-2 text-sm text-fg/75">There are not enough games for this role yet.</p>
          ) : (
            <div className="mt-4 grid gap-4">
              {/* Sectioned, timing-aware build path (spells, skills, starters, boots, core, situational) */}
              <BuildBreakdown
                summonerSpells={builds?.summonerSpells}
                skillOrder={builds?.skillOrder}
                startingItems={builds?.startingItems}
                boots={builds?.boots}
                coreBuildPath={builds?.coreBuildPath}
                situationalSlots={builds?.situationalSlots}
                itemVersion={itemVersion}
                items={items}
                spellVersion={spellStatic.version}
                spells={spellStatic.spells}
                minGames={buildMinGames}
              />

              {/* Global Core Items */}
              {globalCoreItems.length > 0 ? (
                <ItemBuildDisplay
                  allItems={[]}
                  coreItems={globalCoreItems}
                  situationalItems={[]}
                  version={itemVersion}
                  items={items}
                />
              ) : null}

              {buildRows.map((b, idx) => (
                <details
                  key={idx}
                  open={idx === 0}
                  className="group rounded-lg border border-border/60 bg-surface-2/40"
                >
                  {/* Recommended is open; alternatives collapse to a summary row (progressive disclosure). */}
                  <summary className="flex cursor-pointer list-none items-center justify-between gap-2 p-3 [&::-webkit-details-marker]:hidden">
                    <span className="flex items-center gap-2 text-sm font-semibold text-fg">
                      <svg
                        viewBox="0 0 12 12"
                        aria-hidden="true"
                        className="size-3 shrink-0 text-muted transition-transform duration-150 group-open:rotate-90"
                      >
                        <path
                          d="M4.5 3 7.5 6 4.5 9"
                          stroke="currentColor"
                          strokeWidth="1.4"
                          fill="none"
                          strokeLinecap="round"
                          strokeLinejoin="round"
                        />
                      </svg>
                      {idx === 0 ? "Recommended Build" : `Alternative ${idx}`}
                    </span>
                    <span className="text-xs text-muted">
                      <WinRateText value={b.winRate} decimals={1} games={b.games} />
                    </span>
                  </summary>

                  <div className="border-t border-border/40 p-3">
                    {/* Items: Core + Situational */}
                    <ItemBuildDisplay
                      allItems={b.items ?? []}
                      coreItems={b.coreItems ?? []}
                      situationalItems={b.situationalItems ?? []}
                      version={itemVersion}
                      items={items}
                      winRate={b.winRate}
                      games={b.games}
                    />

                    {/* Runes */}
                    <div className="mt-3 border-t border-border/40 pt-3">
                      <p className="mb-2 text-xs font-medium text-muted">Runes</p>
                      <RuneTreeDisplay
                        primaryStyleId={b.primaryStyleId ?? 0}
                        subStyleId={b.subStyleId ?? 0}
                        primarySelections={b.primaryRunes ?? []}
                        subSelections={b.subRunes ?? []}
                        statShards={b.statShards ?? []}
                        trees={runeTrees}
                        runeById={runeById}
                        styleById={styleById}
                      />
                    </div>
                  </div>
                </details>
              ))}
            </div>
          )}
        </Card>

        {/* ── Matchups (one sortable table owns matchups; no redundant lists) ── */}
        <Card className="min-w-0 p-5" id="matchups">
          {!matchups ? (
            <>
              <h2 className="type-section">Matchups</h2>
              <div className="mt-2">
                <p className="text-sm text-fg/75">Matchup data is unavailable right now.</p>
                <p className="mt-1 text-xs text-muted">Try selecting a different region or check back after patch data has been processed.</p>
              </div>
            </>
          ) : (
            <MatchupsTable
              title="Matchups"
              subtitle={`Lane matchups for ${champName} as ${roleDisplayLabel(effectiveRole)}`}
              rows={matchupRows}
              version={version}
              linkQuery={linkQuery}
            />
          )}
        </Card>
      </div>
    </>
  );
}

// ── Suspense fallbacks (mirror the streamed regions' layout) ────────────────
function ChampionHeroMetaSkeleton() {
  return (
    <>
      <Skeleton className="h-5 w-56" />
      <Skeleton className="h-14 w-full rounded-lg" />
      <div className="flex flex-wrap gap-3">
        <Skeleton className="h-10 w-64 rounded-lg" />
        <Skeleton className="h-10 w-36 rounded-control" />
        <Skeleton className="h-10 w-40 rounded-lg" />
      </div>
    </>
  );
}

function ChampionSectionsSkeleton() {
  return (
    <>
      <Card className="p-5">
        <Skeleton className="h-6 w-28" />
        <div className="mt-4 grid gap-3">
          {Array.from({ length: 4 }).map((_, i) => (
            <Skeleton key={i} className="h-10 w-full rounded-md" />
          ))}
        </div>
      </Card>

      <div className="grid gap-6 md:grid-cols-2">
        <Card className="p-5">
          <Skeleton className="h-6 w-24" />
          <div className="mt-4 grid gap-4">
            <Skeleton className="h-28 w-full rounded-lg" />
            <Skeleton className="h-28 w-full rounded-lg" />
          </div>
        </Card>
        <Card className="p-5">
          <Skeleton className="h-6 w-28" />
          <div className="mt-4 grid gap-2">
            {Array.from({ length: 5 }).map((_, i) => (
              <Skeleton key={i} className="h-10 w-full rounded-md" />
            ))}
          </div>
        </Card>
      </div>
    </>
  );
}
