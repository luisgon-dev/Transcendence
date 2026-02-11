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
import { formatPercent } from "@/lib/format";
import { roleDisplayLabel } from "@/lib/roles";
import { encodeRiotIdPath } from "@/lib/riotid";
import { computeNextPollDelayMs } from "@/lib/polling";
import { profileIconUrl } from "@/lib/staticData";

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

type ApiErrorResponse = {
  message?: string;
  code?: string;
  requestId?: string;
  detail?: string;
};

type RoleStatDto = {
  role: string;
  games: number;
  wins: number;
  losses: number;
  winRate: number;
};

type SummonerOverviewDto = {
  summonerId: string;
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
  avgGameDurationMin: number;
  recentPerformance: { matchId: string; win: boolean }[];
};

function isRecord(v: unknown): v is Record<string, unknown> {
  return typeof v === "object" && v !== null;
}

function pickApiError(status: number, json: unknown): ApiErrorResponse {
  if (!isRecord(json)) {
    return { message: `Request failed (${status}).` };
  }

  const msg = typeof json.message === "string" ? (json.message as string) : null;
  const requestId =
    typeof json.requestId === "string" ? (json.requestId as string) : undefined;
  const detail =
    typeof json.detail === "string" ? (json.detail as string) : undefined;
  const code = typeof json.code === "string" ? (json.code as string) : undefined;

  return {
    message: msg ?? `Request failed (${status}).`,
    code,
    requestId,
    detail
  };
}

type ChampionStatic = {
  version: string;
  champions: Record<string, { id: string; name: string }>;
};

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
  const [error, setError] = useState<ApiErrorResponse | null>(
    initialStatus !== 200 && initialStatus !== 202
      ? pickApiError(initialStatus, initialBody)
      : null
  );
  const [busy, setBusy] = useState(false);
  const [polling, setPolling] = useState(initialStatus === 202);
  const [pollDelayMs, setPollDelayMs] = useState(2000);
  const [staticData, setStaticData] = useState<ChampionStatic | null>(null);
  const [roles, setRoles] = useState<RoleStatDto[] | null>(null);
  const [overview, setOverview] = useState<SummonerOverviewDto | null>(null);
  const [extraError, setExtraError] = useState<string | null>(null);
  const [tab, setTab] = useState<"overview" | "champions" | "matches" | "live">(
    "overview"
  );

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

  useEffect(() => {
    const summonerId = profile?.summonerId;
    if (!summonerId) return;

    let cancelled = false;
    async function loadExtras(id: string) {
      setExtraError(null);
      try {
        const [rolesRes, overviewRes] = await Promise.all([
          fetch(`/api/trn/public/summoners/${encodeURIComponent(id)}/stats/roles`, {
            cache: "no-store"
          }),
          fetch(
            `/api/trn/public/summoners/${encodeURIComponent(id)}/stats/overview?recent=10`,
            { cache: "no-store" }
          )
        ]);

        if (!rolesRes.ok || !overviewRes.ok) {
          if (!cancelled) setExtraError("Failed to load extra stats.");
          return;
        }

        const rolesJson = (await rolesRes.json().catch(() => null)) as unknown;
        const overviewJson = (await overviewRes.json().catch(() => null)) as unknown;

        if (!cancelled) {
          setRoles(Array.isArray(rolesJson) ? (rolesJson as RoleStatDto[]) : null);
          setOverview(
            overviewJson && typeof overviewJson === "object"
              ? (overviewJson as SummonerOverviewDto)
              : null
          );
        }
      } catch (e) {
        if (!cancelled) {
          setExtraError(e instanceof Error ? e.message : "Failed to load extra stats.");
        }
      }
    }

    void loadExtras(summonerId);
    return () => {
      cancelled = true;
    };
  }, [profile?.summonerId]);

  const fetchProfileOnce = useCallback(async () => {
    setError(null);
    try {
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
                typeof json.message === "string"
                  ? (json.message as string)
                  : undefined,
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

      setAccepted(null);
      setError(pickApiError(res.status, json));
    } catch (e) {
      setAccepted(null);
      setError({
        message: e instanceof Error ? e.message : "Request failed.",
        code: "CLIENT_FETCH_FAILED"
      });
    }
  }, [region, gameName, tagLine]);

  function friendlyAcceptedMessage(msg?: string) {
    const m = (msg ?? "").toLowerCase();
    if (!m) return null;

    if (m.includes("refresh queued")) {
      return "Update started. This page will refresh as soon as new data is ready.";
    }
    if (m.includes("refresh in process")) {
      return "Update in progress. New data will appear here shortly.";
    }
    if (m.includes("summoner not found")) {
      return "We don't have data for this player yet. Start an update to fetch it.";
    }
    return msg ?? null;
  }

  useEffect(() => {
    if (!polling) return;

    const t = setTimeout(async () => {
      try {
        await fetchProfileOnce();
      } catch (e) {
        setAccepted(null);
        setError({
          message: e instanceof Error ? e.message : "Request failed.",
          code: "CLIENT_FETCH_FAILED"
        });
      }
      setPollDelayMs((d) => computeNextPollDelayMs(d));
    }, pollDelayMs);

    return () => clearTimeout(t);
  }, [polling, pollDelayMs, fetchProfileOnce]);

  async function queueRefresh() {
    setBusy(true);
    setError(null);
    try {
      const res = await fetch(
        `/api/trn/public/summoners/${encodeURIComponent(region)}/${encodeURIComponent(
          gameName
        )}/${encodeURIComponent(tagLine)}/refresh`,
        { method: "POST" }
      );
      const json = (await res.json().catch(() => null)) as AcceptedResponse | null;
      if (!res.ok) {
        setAccepted(null);
        setError(pickApiError(res.status, json));
        return;
      }

      setAccepted(json ?? { message: "Refresh queued." });
      setPolling(true);
      setPollDelayMs(computeNextPollDelayMs(2000, json?.retryAfterSeconds));
    } catch (e) {
      setAccepted(null);
      setError({
        message: e instanceof Error ? e.message : "Request failed.",
        code: "CLIENT_FETCH_FAILED"
      });
    } finally {
      setBusy(false);
    }
  }

  const canShowRefresh = Boolean(profile);
  const staticVersion = staticData?.version;

  const dataAge = profile?.profileAge?.ageDescription ?? "updated recently";
  const rankAge = profile?.rankAge?.ageDescription ?? "updated recently";
  const statsAge = profile?.statsAge?.ageDescription ?? null;

  return (
    <div className="grid gap-6">
      <header className="flex flex-col gap-3">
        <div className="flex flex-wrap items-center justify-between gap-3">
          <div className="flex items-center gap-3">
            {profile && staticVersion ? (
              <Image
                src={profileIconUrl(staticVersion, profile.profileIconId)}
                alt={`${title} profile icon`}
                width={48}
                height={48}
                className="rounded-xl border border-border/70 bg-white/5"
              />
            ) : (
              <div className="h-12 w-12 rounded-xl border border-border/70 bg-white/5" />
            )}
            <div>
              <h1 className="font-[var(--font-sora)] text-3xl font-semibold tracking-tight">
                {title}
              </h1>
              {profile ? (
                <p className="text-sm text-muted">
                  Level {profile.summonerLevel} · Updated {dataAge}
                </p>
              ) : (
                <p className="text-sm text-muted">{region.toUpperCase()}</p>
              )}
            </div>
          </div>

          <div className="flex flex-wrap items-center gap-2">
            {canShowRefresh ? (
              <Button variant="outline" onClick={queueRefresh} disabled={busy}>
                {busy ? "Starting..." : "Update"}
              </Button>
            ) : (
              <Button onClick={queueRefresh} disabled={busy}>
                {busy ? "Starting..." : "Load data"}
              </Button>
            )}

            <FavoriteButton region={region} gameName={gameName} tagLine={tagLine} />
          </div>
        </div>

        {accepted?.message ? (
          <Card className="p-4">
            <p className="text-sm text-fg/85">
              {friendlyAcceptedMessage(accepted.message) ?? accepted.message}
            </p>
            {polling ? (
              <p className="mt-1 text-xs text-muted">
                Updating... checking again in ~{Math.round(pollDelayMs / 1000)}s
              </p>
            ) : null}
          </Card>
        ) : null}

        {error?.message ? (
          <Card className="p-4">
            <p className="text-sm text-fg/85">{error.message}</p>
            {error.requestId ? (
              <p className="mt-1 text-xs text-muted">
                Request ID: <code>{error.requestId}</code>
              </p>
            ) : null}
            {error.detail ? (
              <pre className="mt-2 max-w-full overflow-x-auto rounded-lg border border-border/60 bg-black/25 p-3 text-xs text-fg/80">
                {error.detail}
              </pre>
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
          <div className="flex flex-wrap items-center justify-between gap-3">
            <div className="flex flex-wrap items-center gap-2 text-xs">
              <Badge className="bg-white/5 text-fg/90" title="Profile data freshness">
                Profile: {dataAge}
              </Badge>
              <Badge className="bg-white/5 text-fg/90" title="Rank data freshness">
                Rank: {rankAge}
              </Badge>
              {statsAge ? (
                <Badge className="bg-white/5 text-fg/90" title="Stats data freshness">
                  Stats: {statsAge}
                </Badge>
              ) : null}
            </div>

            <div className="flex flex-wrap gap-2">
              {(
                [
                  ["overview", "Overview"],
                  ["champions", "Champions"],
                  ["matches", "Matches"],
                  ["live", "Live"]
                ] as const
              ).map(([key, label]) => (
                <button
                  key={key}
                  type="button"
                  onClick={() => setTab(key)}
                  className={`rounded-md border px-3 py-1.5 text-sm transition ${
                    tab === key
                      ? "border-primary/50 bg-primary/15 text-primary"
                      : "border-border/70 bg-white/5 text-fg/80 hover:bg-white/10"
                  }`}
                >
                  {label}
                </button>
              ))}
            </div>
          </div>

          {tab === "overview" ? (
            <div className="grid gap-6 md:grid-cols-2">
              <Card className="p-5">
                <h2 className="font-[var(--font-sora)] text-lg font-semibold">Ranks</h2>
                <div className="mt-4 grid gap-3">
                  <div className="flex items-center justify-between rounded-lg border border-border/60 bg-white/5 px-3 py-2">
                    <p className="text-sm font-medium">Solo/Duo</p>
                    <p className="text-sm text-fg/80">
                      {profile.soloRank
                        ? `${profile.soloRank.tier} ${profile.soloRank.division} · ${profile.soloRank.leaguePoints} LP (${profile.soloRank.wins}W/${profile.soloRank.losses}L)`
                        : "Unranked"}
                    </p>
                  </div>
                  <div className="flex items-center justify-between rounded-lg border border-border/60 bg-white/5 px-3 py-2">
                    <p className="text-sm font-medium">Flex</p>
                    <p className="text-sm text-fg/80">
                      {profile.flexRank
                        ? `${profile.flexRank.tier} ${profile.flexRank.division} · ${profile.flexRank.leaguePoints} LP (${profile.flexRank.wins}W/${profile.flexRank.losses}L)`
                        : "Unranked"}
                    </p>
                  </div>
                </div>
              </Card>

              <Card className="p-5">
                <div className="flex items-center justify-between gap-3">
                  <h2 className="font-[var(--font-sora)] text-lg font-semibold">
                    Overview
                  </h2>
                  {overview?.recentPerformance?.length ? (
                    <div className="flex items-center gap-1" title="Recent form (last 10)">
                      {overview.recentPerformance.slice(0, 10).map((p) => (
                        <span
                          key={p.matchId}
                          className={`h-3 w-3 rounded-sm border border-border/60 ${
                            p.win ? "bg-emerald-400/60" : "bg-red-400/60"
                          }`}
                          title={p.win ? "Win" : "Loss"}
                        />
                      ))}
                    </div>
                  ) : null}
                </div>

                {!profile.overviewStats ? (
                  <p className="mt-3 text-sm text-muted">No stats yet.</p>
                ) : (
                  <div className="mt-4 grid grid-cols-2 gap-3 text-sm">
                    <div className="rounded-lg border border-border/60 bg-white/5 px-3 py-2">
                      <p className="text-xs text-muted">Win Rate</p>
                      <p className="text-sm font-semibold">
                        {formatPercent(profile.overviewStats.winRate)}
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

                <div className="mt-4">
                  <div className="flex items-center justify-between">
                    <p className="text-sm font-semibold text-fg">Role Breakdown</p>
                    {extraError ? (
                      <p className="text-xs text-red-300">{extraError}</p>
                    ) : null}
                  </div>
                  {!roles || roles.length === 0 ? (
                    <p className="mt-2 text-sm text-muted">No role data yet.</p>
                  ) : (
                    <div className="mt-3 grid gap-2">
                      {roles
                        .slice()
                        .sort((a, b) => b.games - a.games)
                        .map((r) => {
                          const raw = Number.isFinite(r.winRate) ? r.winRate : 0;
                          const pct = Math.max(
                            0,
                            Math.min(100, Math.abs(raw) >= 1.5 ? raw : raw * 100)
                          );
                          return (
                            <div key={r.role} className="grid gap-1">
                              <div className="flex items-center justify-between text-xs text-fg/80">
                                <span className="font-medium">{roleDisplayLabel(r.role)}</span>
                                <span>
                                  {r.games} games · {formatPercent(r.winRate)}
                                </span>
                              </div>
                              <div className="h-2 rounded-full border border-border/60 bg-black/20">
                                <div
                                  className="h-full rounded-full bg-gradient-to-r from-primary/70 to-primary-2/70"
                                  style={{ width: `${pct}%` }}
                                />
                              </div>
                            </div>
                          );
                        })}
                    </div>
                  )}
                </div>
              </Card>
            </div>
          ) : null}

          {tab === "champions" ? (
            <Card className="p-5">
              <div className="flex items-center justify-between">
                <h2 className="font-[var(--font-sora)] text-lg font-semibold">
                  Champions
                </h2>
                <Badge>{(profile.topChampions ?? []).length}</Badge>
              </div>
              <div className="mt-4 overflow-x-auto">
                <table className="w-full min-w-[520px] text-left text-sm">
                  <thead className="text-xs text-muted">
                    <tr>
                      <th className="py-2 pr-4">Champion</th>
                      <th className="py-2 pr-4">Games</th>
                      <th className="py-2 pr-4">Win</th>
                      <th className="py-2 pr-4">KDA</th>
                    </tr>
                  </thead>
                  <tbody>
                    {(profile.topChampions ?? []).map((c) => (
                      <tr
                        key={c.championId}
                        className="border-t border-border/50"
                      >
                        <td className="py-2 pr-4">
                          <div className="flex min-w-0 items-center gap-3">
                            {champIconUrl(staticData, c.championId) ? (
                              <Image
                                src={champIconUrl(staticData, c.championId)!}
                                alt={c.championName}
                                width={26}
                                height={26}
                                className="rounded-md"
                              />
                            ) : (
                              <div className="h-[26px] w-[26px] rounded-md border border-border/60 bg-black/30" />
                            )}
                            <Link
                              href={`/champions/${c.championId}`}
                              className="truncate text-sm font-medium hover:underline"
                            >
                              {staticData?.champions[String(c.championId)]?.name ??
                                c.championName}
                            </Link>
                          </div>
                        </td>
                        <td className="py-2 pr-4">{c.games}</td>
                        <td className="py-2 pr-4">{formatPercent(c.winRate)}</td>
                        <td className="py-2 pr-4">{c.kdaRatio.toFixed(2)}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </Card>
          ) : null}

          {tab === "matches" ? (
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
                    View full match history
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
                        <p className="text-xs text-muted">
                          {fmtKda(m.kills, m.deaths, m.assists)} · {m.csPerMin.toFixed(1)} CS/min
                        </p>
                      </div>
                    </div>
                    <span className="text-xs text-fg/80">{m.win ? "Win" : "Loss"}</span>
                  </div>
                ))}
              </div>
            </Card>
          ) : null}

          {tab === "live" ? (
            <LiveGameCard region={region} gameName={gameName} tagLine={tagLine} />
          ) : null}
        </div>
      )}
    </div>
  );
}
