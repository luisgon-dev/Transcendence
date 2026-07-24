"use client";

import { useEffect, useMemo, useState } from "react";
import dynamic from "next/dynamic";
import Link from "next/link";
import { usePathname } from "next/navigation";

import { PerformanceCard } from "@/components/lol-profile/PerformanceCard";
import { ProfileHeroCard } from "@/components/lol-profile/ProfileHeroCard";
import { ProfileSidebar } from "@/components/lol-profile/ProfileSidebar";
import { StaticDataProvider } from "@/components/lol-profile/StaticDataContext";
import {
  type ApiErrorResponse,
  type ChampionStatic,
  type ItemStatic,
  type MatchSortOption,
  type MatchSummary,
  type PagedResultDto,
  type RankHistoryEntry,
  type RankInfo,
  type RuneStatic,
  type SpellStatic,
  type SummonerLookupResponse
} from "@/components/lol-profile/shared";
import {
  useMatchHistory,
  useProfileStaticData,
  useRankHistory,
  useSummonerRefreshPolling
} from "@/components/lol-profile/useSummonerProfileData";
import { Button } from "@/components/ui/Button";
import { buttonClassName } from "@/components/ui/buttonStyles";
import { Card } from "@/components/ui/Card";
import { EmptyState } from "@/components/ui/EmptyState";
import { SearchIcon } from "@/components/ui/icons";
import { Skeleton } from "@/components/ui/Skeleton";

export type { SummonerProfileResponse } from "@/components/lol-profile/shared";

const SORT_OPTIONS: Array<{ value: MatchSortOption; label: string }> = [
  { value: "DATE_DESC", label: "Most Recent" },
  { value: "KDA_DESC", label: "Best KDA" },
  { value: "DMG_DESC", label: "Highest Damage" }
];

const MatchHistorySection = dynamic(
  () =>
    import("@/components/lol-profile/MatchHistorySection").then(
      (module) => module.MatchHistorySection
    ),
  {
    loading: () => (
      <Card className="profile-section-card p-5">
        <div className="grid gap-3">
          <Skeleton className="h-11 w-full" />
          <Skeleton className="h-40 w-full" />
          <Skeleton className="h-40 w-full" />
        </div>
      </Card>
    )
  }
);

function usePrefersReducedMotion() {
  const [reduced, setReduced] = useState(false);

  useEffect(() => {
    const query = window.matchMedia("(prefers-reduced-motion: reduce)");
    const update = () => setReduced(query.matches);
    update();
    query.addEventListener("change", update);
    return () => query.removeEventListener("change", update);
  }, []);

  return reduced;
}

export function SummonerProfileClient({
  region,
  gameName,
  tagLine,
  initialLookup,
  initialError,
  initialPage = 1,
  initialQueue = "ALL",
  initialExpandMatchId = null,
  initialSort = "DATE_DESC",
  initialChampion = "",
  initialHistory = null,
  initialRankHistory = null,
  initialChampionStatic = null,
  initialItemStatic = null,
  initialSpellStatic = null,
  initialRuneStatic = null
}: {
  region: string;
  gameName: string;
  tagLine: string;
  initialLookup: SummonerLookupResponse | null;
  initialError: ApiErrorResponse | null;
  initialPage?: number;
  initialQueue?: string;
  initialExpandMatchId?: string | null;
  initialSort?: string;
  initialChampion?: string;
  initialHistory?: PagedResultDto<MatchSummary> | null;
  initialRankHistory?: RankHistoryEntry[] | null;
  initialChampionStatic?: ChampionStatic | null;
  initialItemStatic?: ItemStatic | null;
  initialSpellStatic?: SpellStatic | null;
  initialRuneStatic?: RuneStatic | null;
}) {
  const pathname = usePathname();
  const prefersReducedMotion = usePrefersReducedMotion();
  const title = `${gameName}#${tagLine}`;
  const staticData = useProfileStaticData({
    championStatic: initialChampionStatic,
    itemStatic: initialItemStatic,
    spellStatic: initialSpellStatic,
    runeStatic: initialRuneStatic
  });
  const lookup = useSummonerRefreshPolling({
    region,
    gameName,
    tagLine,
    initialLookup,
    initialError
  });
  const matches = useMatchHistory({
    summonerId: lookup.profile?.summonerId,
    championStatic: staticData.championStatic,
    initialPage,
    initialQueue,
    initialSort,
    initialChampion,
    initialExpandMatchId,
    initialHistory
  });
  const rankHistory = useRankHistory(lookup.profile?.summonerId, initialRankHistory);

  useEffect(() => {
    const params = new URLSearchParams();
    if (matches.page > 1) params.set("page", String(matches.page));
    if (matches.queue !== "ALL") params.set("queue", matches.queue);
    if (matches.sort !== "DATE_DESC") params.set("sort", matches.sort.toLowerCase());
    if (matches.championFilter.trim()) params.set("champion", matches.championFilter.trim());
    if (matches.expandedMatchId) params.set("expandMatchId", matches.expandedMatchId);
    const next = params.toString();
    window.history.replaceState(window.history.state, "", next ? `${pathname}?${next}` : pathname);
  }, [
    matches.championFilter,
    matches.expandedMatchId,
    matches.page,
    matches.queue,
    matches.sort,
    pathname
  ]);

  const recentForm = useMemo(
    () => (matches.history?.items ?? []).slice(0, 10).map((match) => match.win),
    [matches.history?.items]
  );
  const rankedEntries = useMemo(() => {
    const entries: Array<{ label: string; rank: RankInfo }> = [];
    if (lookup.profile?.soloRank) entries.push({ label: "Solo/Duo", rank: lookup.profile.soloRank });
    if (lookup.profile?.flexRank) entries.push({ label: "Flex", rank: lookup.profile.flexRank });
    return entries;
  }, [lookup.profile]);
  const unrankedQueues = useMemo(() => {
    const queues: string[] = [];
    if (!lookup.profile?.soloRank) queues.push("Solo/Duo: Unranked");
    if (!lookup.profile?.flexRank) queues.push("Flex: Unranked");
    return queues;
  }, [lookup.profile]);
  const dataAge = lookup.profile?.profileAge?.ageDescription ?? "updated recently";

  return (
    <StaticDataProvider value={staticData}>
      <div className="grid grid-cols-1 gap-8">
        <ProfileHeroCard
          title={title}
          region={region}
          gameName={gameName}
          tagLine={tagLine}
          profile={lookup.profile}
          championStatic={staticData.championStatic}
          dataAge={dataAge}
          rankedEntries={rankedEntries}
          recentForm={recentForm}
          accepted={lookup.accepted}
          error={lookup.error}
          busy={lookup.busy}
          onRefresh={lookup.queueRefresh}
        />

        {!lookup.profile ? (
          lookup.polling && !lookup.error ? (
            <Card className="profile-section-card p-5">
              <Skeleton className="h-16 w-full" />
            </Card>
          ) : (
            <EmptyState
              icon={<SearchIcon className="h-6 w-6" />}
              title={`We couldn’t find ${title}`}
              description={
                lookup.error?.message
                  ? `${lookup.error.message} Double-check the Riot ID and that ${region.toUpperCase()} is the right region, or queue an update to fetch fresh data.`
                  : `No profile in ${region.toUpperCase()} matched this Riot ID. Double-check the spelling and region, or queue an update to fetch fresh data.`
              }
              action={
                <div className="flex flex-wrap items-center justify-center gap-2">
                  <Button
                    variant="primary"
                    onClick={() => void lookup.queueRefresh()}
                    disabled={lookup.busy}
                  >
                    {lookup.busy ? "Updating…" : "Update now"}
                  </Button>
                  <Link href="/" className={buttonClassName({ variant: "outline" })}>
                    New search
                  </Link>
                </div>
              }
            />
          )
        ) : (
          <div className="grid min-w-0 grid-cols-1 gap-6 xl:grid-cols-[20rem_minmax(0,1fr)] xl:items-start">
            <div className="order-2 min-w-0 xl:order-1">
              <ProfileSidebar
                profile={lookup.profile}
                championStatic={staticData.championStatic}
                rankedEntries={rankedEntries}
                unrankedQueues={unrankedQueues}
                rankHistory={rankHistory}
                region={region}
                gameName={gameName}
                tagLine={tagLine}
              />
            </div>
            <div className="order-1 grid min-w-0 gap-6 [&>*]:min-w-0 [&>*]:max-w-full xl:order-2">
              <PerformanceCard
                matches={matches.history?.items ?? []}
                overviewStats={lookup.profile.overviewStats}
                championStatic={staticData.championStatic}
                activeSeason={lookup.profile.activeSeason}
                fullHistory={lookup.profile.fullHistory}
              />
              <MatchHistorySection
                identity={{
                  region,
                  gameName,
                  tagLine,
                  summonerId: lookup.profile.summonerId ?? ""
                }}
                filters={{
                  queue: matches.queue,
                  championFilter: matches.championFilter,
                  sort: matches.sort,
                  queueOptions: matches.queueOptions,
                  championOptions: matches.championOptions,
                  sortOptions: SORT_OPTIONS,
                  onQueueChange: matches.setQueue,
                  onChampionFilterChange: matches.setChampionFilter,
                  onSortChange: matches.setSort
                }}
                pageState={{
                  page: matches.page,
                  history: matches.history,
                  historyBusy: matches.historyBusy,
                  historyError: matches.historyError,
                  visibleMatches: matches.visibleMatches,
                  onPreviousPage: matches.previousPage,
                  onNextPage: matches.nextPage
                }}
                expansion={{
                  expandedMatchId: matches.expandedMatchId,
                  details: matches.details,
                  detailBusy: matches.detailBusy,
                  onToggleExpanded: matches.toggleExpanded
                }}
                prefersReducedMotion={prefersReducedMotion}
              />
            </div>
          </div>
        )}
      </div>
    </StaticDataProvider>
  );
}
