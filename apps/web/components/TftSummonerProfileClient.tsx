"use client";

import Image from "next/image";
import Link from "next/link";
import { useCallback, useEffect, useMemo, useState } from "react";
import { usePathname, useRouter } from "next/navigation";

import { Badge } from "@/components/ui/Badge";
import { Button } from "@/components/ui/Button";
import { Card } from "@/components/ui/Card";
import { Skeleton } from "@/components/ui/Skeleton";
import { formatDurationSeconds, formatPercent, formatRelativeTime } from "@/lib/format";
import { computeNextPollDelayMs } from "@/lib/polling";
import { rankEmblemUrl, rankTierColorClass, rankTierDisplayLabel } from "@/lib/ranks";
import { encodeRiotIdPath } from "@/lib/riotid";
import { profileIconUrl } from "@/lib/staticData";
import {
  formatPlacement,
  formatTftPercent,
  placementBarClass,
  placementBgClass,
  placementColorClass,
  type TftAcceptedResponse,
  type TftMatchDetail,
  type TftMatchParticipant,
  type TftPagedMatches,
  type TftRecentMatchSummary,
  type TftSummonerProfile
} from "@/lib/tft";

// ---------------------------------------------------------------------------
// Payload union for SSR hydration
// ---------------------------------------------------------------------------

type TftSummonerPayload =
  | { kind: "profile"; profile: TftSummonerProfile }
  | { kind: "accepted"; accepted: TftAcceptedResponse };

type TftSortOption = "DATE_DESC" | "PLACEMENT_ASC" | "DMG_DESC";

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function rankColorClass(tier?: string): string {
  if (!tier) return "text-fg/80";
  return rankTierColorClass(tier) === "text-muted" ? "text-fg/80" : rankTierColorClass(tier);
}

function friendlyQueueLabel(queueType: string): string {
  const map: Record<string, string> = {
    RANKED_TFT: "Ranked",
    RANKED_TFT_TURBO: "Hyper Roll",
    RANKED_TFT_DOUBLE_UP: "Double Up",
    RANKED_TFT_PAIRS: "Pairs"
  };
  return map[queueType] ?? queueType.replace(/_/g, " ");
}

function normalizeSort(value?: string): TftSortOption {
  if (!value) return "DATE_DESC";
  const v = value.trim().toUpperCase();
  if (v === "PLACEMENT_ASC") return "PLACEMENT_ASC";
  if (v === "DMG_DESC" || v === "DAMAGE_DESC") return "DMG_DESC";
  return "DATE_DESC";
}

function starString(tier: number): string {
  if (tier <= 1) return "";
  return "\u2605".repeat(tier);
}

// Stop polling after this many attempts (~a few minutes once the delay reaches its
// 10s cap) so a never-resolving 202 \u2014 e.g. ingestion still catching up, or a player
// with no ranked TFT games \u2014 can't spin an infinite spinner.
const POLL_MAX_ATTEMPTS = 24;

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export function TftSummonerProfileClient({
  region,
  gameName,
  tagLine,
  initialPayload,
  initialPage = 1,
  initialSort = "DATE_DESC"
}: {
  region: string;
  gameName: string;
  tagLine: string;
  initialPayload: TftSummonerPayload;
  initialPage?: number;
  initialSort?: string;
}) {
  const router = useRouter();
  const pathname = usePathname();
  const title = `${gameName}#${tagLine}`;

  // Profile state
  const [payload, setPayload] = useState<TftSummonerPayload>(initialPayload);
  const [refreshing, setRefreshing] = useState(false);
  const [polling, setPolling] = useState(initialPayload.kind === "accepted");
  const [pollDelayMs, setPollDelayMs] = useState(2000);
  const [pollAttempts, setPollAttempts] = useState(0);
  const [pollTimedOut, setPollTimedOut] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [ddVersion, setDdVersion] = useState<string | null>(null);

  // Match history state
  const [page, setPage] = useState(Math.max(1, initialPage));
  const [sort, setSort] = useState<TftSortOption>(normalizeSort(initialSort));
  const [history, setHistory] = useState<TftPagedMatches | null>(null);
  const [historyBusy, setHistoryBusy] = useState(false);
  const [historyError, setHistoryError] = useState<string | null>(null);
  const [historyReloadKey, setHistoryReloadKey] = useState(0);

  // Match detail expand state
  const [expandedMatchId, setExpandedMatchId] = useState<string | null>(null);
  const [details, setDetails] = useState<Record<string, TftMatchDetail | null>>({});
  const [detailBusy, setDetailBusy] = useState<Record<string, boolean>>({});

  const profile = payload.kind === "profile" ? payload.profile : null;

  // ---------------------------------------------------------------------------
  // DDragon version for profile icon
  // ---------------------------------------------------------------------------
  useEffect(() => {
    let cancelled = false;
    async function load() {
      try {
        const res = await fetch("https://ddragon.leagueoflegends.com/api/versions.json");
        if (!res.ok) return;
        const versions = (await res.json()) as string[];
        if (!cancelled && versions[0]) setDdVersion(versions[0]);
      } catch { /* ok */ }
    }
    void load();
    return () => { cancelled = true; };
  }, []);

  // ---------------------------------------------------------------------------
  // Profile fetching & polling
  // ---------------------------------------------------------------------------
  const loadProfile = useCallback(async () => {
    const res = await fetch(
      `/api/trn/public/tft/summoners/${encodeURIComponent(region)}/${encodeURIComponent(gameName)}/${encodeURIComponent(tagLine)}`,
      { cache: "no-store" }
    );
    const json = (await res.json().catch(() => null)) as TftSummonerProfile | TftAcceptedResponse | null;

    if (res.status === 202 && json) {
      setPayload({ kind: "accepted", accepted: json as TftAcceptedResponse });
      return;
    }
    if (!res.ok || !json) {
      setError(`Failed to load TFT summoner (${res.status}).`);
      setPolling(false);
      return;
    }
    setPayload({ kind: "profile", profile: json as TftSummonerProfile });
    setPolling(false);
  }, [region, gameName, tagLine]);

  useEffect(() => {
    if (!polling) return;
    if (pollAttempts >= POLL_MAX_ATTEMPTS) {
      setPolling(false);
      setPollTimedOut(true);
      return;
    }
    const t = setTimeout(async () => {
      try {
        await loadProfile();
      } finally {
        setPollAttempts((n) => n + 1);
        setPollDelayMs((d) => computeNextPollDelayMs(d));
      }
    }, pollDelayMs);
    return () => clearTimeout(t);
  }, [loadProfile, pollDelayMs, polling, pollAttempts]);

  // Reset polling back to an active state (used by "Update Now" and "Check Again").
  const restartPolling = useCallback(() => {
    setPollTimedOut(false);
    setPollAttempts(0);
    setPollDelayMs(2000);
    setPolling(true);
  }, []);

  // ---------------------------------------------------------------------------
  // Refresh action
  // ---------------------------------------------------------------------------
  async function queueRefresh() {
    setRefreshing(true);
    setError(null);
    try {
      const res = await fetch(
        `/api/trn/public/tft/summoners/${encodeURIComponent(region)}/${encodeURIComponent(gameName)}/${encodeURIComponent(tagLine)}/refresh`,
        { method: "POST" }
      );
      const json = (await res.json().catch(() => null)) as TftAcceptedResponse | null;
      if (!res.ok || !json) {
        setError(`Couldn't start an update (${res.status}).`);
        return;
      }
      setPayload({ kind: "accepted", accepted: json });
      setPollTimedOut(false);
      setPollAttempts(0);
      setPolling(true);
      setPollDelayMs(computeNextPollDelayMs(2000, json.retryAfterSeconds));
    } finally {
      setRefreshing(false);
    }
  }

  // ---------------------------------------------------------------------------
  // Paginated match history
  // ---------------------------------------------------------------------------
  useEffect(() => {
    const summonerId = profile?.summonerId;
    if (!summonerId) return;
    let cancelled = false;

    async function load(id: string) {
      setHistoryBusy(true);
      setHistoryError(null);
      try {
        const res = await fetch(
          `/api/trn/public/tft/summoners/${encodeURIComponent(id)}/matches/recent?page=${page}&pageSize=20`,
          { cache: "no-store" }
        );
        const json = (await res.json().catch(() => null)) as TftPagedMatches | { message?: string } | null;
        if (!res.ok) {
          if (!cancelled) setHistoryError(json && "message" in json ? (json as { message?: string }).message ?? "Failed to load matches." : "Failed to load matches.");
          return;
        }
        if (!cancelled) setHistory(json as TftPagedMatches);
      } catch (e) {
        if (!cancelled) setHistoryError(e instanceof Error ? e.message : "Failed to load matches.");
      } finally {
        if (!cancelled) setHistoryBusy(false);
      }
    }
    void load(summonerId);
    return () => { cancelled = true; };
  }, [page, profile?.summonerId, historyReloadKey]);

  // ---------------------------------------------------------------------------
  // Quick stats computed from match history
  // ---------------------------------------------------------------------------
  const quickStats = useMemo(() => {
    const matches = history?.items ?? [];
    if (matches.length === 0) return null;

    const total = matches.length;
    const avgPlacement = matches.reduce((s, m) => s + m.placement, 0) / total;
    const top4 = matches.filter((m) => m.placement <= 4).length / total;
    const wins = matches.filter((m) => m.placement === 1).length / total;

    return { total, avgPlacement, top4, wins };
  }, [history?.items]);

  const recentForm = useMemo(() => {
    return (history?.items ?? []).slice(0, 10).map((m) => m.placement);
  }, [history?.items]);

  // ---------------------------------------------------------------------------
  // Sorted matches
  // ---------------------------------------------------------------------------
  const visibleMatches = useMemo(() => {
    const items = history?.items?.slice() ?? [];
    if (sort === "PLACEMENT_ASC") items.sort((a, b) => a.placement - b.placement);
    else if (sort === "DMG_DESC") items.sort((a, b) => b.totalDamageToPlayers - a.totalDamageToPlayers);
    else items.sort((a, b) => b.matchDate - a.matchDate);
    return items;
  }, [history?.items, sort]);

  // ---------------------------------------------------------------------------
  // URL state persistence
  // ---------------------------------------------------------------------------
  useEffect(() => {
    const params = new URLSearchParams();
    if (page > 1) params.set("page", String(page));
    if (sort !== "DATE_DESC") params.set("sort", sort.toLowerCase());
    const next = params.toString();
    router.replace(next ? `${pathname}?${next}` : pathname, { scroll: false });
  }, [page, pathname, router, sort]);

  // ---------------------------------------------------------------------------
  // Match detail expansion
  // ---------------------------------------------------------------------------
  async function toggleExpanded(matchId: string) {
    const next = expandedMatchId === matchId ? null : matchId;
    setExpandedMatchId(next);
    if (!next || details[next] || !profile?.summonerId) return;

    setDetailBusy((s) => ({ ...s, [next]: true }));
    try {
      const res = await fetch(
        `/api/trn/public/tft/summoners/${encodeURIComponent(profile.summonerId)}/matches/${encodeURIComponent(next)}`,
        { cache: "no-store" }
      );
      const json = (await res.json().catch(() => null)) as TftMatchDetail | null;
      if (res.ok && json?.participants) setDetails((s) => ({ ...s, [next]: json }));
      else setDetails((s) => ({ ...s, [next]: null }));
    } finally {
      setDetailBusy((s) => ({ ...s, [next]: false }));
    }
  }

  // ---------------------------------------------------------------------------
  // Accepted / loading state
  // ---------------------------------------------------------------------------
  if (payload.kind === "accepted") {
    return (
      <Card className="profile-hero-card grid gap-4 rounded-hero p-6">
        <div>
          <p className="type-kicker text-muted">TFT profile</p>
          <h1 className="type-hero-title mt-2">
            {title}
          </h1>
          <p className="mt-3 text-sm text-fg/75">
            {pollTimedOut
              ? "This is taking longer than expected. This player may not have recent ranked TFT games yet, or ingestion is still catching up — try again shortly."
              : payload.accepted.message ?? "We are pulling this player's latest TFT matches now."}
          </p>
          {polling && !pollTimedOut ? (
            <p className="mt-2 type-caption text-muted" role="status" aria-live="polite">
              Checking for updates…
            </p>
          ) : null}
        </div>
        <div className="flex flex-wrap items-center gap-3">
          <Button type="button" onClick={queueRefresh} disabled={refreshing}>
            {refreshing ? "Starting..." : "Update Now"}
          </Button>
          <Button
            type="button"
            variant="outline"
            disabled={polling && !pollTimedOut}
            onClick={() => {
              restartPolling();
              void loadProfile();
            }}
          >
            Check Again
          </Button>
        </div>
        {error ? <p className="text-sm text-danger">{error}</p> : null}
      </Card>
    );
  }

  // After the accepted early-return, profile is guaranteed non-null.
  if (!profile) return null;

  // ---------------------------------------------------------------------------
  // Sort options
  // ---------------------------------------------------------------------------
  const sortOptions: Array<{ value: TftSortOption; label: string }> = [
    { value: "DATE_DESC", label: "Most Recent" },
    { value: "PLACEMENT_ASC", label: "Best Placement" },
    { value: "DMG_DESC", label: "Highest Damage" }
  ];

  // ---------------------------------------------------------------------------
  // Render profile
  // ---------------------------------------------------------------------------
  return (
    <div className="grid gap-8">
      <Card className="profile-hero-card rounded-hero p-5 md:p-8">
        <div className="relative grid gap-6 xl:grid-cols-[minmax(0,1.3fr)_minmax(300px,0.8fr)] xl:items-end">
          <div className="grid gap-5">
            <div className="flex min-w-0 flex-col gap-5 sm:flex-row sm:items-center">
            {ddVersion ? (
              <Image
                src={profileIconUrl(ddVersion, profile.profileIconId)}
                alt={`${title} icon`}
                width={88}
                height={88}
                className="rounded-panel border border-border/80 shadow-media"
              />
            ) : (
              <div className="h-[88px] w-[88px] rounded-panel border border-border/70 bg-surface/70" />
            )}
            <div className="min-w-0">
              <p className="type-kicker text-muted">TFT profile</p>
              <h1 className="type-hero-title mt-2 truncate">
                {title}
              </h1>
              <p className="mt-2 text-sm text-fg/80">
                {profile.platformRegion} · Level {profile.summonerLevel.toLocaleString()} · Updated{" "}
                {new Date(profile.updatedAtUtc).toLocaleString()}
              </p>
              {recentForm.length > 0 && (
                <div className="mt-4">
                  <div className="flex items-center justify-between gap-3">
                    <p className="type-kicker text-fg/68">Recent placements</p>
                    <p className="text-xs text-fg/70">Latest {recentForm.length} games</p>
                  </div>
                  <div className="mt-2 flex flex-wrap items-center gap-1.5" aria-label="Recent placements (latest first)">
                    {recentForm.map((placement, idx) => (
                      <span
                        key={`p-${idx}-${placement}`}
                        className={`type-overline flex h-7 w-7 items-center justify-center rounded-full font-bold tracking-normal shadow-soft ${placementBgClass(placement)} ${placementColorClass(placement)}`}
                        title={formatPlacement(placement)}
                      >
                        {placement}
                      </span>
                    ))}
                  </div>
                </div>
              )}
            </div>
          </div>
            {error ? (
              <p className="rounded-2xl border border-danger/30 bg-danger/10 px-4 py-3 text-sm text-danger">
                {error}
              </p>
            ) : null}
          </div>
          <div className="surface-card grid gap-3 rounded-panel p-4">
            <div className="flex items-center justify-between gap-3">
              <div>
                <p className="type-kicker text-muted">Snapshot</p>
                <p className="mt-1 text-sm text-fg/66">Recent ladder health and placements.</p>
              </div>
              <Badge className="surface-chip text-fg/72">
                {history ? `${history.totalCount.toLocaleString()} tracked` : "Awaiting history"}
              </Badge>
            </div>
            <div className="grid gap-3 sm:grid-cols-3 xl:grid-cols-1">
              <div className="profile-metric-tile">
                <p className="type-kicker text-muted">Ranked</p>
                <p className={`mt-2 text-xl font-semibold ${rankColorClass(profile.ranks[0]?.tier)}`}>
                  {profile.ranks[0]
                    ? `${rankTierDisplayLabel(profile.ranks[0].tier)} ${profile.ranks[0].rankNumber}`
                    : "Unranked"}
                </p>
                <p className="mt-1 text-sm text-fg/66">
                  {profile.ranks[0]
                    ? `${profile.ranks[0].leaguePoints} LP · ${friendlyQueueLabel(profile.ranks[0].queueType)}`
                    : "No ranked TFT queue data yet"}
                </p>
              </div>
              <div className="profile-metric-tile">
                <p className="type-kicker text-muted">Top 4 rate</p>
                <p className="type-tabular mt-2 text-xl font-semibold text-success">
                  {quickStats ? formatTftPercent(quickStats.top4) : "Pending"}
                </p>
                <p className="mt-1 text-sm text-fg/66">
                  {quickStats
                    ? `${quickStats.total} games · ${quickStats.avgPlacement.toFixed(1)} avg place`
                    : "Waiting for match sample"}
                </p>
              </div>
              <div className="profile-metric-tile">
                <p className="type-kicker text-muted">Wins</p>
                <p className="type-tabular mt-2 text-xl font-semibold text-warning">
                  {quickStats ? formatTftPercent(quickStats.wins) : "Pending"}
                </p>
                <p className="mt-1 text-sm text-fg/66">
                  {quickStats
                    ? `${quickStats.total} recent games`
                    : "First place rate will appear here"}
                </p>
              </div>
            </div>
            <div className="flex flex-wrap items-center gap-2 pt-1">
              <Button variant="outline" onClick={queueRefresh} disabled={refreshing}>
                {refreshing ? "Updating..." : "Update Now"}
              </Button>
            </div>
          </div>
        </div>
      </Card>

      <div className="grid gap-6 xl:grid-cols-[minmax(280px,0.32fr)_minmax(0,1fr)] xl:items-start">
        <aside className="grid content-start gap-5 xl:sticky xl:top-24">
          <Card className="profile-section-card p-5">
            <div>
              <p className="type-kicker text-muted">Ranked snapshot</p>
              <h2 className="mt-2 type-section">Queue breakdown and LP position</h2>
            </div>
            {profile.ranks.length === 0 ? (
              <p className="mt-4 text-sm text-fg/80">No TFT ranked results yet.</p>
            ) : (
              <div className="mt-4 grid gap-4 text-sm">
                {profile.ranks.map((rank) => {
                  const emblem = rankEmblemUrl(rank.tier);
                  const totalGames = rank.wins + rank.losses;
                  const wr = totalGames > 0 ? rank.wins / totalGames : null;
                  return (
                    <div
                      key={`${rank.queueType}-${rank.tier}`}
                      className="surface-subtle grid gap-3 rounded-card px-3 py-3 sm:grid-cols-[68px_minmax(0,1fr)] sm:items-center"
                    >
                      {emblem ? (
                        <div className="flex h-[68px] w-[68px] items-center justify-center rounded-control border border-border/50 bg-surface/70 p-1">
                          <Image
                            src={emblem}
                            alt={`${rankTierDisplayLabel(rank.tier)} emblem`}
                            width={68}
                            height={68}
                            sizes="68px"
                          />
                        </div>
                      ) : (
                        <div className="h-[68px] w-[68px] rounded-control border border-border/50 bg-surface/70" />
                      )}
                      <div className="min-w-0">
                        <p className="type-kicker text-fg/62">{friendlyQueueLabel(rank.queueType)}</p>
                        <p className={`mt-0.5 text-lg font-semibold leading-tight ${rankColorClass(rank.tier)}`}>
                          {rankTierDisplayLabel(rank.tier)} {rank.rankNumber}
                        </p>
                        <p className="mt-2 flex flex-wrap items-center gap-2 text-xs text-fg/70">
                          <span>{rank.leaguePoints} LP</span>
                          {rank.wins}W {rank.losses}L
                          {wr != null && <span className="ml-1">· {formatPercent(wr)}</span>}
                        </p>
                      </div>
                    </div>
                  );
                })}
              </div>
            )}
          </Card>

          {quickStats && (
            <Card className="profile-section-card p-5">
              <div>
                <p className="type-kicker text-muted">Quick stats</p>
                <h2 className="mt-2 type-section">Recent TFT tempo at a glance</h2>
              </div>
              <div className="mt-4 grid gap-3">
                <div className="profile-metric-tile">
                  <p className="type-kicker text-muted">Average placement</p>
                  <p className="type-tabular mt-2 text-2xl font-semibold text-fg">{quickStats.avgPlacement.toFixed(1)}</p>
                  <p className="mt-1 text-sm text-fg/66">Across {quickStats.total} recent games</p>
                </div>
                <div className="grid gap-3 sm:grid-cols-2">
                  <div className="profile-metric-tile">
                    <p className="type-kicker text-muted">Top 4</p>
                    <p className="type-tabular mt-2 text-xl font-semibold text-success">{formatTftPercent(quickStats.top4)}</p>
                    <p className="mt-1 text-sm text-fg/66">High-floor finishes</p>
                  </div>
                  <div className="profile-metric-tile">
                    <p className="type-kicker text-muted">Wins</p>
                    <p className="type-tabular mt-2 text-xl font-semibold text-warning">{formatTftPercent(quickStats.wins)}</p>
                    <p className="mt-1 text-sm text-fg/66">First place rate</p>
                  </div>
                </div>
              </div>
            </Card>
          )}
        </aside>

        <section className="grid gap-5">
          <Card className="profile-section-card rounded-panel p-5 md:p-6">
            <div className="grid gap-5 xl:grid-cols-[minmax(0,1fr)_minmax(250px,auto)] xl:items-start">
              <div>
                <p className="type-kicker text-muted">Match history</p>
                <h2 className="type-panel-title mt-2">
                  Comps, traits, and placement trends
                </h2>
                <p className="mt-2 max-w-2xl text-sm text-fg/70">
                  Open any lobby to compare the whole table, then scan traits and units without digging through clutter.
                </p>
                {history && (
                  <p className="mt-3 text-xs text-fg/65">
                    {history.totalCount} total · Page {history.page} of {history.totalPages}
                  </p>
                )}
              </div>
              <div className="surface-card grid gap-3 rounded-card p-4">
                <select
                  value={sort}
                  onChange={(e) => setSort(e.target.value as TftSortOption)}
                  aria-label="Sort matches by"
                  className="h-10 rounded-control border border-border/70 bg-surface-2/55 px-3 text-sm text-fg shadow-inset"
                >
                  {sortOptions.map((opt) => (
                    <option key={opt.value} value={opt.value}>{opt.label}</option>
                  ))}
                </select>
                <div className="flex flex-wrap items-center gap-2">
                  <Badge className="surface-chip text-fg/72">
                    {history?.items.length ?? 0} shown
                  </Badge>
                  <Badge className="surface-chip text-fg/72">
                    {quickStats ? `${formatTftPercent(quickStats.top4)} top 4` : "No sample yet"}
                  </Badge>
                </div>
              </div>
            </div>

            <div className="mt-5 grid gap-4" aria-busy={historyBusy && !history}>
              {historyBusy && !history ? (
                Array.from({ length: 5 }).map((_, i) => (
                  <Skeleton key={i} className="h-20 w-full rounded-xl" />
                ))
              ) : historyError ? (
                <div className="surface-subtle grid gap-3 rounded-card p-4" role="status">
                  <p className="text-sm text-danger">{historyError}</p>
                  <div>
                    <Button variant="outline" onClick={() => setHistoryReloadKey((k) => k + 1)} disabled={historyBusy}>
                      Retry
                    </Button>
                  </div>
                </div>
              ) : visibleMatches.length === 0 ? (
                <Card className="surface-subtle p-5">
                  <p className="text-sm text-fg/75">No ranked TFT matches recorded yet for this player.</p>
                  <p className="mt-1 text-sm text-fg/60">
                    Use <span className="font-medium text-fg/80">Update Now</span> to fetch the latest games — they appear here once ingestion finishes.
                  </p>
                </Card>
              ) : (
                visibleMatches.map((match) => (
                  <TftMatchCard
                    key={match.matchId}
                    match={match}
                    expanded={expandedMatchId === match.matchId}
                    detail={details[match.matchId] ?? null}
                    detailLoading={detailBusy[match.matchId] ?? false}
                    onToggle={() => void toggleExpanded(match.matchId)}
                    currentGameName={gameName}
                    currentTagLine={tagLine}
                    region={region}
                  />
                ))
              )}
            </div>

            {history && history.totalPages > 1 && (
              <div className="mt-5 flex items-center justify-center gap-3">
                <Button
                  variant="outline"
                  onClick={() => setPage((p) => Math.max(1, p - 1))}
                  disabled={page <= 1 || historyBusy}
                >
                  Prev
                </Button>
                <Badge>
                  {page} / {history.totalPages}
                </Badge>
                <Button
                  variant="outline"
                  onClick={() => setPage((p) => Math.min(history.totalPages, p + 1))}
                  disabled={page >= history.totalPages || historyBusy}
                >
                  Next
                </Button>
              </div>
            )}
          </Card>
        </section>
      </div>
    </div>
  );
}

// ---------------------------------------------------------------------------
// Match Card sub-component
// ---------------------------------------------------------------------------

function TftMatchCard({
  match,
  expanded,
  detail,
  detailLoading,
  onToggle,
  currentGameName,
  currentTagLine,
  region
}: {
  match: TftRecentMatchSummary;
  expanded: boolean;
  detail: TftMatchDetail | null;
  detailLoading: boolean;
  onToggle: () => void;
  currentGameName: string;
  currentTagLine: string;
  region: string;
}) {
  return (
    <div className={`match-card-shell overflow-hidden rounded-panel border ${placementBgClass(match.placement)}`}>
      <button
        type="button"
        onClick={onToggle}
        className="flex w-full items-stretch gap-0 px-0 text-left"
      >
        <div className={`flex w-16 shrink-0 flex-col items-center justify-center ${placementBarClass(match.placement)}`}>
          <span className="text-2xl font-extrabold text-bg">{match.placement}</span>
          <span className="type-overline mt-1 text-bg/70">
            place
          </span>
        </div>

        <div className="flex min-w-0 flex-1 flex-col gap-3 px-4 py-4 md:px-5 md:py-5">
          <div className="flex flex-wrap items-center gap-2">
            <span className={`type-caption surface-chip rounded-full px-2.5 py-1 font-semibold ${placementColorClass(match.placement)}`}>
              {formatPlacement(match.placement)}
            </span>
            <span className="type-caption surface-chip rounded-full px-2.5 py-1 text-fg/72">
              Level {match.level}
            </span>
            <span className="type-caption surface-chip rounded-full px-2.5 py-1 text-fg/72">
              {formatRelativeTime(match.matchDate)}
            </span>
          </div>

          <div className="flex flex-wrap items-center gap-2">
            {match.units.map((unit) => (
              <span
                key={unit.characterId}
                className="type-caption rounded-full border border-primary/22 bg-primary/10 px-2.5 py-1 font-medium text-primary"
              >
                {unit.name ?? unit.characterId}
                {unit.tier > 1 && <span className="ml-0.5 text-rank-gold">{starString(unit.tier)}</span>}
              </span>
            ))}
          </div>

          <div className="flex flex-wrap items-center gap-1.5">
            {match.traits
              .filter((t) => t.tierCurrent > 0)
              .map((trait) => (
                <span
                  key={trait.name}
                  className="type-caption surface-chip rounded-full px-2 py-1 text-fg/70"
                >
                  {trait.name} {trait.numUnits}
                </span>
              ))}
          </div>

          <div className="type-caption flex flex-wrap items-center gap-2 text-fg/60">
            {match.augments.length > 0 && (
              <span>Augments: {match.augments.map((a) => a.replace(/^TFT\d+_/i, "").replace(/_/g, " ")).join(" · ")}</span>
            )}
          </div>
        </div>

        <div className="flex shrink-0 flex-col items-end justify-center gap-2 px-4 py-4 text-right text-xs text-fg/70">
          <div className="surface-subtle rounded-control px-3 py-2">
            <p className="type-tabular text-sm font-semibold text-fg">{match.totalDamageToPlayers.toLocaleString()} dmg</p>
            <p className="type-tabular type-caption mt-1 text-fg/75">{match.playersEliminated} elim</p>
          </div>
          <span className="type-overline text-fg/65">
            {expanded ? "Collapse" : "Expand"}
          </span>
        </div>
      </button>

      {expanded && (
        <div className="border-t border-white/8 p-3 md:p-4">
          {detailLoading ? (
            <div className="grid gap-2">
              {Array.from({ length: 8 }).map((_, i) => (
                <Skeleton key={i} className="h-10 w-full rounded-lg" />
              ))}
            </div>
          ) : detail?.participants ? (
            <TftMatchDetailTable
              participants={detail.participants}
              durationSeconds={detail.durationSeconds}
              currentGameName={currentGameName}
              currentTagLine={currentTagLine}
              region={region}
            />
          ) : (
            <p className="text-sm text-fg/65">Match detail not available.</p>
          )}
        </div>
      )}
    </div>
  );
}

// ---------------------------------------------------------------------------
// Match Detail Table sub-component
// ---------------------------------------------------------------------------

function TftMatchDetailTable({
  participants,
  durationSeconds,
  currentGameName,
  currentTagLine,
  region
}: {
  participants: TftMatchParticipant[];
  durationSeconds: number;
  currentGameName: string;
  currentTagLine: string;
  region: string;
}) {
  const sorted = [...participants].sort((a, b) => a.placement - b.placement);

  return (
    <div className="match-detail-shell grid gap-3 p-3 md:p-4">
      <div className="flex items-center justify-between gap-3">
        <div>
          <p className="type-kicker text-muted">Lobby table</p>
          <p className="mt-1 text-xs text-fg/75">
            Duration: {formatDurationSeconds(durationSeconds)}
          </p>
        </div>
      </div>
      {sorted.map((p) => {
        const gn = p.riotIdGameName ?? p.gameName ?? null;
        const tl = p.riotIdTagLine ?? p.tagLine ?? null;
        const isMe =
          (gn ?? "").toLowerCase() === currentGameName.toLowerCase() &&
          (tl ?? "").toLowerCase() === currentTagLine.toLowerCase();
        const displayName = gn && tl ? `${gn}#${tl}` : gn ?? "Unknown";

        return (
          <div
            key={p.puuid}
            className={`flex items-center gap-3 rounded-control border px-3 py-3 text-xs ${
              isMe
                ? "border-primary/45 bg-primary/10 shadow-card"
                : "surface-subtle"
            }`}
          >
            <span className={`w-10 shrink-0 text-center text-sm font-bold ${placementColorClass(p.placement)}`}>
              {formatPlacement(p.placement)}
            </span>

            <div className="min-w-0 flex-1">
              {gn && tl ? (
                <Link
                  href={`/tft/summoners/${region}/${encodeRiotIdPath({ gameName: gn, tagLine: tl })}`}
                  className="truncate rounded text-sm font-medium text-fg hover:text-primary focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40"
                >
                  {displayName}
                </Link>
              ) : (
                <span className="truncate text-sm text-fg/80">{displayName}</span>
              )}

              <div className="mt-1 flex flex-wrap gap-1.5">
                {p.units.map((u) => (
                  <span
                    key={u.characterId}
                    className="type-caption rounded-full border border-primary/18 bg-primary/8 px-2 py-0.5 text-fg/68"
                  >
                    {u.name ?? u.characterId}
                    {u.tier > 1 && <span className="text-rank-gold">{starString(u.tier)}</span>}
                  </span>
                ))}
              </div>

              <div className="mt-1 flex flex-wrap gap-1.5">
                {p.traits
                  .filter((t) => t.tierCurrent > 0)
                  .map((t) => (
                    <span key={t.name} className="type-caption surface-chip rounded-full px-2 py-0.5 text-fg/60">
                      {t.name}
                    </span>
                  ))}
              </div>
            </div>

            <div className="type-caption type-tabular surface-subtle shrink-0 rounded-control px-3 py-2 text-right text-fg/75">
              <p>Lvl {p.level}</p>
              <p>{p.goldLeft}g left</p>
              <p>{p.totalDamageToPlayers.toLocaleString()} dmg</p>
              {p.timeEliminatedSeconds > 0 && (
                <p>{formatDurationSeconds(p.timeEliminatedSeconds)} elim</p>
              )}
            </div>
          </div>
        );
      })}
    </div>
  );
}
