"use client";

import Image from "next/image";
import Link from "next/link";
import { useCallback, useEffect, useMemo, useState } from "react";

import { FavoriteButton } from "@/components/FavoriteButton";
import { LiveGameCard } from "@/components/LiveGameCard";
import { Badge } from "@/components/ui/Badge";
import { Button } from "@/components/ui/Button";
import { Card } from "@/components/ui/Card";
import { Skeleton } from "@/components/ui/Skeleton";
import { encodeRiotIdPath } from "@/lib/riotid";
import { computeNextPollDelayMs } from "@/lib/polling";

type DataAgeMetadata = {
  fetchedAt?: string;
  ageDescription?: string;
  [k: string]: unknown;
};

type RankInfo = {
  tier: string;
  division: string;
  leaguePoints: number;
  wins: number;
  losses: number;
};

type ProfileOverviewStats = {
  totalMatches: number;
  wins: number;
  losses: number;
  winRate: number;
  avgKills: number;
  avgDeaths: number;
  avgAssists: number;
  kdaRatio: number;
  avgCsPerMin: number;
  avgVisionScore: number;
  avgDamageToChamps: number;
};

type ProfileChampionStat = {
  championId: number;
  championName: string;
  games: number;
  wins: number;
  losses: number;
  winRate: number;
  kdaRatio: number;
};

type ProfileRecentMatch = {
  matchId: string;
  matchDate: number;
  queueType: string;
  win: boolean;
  championId: number;
  championName: string;
  kills: number;
  deaths: number;
  assists: number;
  csPerMin: number;
};

export type SummonerProfileResponse = {
  summonerId?: string;
  puuid: string;
  gameName: string;
  tagLine: string;
  summonerLevel: number;
  profileIconId: number;
  soloRank?: RankInfo | null;
  flexRank?: RankInfo | null;
  overviewStats?: ProfileOverviewStats | null;
  topChampions?: ProfileChampionStat[] | null;
  recentMatches?: ProfileRecentMatch[] | null;
  profileAge: DataAgeMetadata;
  rankAge: DataAgeMetadata;
  statsAge?: DataAgeMetadata | null;
};

type AcceptedResponse = {
  message?: string;
  retryAfterSeconds?: number;
  poll?: string;
};

function isRecord(v: unknown): v is Record<string, unknown> {
  return typeof v === "object" && v !== null;
}

type ChampionStatic = {
  version: string;
  champions: Record<string, { id: string; name: string }>;
};

function percent(n: number) {
  return `${(n * 100).toFixed(1)}%`;
}

function fmtKda(k: number, d: number, a: number) {
  return `${k}/${d}/${a}`;
}

function champIconUrl(staticData: ChampionStatic | null, championId: number) {
  const champ = staticData?.champions[String(championId)];
  if (!champ) return null;
  return `https://ddragon.leagueoflegends.com/cdn/${staticData!.version}/img/champion/${champ.id}.png`;
}

export function SummonerProfileClient({
  region,
  gameName,
  tagLine,
  initialStatus,
  initialBody
}: {
  region: string;
  gameName: string;
  tagLine: string;
  initialStatus: number;
  initialBody: unknown;
}) {
  const [profile, setProfile] = useState<SummonerProfileResponse | null>(
    initialStatus === 200 ? (initialBody as SummonerProfileResponse) : null
  );
  const [accepted, setAccepted] = useState<AcceptedResponse | null>(
    initialStatus === 202 ? (initialBody as AcceptedResponse) : null
  );
  const [busy, setBusy] = useState(false);
  const [polling, setPolling] = useState(initialStatus === 202);
  const [pollDelayMs, setPollDelayMs] = useState(2000);
  const [staticData, setStaticData] = useState<ChampionStatic | null>(null);

  const title = useMemo(() => `${gameName}#${tagLine}`, [gameName, tagLine]);

  useEffect(() => {
    let cancelled = false;
    async function loadStatic() {
      try {
        const res = await fetch("/api/static/champions");
        if (!res.ok) return;
        const json = (await res.json()) as ChampionStatic;
        if (!cancelled) setStaticData(json);
      } catch {
        // ignore
      }
    }
    void loadStatic();
    return () => {
      cancelled = true;
    };
  }, []);

  const fetchProfileOnce = useCallback(async () => {
    const res = await fetch(
      `/api/trn/public/summoners/${encodeURIComponent(region)}/${encodeURIComponent(
        gameName
      )}/${encodeURIComponent(tagLine)}`,
      { cache: "no-store" }
    );

    const json = (await res.json().catch(() => null)) as unknown;
    if (res.status === 200) {
      setProfile(json as SummonerProfileResponse);
      setAccepted(null);
      setPolling(false);
      return;
    }

    if (res.status === 202) {
      const acc: AcceptedResponse | null = isRecord(json)
        ? {
            message:
              typeof json.message === "string" ? (json.message as string) : undefined,
            poll: typeof json.poll === "string" ? (json.poll as string) : undefined,
            retryAfterSeconds:
              typeof json.retryAfterSeconds === "number"
                ? (json.retryAfterSeconds as number)
                : undefined
          }
        : null;

      setAccepted(acc ?? { message: "Refresh in process." });
      if (acc && Number.isFinite(acc.retryAfterSeconds)) {
        setPollDelayMs((d) => computeNextPollDelayMs(d, acc.retryAfterSeconds));
      }
      return;
    }

    setAccepted({ message: `Unexpected response (${res.status}).` });
  }, [region, gameName, tagLine]);

  useEffect(() => {
    if (!polling) return;

    const t = setTimeout(async () => {
      await fetchProfileOnce();
      setPollDelayMs((d) => computeNextPollDelayMs(d));
    }, pollDelayMs);

    return () => clearTimeout(t);
  }, [polling, pollDelayMs, fetchProfileOnce]);

  async function queueRefresh() {
    setBusy(true);
    try {
      const res = await fetch(
        `/api/trn/public/summoners/${encodeURIComponent(region)}/${encodeURIComponent(
          gameName
        )}/${encodeURIComponent(tagLine)}/refresh`,
        { method: "POST" }
      );
      const json = (await res.json().catch(() => null)) as AcceptedResponse | null;
      setAccepted(json ?? { message: "Refresh queued." });
      setPolling(true);

      setPollDelayMs(computeNextPollDelayMs(2000, json?.retryAfterSeconds));
    } finally {
      setBusy(false);
    }
  }

  const canShowRefresh = Boolean(profile);

  return (
    <div className="grid gap-6">
      <header className="flex flex-col gap-3">
        <div className="flex flex-wrap items-center justify-between gap-3">
          <div className="flex items-center gap-3">
            <div className="h-12 w-12 rounded-xl border border-border/70 bg-white/5" />
            <div>
              <h1 className="font-[var(--font-sora)] text-3xl font-semibold tracking-tight">
                {title}
              </h1>
              {profile ? (
                <p className="text-sm text-muted">
                  Level {profile.summonerLevel} · Profile{" "}
                  {profile.profileAge?.ageDescription ?? "updated recently"} · Rank{" "}
                  {profile.rankAge?.ageDescription ?? "updated recently"}
                </p>
              ) : (
                <p className="text-sm text-muted">{region.toUpperCase()}</p>
              )}
            </div>
          </div>

          <div className="flex flex-wrap items-center gap-2">
            {canShowRefresh ? (
              <Button variant="outline" onClick={queueRefresh} disabled={busy}>
                {busy ? "Queueing..." : "Refresh"}
              </Button>
            ) : (
              <Button onClick={queueRefresh} disabled={busy}>
                {busy ? "Queueing..." : "Fetch Profile"}
              </Button>
            )}

            <FavoriteButton region={region} gameName={gameName} tagLine={tagLine} />
          </div>
        </div>

        {accepted?.message ? (
          <Card className="p-4">
            <p className="text-sm text-fg/85">{accepted.message}</p>
            {polling ? (
              <p className="mt-1 text-xs text-muted">
                Polling for data... (next check in ~{Math.round(pollDelayMs / 1000)}s)
              </p>
            ) : null}
          </Card>
        ) : null}
      </header>

      {!profile ? (
        <div className="grid gap-4 md:grid-cols-2">
          <Card className="p-5">
            <Skeleton className="h-5 w-40" />
            <Skeleton className="mt-4 h-24 w-full" />
          </Card>
          <Card className="p-5">
            <Skeleton className="h-5 w-40" />
            <Skeleton className="mt-4 h-24 w-full" />
          </Card>
        </div>
      ) : (
        <div className="grid gap-6">
          <div className="grid gap-6 md:grid-cols-2">
            <Card className="p-5">
              <h2 className="font-[var(--font-sora)] text-lg font-semibold">
                Ranks
              </h2>
              <div className="mt-4 grid gap-3">
                <div className="flex items-center justify-between rounded-lg border border-border/60 bg-white/5 px-3 py-2">
                  <p className="text-sm font-medium">Solo/Duo</p>
                  <p className="text-sm text-fg/80">
                    {profile.soloRank
                      ? `${profile.soloRank.tier} ${profile.soloRank.division} · ${profile.soloRank.leaguePoints} LP`
                      : "Unranked"}
                  </p>
                </div>
                <div className="flex items-center justify-between rounded-lg border border-border/60 bg-white/5 px-3 py-2">
                  <p className="text-sm font-medium">Flex</p>
                  <p className="text-sm text-fg/80">
                    {profile.flexRank
                      ? `${profile.flexRank.tier} ${profile.flexRank.division} · ${profile.flexRank.leaguePoints} LP`
                      : "Unranked"}
                  </p>
                </div>
              </div>
            </Card>

            <Card className="p-5">
              <h2 className="font-[var(--font-sora)] text-lg font-semibold">
                Overview
              </h2>
              {!profile.overviewStats ? (
                <p className="mt-3 text-sm text-muted">No stats yet.</p>
              ) : (
                <div className="mt-4 grid grid-cols-2 gap-3 text-sm">
                  <div className="rounded-lg border border-border/60 bg-white/5 px-3 py-2">
                    <p className="text-xs text-muted">Win Rate</p>
                    <p className="text-sm font-semibold">
                      {percent(profile.overviewStats.winRate)}
                    </p>
                  </div>
                  <div className="rounded-lg border border-border/60 bg-white/5 px-3 py-2">
                    <p className="text-xs text-muted">Matches</p>
                    <p className="text-sm font-semibold">
                      {profile.overviewStats.totalMatches}
                    </p>
                  </div>
                  <div className="rounded-lg border border-border/60 bg-white/5 px-3 py-2">
                    <p className="text-xs text-muted">KDA</p>
                    <p className="text-sm font-semibold">
                      {profile.overviewStats.kdaRatio.toFixed(2)}
                    </p>
                  </div>
                  <div className="rounded-lg border border-border/60 bg-white/5 px-3 py-2">
                    <p className="text-xs text-muted">CS/min</p>
                    <p className="text-sm font-semibold">
                      {profile.overviewStats.avgCsPerMin.toFixed(1)}
                    </p>
                  </div>
                </div>
              )}
            </Card>
          </div>

          <div className="grid gap-6 md:grid-cols-2">
            <Card className="p-5">
              <div className="flex items-center justify-between">
                <h2 className="font-[var(--font-sora)] text-lg font-semibold">
                  Top Champions
                </h2>
                <Badge>{(profile.topChampions ?? []).length}</Badge>
              </div>
              <div className="mt-4 grid gap-2">
                {(profile.topChampions ?? []).map((c) => (
                  <div
                    key={c.championId}
                    className="flex items-center justify-between rounded-lg border border-border/60 bg-white/5 px-3 py-2"
                  >
                    <div className="flex items-center gap-3">
                      {champIconUrl(staticData, c.championId) ? (
                        <Image
                          src={champIconUrl(staticData, c.championId)!}
                          alt={c.championName}
                          width={28}
                          height={28}
                          className="rounded-md"
                        />
                      ) : (
                        <div className="h-7 w-7 rounded-md border border-border/60 bg-black/30" />
                      )}
                      <div className="min-w-0">
                        <Link
                          href={`/champions/${c.championId}`}
                          className="truncate text-sm font-medium hover:underline"
                        >
                          {staticData?.champions[String(c.championId)]?.name ??
                            c.championName}
                        </Link>
                        <p className="text-xs text-muted">{c.games} games</p>
                      </div>
                    </div>
                    <p className="text-xs text-fg/80">
                      {percent(c.winRate)} · {c.kdaRatio.toFixed(2)} KDA
                    </p>
                  </div>
                ))}
              </div>
            </Card>

            <Card className="p-5">
              <div className="flex items-center justify-between">
                <h2 className="font-[var(--font-sora)] text-lg font-semibold">
                  Recent Matches
                </h2>
                {profile.summonerId ? (
                  <Link
                    className="text-sm text-primary hover:underline"
                    href={`/summoners/${region}/${encodeRiotIdPath({
                      gameName,
                      tagLine
                    })}/matches`}
                  >
                    View all
                  </Link>
                ) : null}
              </div>
              <div className="mt-4 grid gap-2">
                {(profile.recentMatches ?? []).map((m) => (
                  <div
                    key={m.matchId}
                    className={`flex items-center justify-between rounded-lg border px-3 py-2 ${
                      m.win
                        ? "border-emerald-400/30 bg-emerald-500/10"
                        : "border-red-400/30 bg-red-500/10"
                    }`}
                  >
                    <div className="flex items-center gap-3">
                      {champIconUrl(staticData, m.championId) ? (
                        <Image
                          src={champIconUrl(staticData, m.championId)!}
                          alt={m.championName}
                          width={28}
                          height={28}
                          className="rounded-md"
                        />
                      ) : (
                        <div className="h-7 w-7 rounded-md border border-border/60 bg-black/30" />
                      )}
                      <div className="min-w-0">
                        <p className="truncate text-sm font-medium text-fg">
                          {staticData?.champions[String(m.championId)]?.name ??
                            m.championName}{" "}
                          <span className="text-xs text-muted">({m.queueType})</span>
                        </p>
                        <p className="text-xs text-muted">{fmtKda(m.kills, m.deaths, m.assists)}</p>
                      </div>
                    </div>
                    <span className="text-xs text-fg/80">
                      {m.win ? "Win" : "Loss"}
                    </span>
                  </div>
                ))}
              </div>
            </Card>
          </div>

          <LiveGameCard region={region} gameName={gameName} tagLine={tagLine} />
        </div>
      )}
    </div>
  );
}
