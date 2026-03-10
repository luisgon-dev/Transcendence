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
import { rankEmblemUrl, rankTierDisplayLabel } from "@/lib/ranks";
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
  const map: Record<string, string> = {
    IRON: "text-zinc-400",
    BRONZE: "text-amber-600",
    SILVER: "text-zinc-300",
    GOLD: "text-yellow-400",
    PLATINUM: "text-cyan-400",
    EMERALD: "text-emerald-400",
    DIAMOND: "text-sky-300",
    MASTER: "text-purple-400",
    GRANDMASTER: "text-red-400",
    CHALLENGER: "text-amber-300"
  };
  return map[tier.toUpperCase()] ?? "text-fg/80";
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
  const [error, setError] = useState<string | null>(null);
  const [ddVersion, setDdVersion] = useState<string | null>(null);

  // Match history state
  const [page, setPage] = useState(Math.max(1, initialPage));
  const [sort, setSort] = useState<TftSortOption>(normalizeSort(initialSort));
  const [history, setHistory] = useState<TftPagedMatches | null>(null);
  const [historyBusy, setHistoryBusy] = useState(false);
  const [historyError, setHistoryError] = useState<string | null>(null);

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
    const t = setTimeout(async () => {
      try {
        await loadProfile();
      } finally {
        setPollDelayMs((d) => computeNextPollDelayMs(d));
      }
    }, pollDelayMs);
    return () => clearTimeout(t);
  }, [loadProfile, pollDelayMs, polling]);

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
  }, [page, profile?.summonerId]);

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
      <Card className="grid gap-4 p-6">
        <div>
          <h1 className="font-[var(--font-sora)] text-3xl font-semibold tracking-tight">{title}</h1>
          <p className="mt-2 text-sm text-fg/75">
            {payload.accepted.message ?? "We are pulling this player's latest TFT matches now."}
          </p>
        </div>
        <div className="flex flex-wrap items-center gap-3">
          <Button type="button" onClick={queueRefresh} disabled={refreshing}>
            {refreshing ? "Starting..." : "Update Now"}
          </Button>
          <Button type="button" variant="outline" onClick={() => void loadProfile()}>
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
    <div className="grid gap-6">
      {/* Profile Header */}
      <Card className="rounded-3xl p-5 md:p-6">
        <div className="flex flex-wrap items-center justify-between gap-4">
          <div className="flex min-w-0 items-center gap-4">
            {ddVersion ? (
              <Image
                src={profileIconUrl(ddVersion, profile.profileIconId)}
                alt={`${title} icon`}
                width={72}
                height={72}
                className="rounded-2xl border border-border/80"
              />
            ) : (
              <div className="h-[72px] w-[72px] rounded-2xl border border-border/70 bg-surface/70" />
            )}
            <div className="min-w-0">
              <h1 className="truncate font-[var(--font-sora)] text-3xl font-semibold">{title}</h1>
              <p className="text-sm text-fg/80">
                {profile.platformRegion} · Level {profile.summonerLevel.toLocaleString()} · Updated{" "}
                {new Date(profile.updatedAtUtc).toLocaleString()}
              </p>
              {recentForm.length > 0 && (
                <div className="mt-2">
                  <p className="text-[11px] uppercase tracking-wide text-fg/70">Recent Form</p>
                  <div className="mt-1 flex flex-wrap items-center gap-1" aria-label="Recent placements (latest first)">
                    {recentForm.map((placement, idx) => (
                      <span
                        key={`p-${idx}`}
                        className={`flex h-5 w-5 items-center justify-center rounded-full text-[10px] font-bold ${placementBgClass(placement)} ${placementColorClass(placement)}`}
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
          <Button variant="outline" onClick={queueRefresh} disabled={refreshing}>
            {refreshing ? "Updating..." : "Update Now"}
          </Button>
        </div>
        {error ? <p className="mt-3 text-sm text-danger">{error}</p> : null}
      </Card>

      {/* Rank cards + Quick stats */}
      <div className="grid gap-4 lg:grid-cols-12 lg:items-start">
        {/* Ranks sidebar */}
        <aside className="grid content-start gap-4 lg:col-span-3">
          <Card className="p-4">
            <h2 className="font-[var(--font-sora)] text-lg font-semibold">Ranked</h2>
            {profile.ranks.length === 0 ? (
              <p className="mt-3 text-sm text-fg/80">No TFT ranked results yet.</p>
            ) : (
              <div className="mt-3 grid gap-2 text-sm">
                {profile.ranks.map((rank) => {
                  const emblem = rankEmblemUrl(rank.tier);
                  const totalGames = rank.wins + rank.losses;
                  const wr = totalGames > 0 ? rank.wins / totalGames : null;
                  return (
                    <div
                      key={`${rank.queueType}-${rank.tier}`}
                      className="grid grid-cols-[72px_minmax(0,1fr)] items-center gap-2.5 rounded-xl border border-border/60 bg-surface/50 px-2.5 py-2"
                    >
                      {emblem ? (
                        <div className="flex h-[72px] w-[72px] items-center justify-center rounded-lg border border-border/50 bg-surface/70 p-1">
                          <Image
                            src={emblem}
                            alt={`${rankTierDisplayLabel(rank.tier)} emblem`}
                            width={72}
                            height={72}
                            unoptimized
                          />
                        </div>
                      ) : (
                        <div className="h-[72px] w-[72px] rounded-lg border border-border/50 bg-surface/70" />
                      )}
                      <div className="min-w-0">
                        <p className="text-xs uppercase tracking-[0.15em] text-fg/65">{friendlyQueueLabel(rank.queueType)}</p>
                        <p className={`mt-0.5 text-lg font-semibold leading-tight ${rankColorClass(rank.tier)}`}>
                          {rankTierDisplayLabel(rank.tier)} {rank.rankNumber}
                        </p>
                        <p className="text-fg/80">{rank.leaguePoints} LP</p>
                        <p className="text-xs text-fg/65">
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

          {/* Quick stats */}
          {quickStats && (
            <Card className="p-4">
              <h2 className="font-[var(--font-sora)] text-lg font-semibold">Quick Stats</h2>
              <p className="mt-1 text-xs text-fg/65">From recent {quickStats.total} games</p>
              <div className="mt-3 grid grid-cols-2 gap-2">
                <div className="rounded-lg border border-border/50 bg-surface/50 p-2.5 text-center">
                  <p className="text-xl font-bold text-fg">{quickStats.avgPlacement.toFixed(1)}</p>
                  <p className="text-[11px] text-fg/65">Avg Place</p>
                </div>
                <div className="rounded-lg border border-border/50 bg-surface/50 p-2.5 text-center">
                  <p className="text-xl font-bold text-emerald-400">{formatTftPercent(quickStats.top4)}</p>
                  <p className="text-[11px] text-fg/65">Top 4</p>
                </div>
                <div className="rounded-lg border border-border/50 bg-surface/50 p-2.5 text-center">
                  <p className="text-xl font-bold text-yellow-400">{formatTftPercent(quickStats.wins)}</p>
                  <p className="text-[11px] text-fg/65">Win Rate</p>
                </div>
                <div className="rounded-lg border border-border/50 bg-surface/50 p-2.5 text-center">
                  <p className="text-xl font-bold text-fg">{quickStats.total}</p>
                  <p className="text-[11px] text-fg/65">Games</p>
                </div>
              </div>
            </Card>
          )}
        </aside>

        {/* Match history main area */}
        <section className="lg:col-span-9">
          <Card className="p-5">
            <div className="flex flex-wrap items-center justify-between gap-3">
              <div>
                <h2 className="font-[var(--font-sora)] text-xl font-semibold">Match History</h2>
                {history && (
                  <p className="text-xs text-fg/65">
                    {history.totalCount} total · Page {history.page} of {history.totalPages}
                  </p>
                )}
              </div>
              <div className="flex items-center gap-2">
                <select
                  value={sort}
                  onChange={(e) => setSort(e.target.value as TftSortOption)}
                  className="h-9 rounded-lg border border-border/70 bg-surface/35 px-2.5 text-xs text-fg"
                >
                  {sortOptions.map((opt) => (
                    <option key={opt.value} value={opt.value}>{opt.label}</option>
                  ))}
                </select>
              </div>
            </div>

            {/* Match cards */}
            <div className="mt-4 grid gap-2">
              {historyBusy && !history ? (
                Array.from({ length: 5 }).map((_, i) => (
                  <Skeleton key={i} className="h-20 w-full rounded-xl" />
                ))
              ) : historyError ? (
                <p className="text-sm text-danger">{historyError}</p>
              ) : visibleMatches.length === 0 ? (
                <p className="text-sm text-fg/65">No matches found.</p>
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

            {/* Pagination */}
            {history && history.totalPages > 1 && (
              <div className="mt-4 flex items-center justify-center gap-3">
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
    <div className={`overflow-hidden rounded-xl border ${placementBgClass(match.placement)} transition`}>
      {/* Compact match row */}
      <button
        type="button"
        onClick={onToggle}
        className="flex w-full items-stretch gap-0 text-left"
      >
        {/* Placement bar */}
        <div className={`flex w-12 shrink-0 flex-col items-center justify-center ${placementBarClass(match.placement)}`}>
          <span className="text-lg font-extrabold text-bg">{match.placement}</span>
        </div>

        {/* Main content */}
        <div className="flex min-w-0 flex-1 flex-col gap-1.5 px-3 py-2.5">
          {/* Units row */}
          <div className="flex flex-wrap items-center gap-1.5">
            {match.units.map((unit) => (
              <span
                key={unit.characterId}
                className="rounded border border-primary/25 bg-primary/8 px-1.5 py-0.5 text-[11px] font-medium text-primary"
              >
                {unit.name ?? unit.characterId}
                {unit.tier > 1 && <span className="ml-0.5 text-yellow-400">{starString(unit.tier)}</span>}
              </span>
            ))}
          </div>

          {/* Traits row */}
          <div className="flex flex-wrap items-center gap-1">
            {match.traits
              .filter((t) => t.tierCurrent > 0)
              .map((trait) => (
                <span
                  key={trait.name}
                  className="rounded-full border border-border/50 bg-surface/40 px-1.5 py-0.5 text-[10px] text-fg/70"
                >
                  {trait.name} {trait.numUnits}
                </span>
              ))}
          </div>

          {/* Augments + time */}
          <div className="flex flex-wrap items-center gap-2 text-[10px] text-fg/60">
            {match.augments.length > 0 && (
              <span>Augments: {match.augments.map((a) => a.replace(/^TFT\d+_/i, "").replace(/_/g, " ")).join(" · ")}</span>
            )}
            <span className="ml-auto">{formatRelativeTime(match.matchDate)}</span>
          </div>
        </div>

        {/* Right side stats */}
        <div className="flex shrink-0 flex-col items-end justify-center px-3 py-2.5 text-right text-xs text-fg/70">
          <p>Lvl {match.level}</p>
          <p>{match.totalDamageToPlayers.toLocaleString()} dmg</p>
          <p>{match.playersEliminated} elim</p>
        </div>
      </button>

      {/* Expanded match detail */}
      {expanded && (
        <div className="border-t border-border/40 bg-surface/30 p-3">
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
    <div className="grid gap-1.5">
      <p className="mb-1 text-xs text-fg/60">
        Duration: {formatDurationSeconds(durationSeconds)}
      </p>
      {sorted.map((p) => {
        const isMe =
          (p.gameName ?? "").toLowerCase() === currentGameName.toLowerCase() &&
          (p.tagLine ?? "").toLowerCase() === currentTagLine.toLowerCase();
        const displayName = p.gameName && p.tagLine ? `${p.gameName}#${p.tagLine}` : p.gameName ?? "Unknown";

        return (
          <div
            key={p.puuid}
            className={`flex items-center gap-2 rounded-lg border px-2.5 py-1.5 text-xs ${
              isMe
                ? "border-primary/50 bg-primary/10"
                : "border-border/40 bg-surface/25"
            }`}
          >
            {/* Placement */}
            <span className={`w-8 shrink-0 text-center text-sm font-bold ${placementColorClass(p.placement)}`}>
              {formatPlacement(p.placement)}
            </span>

            {/* Player name */}
            <div className="min-w-0 flex-1">
              {p.gameName && p.tagLine ? (
                <Link
                  href={`/tft/summoners/${region}/${encodeRiotIdPath({ gameName: p.gameName, tagLine: p.tagLine })}`}
                  className="truncate font-medium text-fg hover:text-primary"
                >
                  {displayName}
                </Link>
              ) : (
                <span className="truncate text-fg/80">{displayName}</span>
              )}

              {/* Units */}
              <div className="mt-0.5 flex flex-wrap gap-1">
                {p.units.map((u) => (
                  <span key={u.characterId} className="text-[10px] text-fg/60">
                    {u.name ?? u.characterId}
                    {u.tier > 1 && <span className="text-yellow-400">{starString(u.tier)}</span>}
                  </span>
                ))}
              </div>

              {/* Traits */}
              <div className="mt-0.5 flex flex-wrap gap-1">
                {p.traits
                  .filter((t) => t.tierCurrent > 0)
                  .map((t) => (
                    <span key={t.name} className="text-[10px] text-fg/50">
                      {t.name}
                    </span>
                  ))}
              </div>
            </div>

            {/* Stats */}
            <div className="shrink-0 text-right text-[11px] text-fg/65">
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
