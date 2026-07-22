"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import Image from "next/image";

import { Button } from "@/components/ui/Button";
import { Card } from "@/components/ui/Card";
import { Skeleton } from "@/components/ui/Skeleton";
import { cn } from "@/lib/cn";
import {
  formatDurationSeconds,
  formatPercent,
  kdaColorClass,
  winRateColorClass
} from "@/lib/format";
import { rankTierColorClass } from "@/lib/ranks";
import {
  championSquareIconUrlById,
  runeIconUrl,
  summonerSpellIconUrl
} from "@/lib/staticData";
import type { components } from "@transcendence/api-client";

type LiveGameResponse = components["schemas"]["LiveGameResponseDto"];
type LiveGameParticipant = components["schemas"]["LiveGameParticipantDto"];
type LiveGameParticipantAnalysis = components["schemas"]["LiveGameParticipantAnalysisDto"];
type TeamAnalysis = components["schemas"]["TeamAnalysisDto"];
type EnrichedParticipant = LiveGameParticipant & {
  perkIds?: number[] | null;
  perkStyleId?: number | null;
  perkSubStyleId?: number | null;
};
type ChampionPoolEntry = {
  championId: number;
  games: number;
  winRate: number;
};
type EnrichedParticipantAnalysis = LiveGameParticipantAnalysis & {
  recentGames?: number;
  currentStreak?: number;
  championPool?: ChampionPoolEntry[] | null;
};
type SpellStaticData = {
  version: string;
  spells: Record<string, { id: string; name: string }>;
};
type RuneStaticData = {
  runeById: Record<string, { name: string; icon: string }>;
};
type LiveGameStaticData = {
  spells: SpellStaticData | null;
  runes: RuneStaticData | null;
};

// The BFF error envelope carries message/requestId, which the success DTO lacks.
type LiveGameErrorFields = { message?: string | null; requestId?: string | null };
type LiveGameProbeAccepted = components["schemas"]["LiveGameProbeAcceptedResponse"] & LiveGameErrorFields;

const TEAMS: ReadonlyArray<{ id: number; label: string }> = [
  { id: 100, label: "Blue Side" },
  { id: 200, label: "Red Side" }
];
const LIVE_REFRESH_INTERVAL_MS = 60_000;

function titleCaseTier(value: string | null | undefined): string {
  if (!value) return "";
  return value.charAt(0).toUpperCase() + value.slice(1).toLowerCase();
}

function formatRankLabel(analysis: LiveGameParticipantAnalysis | undefined): string {
  if (!analysis?.rankTier) return "Unranked";
  const tier = titleCaseTier(analysis.rankTier);
  const division = analysis.rankDivision ? ` ${analysis.rankDivision}` : "";
  const lp = analysis.leaguePoints != null ? ` · ${analysis.leaguePoints} LP` : "";
  return `${tier}${division}${lp}`;
}

function ScoutChip({ tone, children }: { tone: "success" | "warning"; children: string }) {
  return (
    <span
      className={cn(
        "rounded-control px-2 py-0.5 text-[11px] leading-tight",
        tone === "success" ? "bg-success/10 text-success" : "bg-warning/10 text-warning"
      )}
    >
      {children}
    </span>
  );
}

function ParticipantRow({
  participant,
  analysis,
  detailed,
  staticData
}: {
  participant: EnrichedParticipant;
  analysis: EnrichedParticipantAnalysis | undefined;
  detailed: boolean;
  staticData: LiveGameStaticData;
}) {
  const name = participant.riotId?.trim() || "Unknown player";
  const recentWinRate = analysis?.recentWinRate;
  const streak = analysis?.currentStreak ?? 0;
  const pool = analysis?.championPool ?? [];
  const perkIds = participant.perkIds ?? [];
  const visiblePerks = perkIds.filter((id) => id < 5000 || id >= 6000).slice(0, 2);

  return (
    <li className="rounded-control border border-border bg-surface-2 px-2.5 py-2">
      <div className="flex items-center gap-3">
        {participant.championId != null ? (
          <Image
            src={championSquareIconUrlById(participant.championId)}
            alt={`Champion ${participant.championId}`}
            width={36}
            height={36}
            className="size-9 shrink-0 rounded-control border border-border bg-surface"
          />
        ) : (
          <span className="grid size-9 shrink-0 place-items-center rounded-control border border-border bg-surface text-[11px] font-medium text-muted">
            ?
          </span>
        )}
        <div className="min-w-0 flex-1">
          <p className="type-ui truncate text-fg">{name}</p>
          <p className={cn("text-xs leading-tight", rankTierColorClass(analysis?.rankTier))}>
            {formatRankLabel(analysis)}
          </p>
        </div>
        {recentWinRate != null ? (
          <div className="shrink-0 text-right">
            <p className={cn("text-sm font-medium tabular-nums", winRateColorClass(recentWinRate))}>
              {formatPercent(recentWinRate, { decimals: 0 })}
            </p>
            <p className="text-[10px] uppercase tracking-wide text-muted">recent WR</p>
          </div>
        ) : null}
      </div>

      {detailed ? (
        <div className="mt-2 flex flex-wrap items-center gap-x-3 gap-y-2 border-t border-border/55 pt-2 text-[11px] text-muted">
          {streak !== 0 ? (
            <span className={streak > 0 ? "text-success" : "text-danger"}>
              {Math.abs(streak)} {streak > 0 ? "win" : "loss"} streak
            </span>
          ) : null}
          {analysis?.recentKda != null ? (
            <span className={cn("tabular-nums", kdaColorClass(analysis.recentKda))}>
              {analysis.recentKda.toFixed(2)} KDA
            </span>
          ) : null}
          {pool.length > 0 ? (
            <span className="inline-flex items-center gap-1" aria-label="Recent champion pool">
              {pool.map((entry) => (
                <span key={entry.championId} className="inline-flex items-center gap-1">
                  <Image
                    src={championSquareIconUrlById(entry.championId)}
                    alt={`Champion ${entry.championId}`}
                    width={20}
                    height={20}
                    className="size-5 rounded-sm border border-border"
                  />
                  <span className="tabular-nums">{entry.games}</span>
                </span>
              ))}
            </span>
          ) : null}
          {staticData.spells ? (
            <span className="inline-flex items-center gap-1" aria-label="Summoner spells">
              {[participant.spell1Id, participant.spell2Id].map((spellId) => {
                const spell = staticData.spells?.spells[String(spellId)];
                return spell ? (
                  <Image
                    key={spellId}
                    src={summonerSpellIconUrl(staticData.spells!.version, spell.id)}
                    alt={spell.name}
                    title={spell.name}
                    width={20}
                    height={20}
                    className="size-5 rounded-sm border border-border"
                  />
                ) : null;
              })}
            </span>
          ) : null}
          {staticData.runes && visiblePerks.length > 0 ? (
            <span className="inline-flex items-center gap-1" aria-label="Selected runes">
              {visiblePerks.map((perkId) => {
                const rune = staticData.runes?.runeById[String(perkId)];
                return rune ? (
                  <Image
                    key={perkId}
                    src={runeIconUrl(rune.icon)}
                    alt={rune.name}
                    title={rune.name}
                    width={20}
                    height={20}
                    className="size-5 rounded-full bg-surface"
                  />
                ) : null;
              })}
            </span>
          ) : null}
        </div>
      ) : null}
    </li>
  );
}

function TeamBlock({
  label,
  participants,
  analysisByPuuid,
  teamAnalysis,
  detailed,
  staticData
}: {
  label: string;
  participants: EnrichedParticipant[];
  analysisByPuuid: Map<string, EnrichedParticipantAnalysis>;
  teamAnalysis: TeamAnalysis | undefined;
  detailed: boolean;
  staticData: LiveGameStaticData;
}) {
  const strengths = teamAnalysis?.strengths ?? [];
  const weaknesses = teamAnalysis?.weaknesses ?? [];

  return (
    <section className="rounded-card border border-border bg-surface p-3">
      <div className="flex items-baseline justify-between gap-2">
        <h4 className="type-ui font-medium text-fg">{label}</h4>
        {teamAnalysis?.estimatedWinProbability != null ? (
          <span className="text-xs text-muted">
            Win prob{" "}
            <span
              className={cn(
                "font-medium tabular-nums",
                winRateColorClass(teamAnalysis.estimatedWinProbability)
              )}
            >
              {formatPercent(teamAnalysis.estimatedWinProbability)}
            </span>
          </span>
        ) : null}
      </div>

      {teamAnalysis?.averageRecentWinRate != null ? (
        <p className="mt-1 text-xs text-muted">
          Avg recent WR{" "}
          <span
            className={cn("tabular-nums", winRateColorClass(teamAnalysis.averageRecentWinRate))}
          >
            {formatPercent(teamAnalysis.averageRecentWinRate)}
          </span>
        </p>
      ) : null}

      {strengths.length > 0 || weaknesses.length > 0 ? (
        <div className="mt-2 flex flex-wrap gap-1.5">
          {strengths.map((item, index) => (
            <ScoutChip key={`s-${index}-${item}`} tone="success">
              {item}
            </ScoutChip>
          ))}
          {weaknesses.map((item, index) => (
            <ScoutChip key={`w-${index}-${item}`} tone="warning">
              {item}
            </ScoutChip>
          ))}
        </div>
      ) : null}

      <ul className="mt-3 flex flex-col gap-1.5">
        {participants.map((participant, index) => (
          <ParticipantRow
            key={participant.puuid ?? `${participant.championId ?? "?"}-${index}`}
            participant={participant}
            analysis={participant.puuid ? analysisByPuuid.get(participant.puuid) : undefined}
            detailed={detailed}
            staticData={staticData}
          />
        ))}
      </ul>
    </section>
  );
}

export function LiveGameCard({
  region,
  gameName,
  tagLine,
  detailed = false
}: {
  region: string;
  gameName: string;
  tagLine: string;
  detailed?: boolean;
}) {
  const [busy, setBusy] = useState(false);
  const [checked, setChecked] = useState(false);
  const [checkedAt, setCheckedAt] = useState<Date | null>(null);
  const [data, setData] = useState<LiveGameResponse | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [staticData, setStaticData] = useState<LiveGameStaticData>({ spells: null, runes: null });
  const requestRef = useRef<AbortController | null>(null);

  useEffect(() => {
    if (!detailed) return;
    let active = true;
    void Promise.all([
      fetch("/api/static/spells", { cache: "force-cache" }).then(async (response) =>
        response.ok ? ((await response.json()) as SpellStaticData) : null
      ),
      fetch("/api/static/runes", { cache: "force-cache" }).then(async (response) =>
        response.ok ? ((await response.json()) as RuneStaticData) : null
      )
    ])
      .then(([spells, runes]) => {
        if (active) setStaticData({ spells, runes });
      })
      .catch(() => undefined);
    return () => {
      active = false;
    };
  }, [detailed]);

  const check = useCallback(async () => {
    if (requestRef.current) return;
    setBusy(true);
    setError(null);
    const controller = new AbortController();
    requestRef.current = controller;
    try {
      const liveGamePath =
        `/api/trn/app/lol/summoners/${encodeURIComponent(region)}/${encodeURIComponent(
          gameName
        )}/${encodeURIComponent(tagLine)}/live-game`;
      const probeStartedAt = Date.now();
      const probeRes = await fetch(`${liveGamePath}/probe`, {
        method: "POST",
        cache: "no-store",
        signal: controller.signal
      });
      const probeJson = (await probeRes.json().catch(() => null)) as LiveGameProbeAccepted | null;
      if (!probeRes.ok) {
        const msg = probeJson?.message ?? `Live game probe failed (${probeRes.status}).`;
        const rid = probeJson?.requestId ? ` Request ID: ${probeJson.requestId}` : "";
        setError(`${msg}${rid}`);
        return;
      }

      const retryDelayMs = Math.min(5_000, Math.max(0, (probeJson?.retryAfterSeconds ?? 2) * 1_000));
      let json: (LiveGameResponse & LiveGameErrorFields) | null = null;
      for (let attempt = 0; attempt < 4; attempt += 1) {
        if (retryDelayMs > 0)
          await new Promise((resolve) => window.setTimeout(resolve, retryDelayMs));
        if (controller.signal.aborted) return;

        const res = await fetch(liveGamePath, { cache: "no-store", signal: controller.signal });
        json = (await res.json().catch(() => null)) as (LiveGameResponse & LiveGameErrorFields) | null;
        if (!res.ok) {
          const msg = json?.message ?? `Live game request failed (${res.status}).`;
          const rid = json?.requestId ? ` Request ID: ${json.requestId}` : "";
          setError(`${msg}${rid}`);
          return;
        }

        const observedAt = json?.lastUpdatedUtc ? Date.parse(json.lastUpdatedUtc) : Number.NaN;
        if (Number.isFinite(observedAt) && observedAt >= probeStartedAt - 1_000) break;
      }

      setData(json);
      setCheckedAt(new Date());
    } catch (e) {
      if (controller.signal.aborted) return;
      setError(e instanceof Error ? e.message : "Live game error.");
    } finally {
      if (requestRef.current === controller) {
        requestRef.current = null;
        if (!controller.signal.aborted) {
          setBusy(false);
          setChecked(true);
        }
      }
    }
  }, [gameName, region, tagLine]);

  // Live state is time-sensitive, so check as soon as the card mounts instead of requiring the user
  // to discover and press a one-shot button.
  useEffect(() => {
    void check();
    return () => {
      const request = requestRef.current;
      request?.abort();
      if (requestRef.current === request) requestRef.current = null;
    };
  }, [check]);

  const participants = (data?.participants ?? []) as EnrichedParticipant[];
  const inGame = data?.state === "in_game" || data?.state === "IN_PROGRESS" || participants.length > 0;

  // Once a game is detected, keep the scout view fresh at a deliberately light cadence. A timeout
  // (rather than an interval) avoids overlapping a slow request.
  useEffect(() => {
    if (!inGame) return;
    const timer = window.setTimeout(() => void check(), LIVE_REFRESH_INTERVAL_MS);
    return () => window.clearTimeout(timer);
  }, [check, checkedAt, inGame]);

  const analysisByPuuid = new Map<string, EnrichedParticipantAnalysis>();
  for (const entry of (data?.analysis?.participants ?? []) as EnrichedParticipantAnalysis[]) {
    if (entry.puuid) analysisByPuuid.set(entry.puuid, entry);
  }

  const teamAnalysisById = new Map<number, TeamAnalysis>();
  for (const team of data?.analysis?.teams ?? []) {
    if (team.teamId != null) teamAnalysisById.set(team.teamId, team);
  }

  const metaParts = [data?.queueType, data?.map].filter(
    (part): part is string => Boolean(part && part.trim())
  );

  return (
    <Card className="p-5">
      <div className="flex items-start justify-between gap-3">
        <div>
          <h3 className="type-section">Live Game</h3>
          <p className="type-ui mt-2 text-fg/75">
            Automatically checks whether this player is in a game.
          </p>
          {checkedAt ? (
            <p className="mt-1 text-xs tabular-nums text-muted">
              Checked {checkedAt.toLocaleTimeString([], { hour: "numeric", minute: "2-digit" })}
              {inGame ? " · Auto-refreshes every 60 sec" : ""}
            </p>
          ) : null}
          {data?.dataAgeSeconds != null ? (
            <p className="mt-1 text-xs tabular-nums text-muted">
              Worker snapshot {Math.max(0, data.dataAgeSeconds).toLocaleString()} sec old
            </p>
          ) : null}
        </div>
        <Button variant="outline" onClick={() => void check()} disabled={busy}>
          {busy ? "Checking…" : "Re-check"}
        </Button>
      </div>

      <div aria-live="polite">
        {error ? <p className="type-ui mt-3 text-danger">{error}</p> : null}

        {busy && !checked ? (
          <div className="mt-4 grid gap-3" aria-label="Checking live game">
            <Skeleton className="h-4 w-40" />
            <div className="grid gap-3 sm:grid-cols-2">
              <Skeleton className="h-32 w-full" />
              <Skeleton className="h-32 w-full" />
            </div>
          </div>
        ) : checked && data ? (
          inGame ? (
            <div className="mt-4">
              <div className="flex flex-wrap items-center gap-x-3 gap-y-1">
                <span className="inline-flex items-center gap-1.5 type-ui text-fg">
                  <span className="relative flex size-2">
                    <span className="absolute inline-flex size-2 animate-ping rounded-full bg-success/60 motion-reduce:hidden" />
                    <span className="relative inline-flex size-2 rounded-full bg-success" />
                  </span>
                  Live
                </span>
                {metaParts.map((part) => (
                  <span key={part} className="type-ui text-fg/80">
                    <span className="mr-3 text-muted">{"·"}</span>
                    {part}
                  </span>
                ))}
                <span className="text-muted">{"·"}</span>
                <span className="type-ui tabular-nums text-fg/80">
                  {formatDurationSeconds(data?.gameLengthSeconds)}
                </span>
              </div>

              <div className="mt-4 grid gap-4 sm:grid-cols-2">
                {TEAMS.map((team) => {
                  const teamParticipants = participants.filter((p) => p.teamId === team.id);
                  if (teamParticipants.length === 0) return null;
                  return (
                    <TeamBlock
                      key={team.id}
                      label={team.label}
                      participants={teamParticipants}
                      analysisByPuuid={analysisByPuuid}
                      teamAnalysis={teamAnalysisById.get(team.id)}
                      detailed={detailed}
                      staticData={staticData}
                    />
                  );
                })}
              </div>
            </div>
          ) : (
            <p className="type-ui mt-4 text-muted">Not currently in a game.</p>
          )
        ) : !error && !busy ? (
          <p className="type-ui mt-4 text-muted">Waiting to check live game status.</p>
        ) : null}
      </div>
    </Card>
  );
}
