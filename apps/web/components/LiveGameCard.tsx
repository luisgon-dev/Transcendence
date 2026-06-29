"use client";

import { useState } from "react";
import Image from "next/image";

import { Button } from "@/components/ui/Button";
import { Card } from "@/components/ui/Card";
import { cn } from "@/lib/cn";
import { formatDurationSeconds, formatPercent, winRateColorClass } from "@/lib/format";
import { rankTierColorClass } from "@/lib/ranks";
import { championSquareIconUrlById } from "@/lib/staticData";
import type { components } from "@transcendence/api-client";

type LiveGameResponse = components["schemas"]["LiveGameResponseDto"];
type LiveGameParticipant = components["schemas"]["LiveGameParticipantDto"];
type LiveGameParticipantAnalysis = components["schemas"]["LiveGameParticipantAnalysisDto"];
type TeamAnalysis = components["schemas"]["TeamAnalysisDto"];

// The BFF error envelope carries message/requestId, which the success DTO lacks.
type LiveGameErrorFields = { message?: string | null; requestId?: string | null };

const TEAMS: ReadonlyArray<{ id: number; label: string }> = [
  { id: 100, label: "Blue Side" },
  { id: 200, label: "Red Side" }
];

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
  analysis
}: {
  participant: LiveGameParticipant;
  analysis: LiveGameParticipantAnalysis | undefined;
}) {
  const name = participant.riotId?.trim() || "Unknown player";
  const recentWinRate = analysis?.recentWinRate;

  return (
    <li className="flex items-center gap-3 rounded-control border border-border bg-surface-2 px-2.5 py-2">
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
    </li>
  );
}

function TeamBlock({
  label,
  participants,
  analysisByPuuid,
  teamAnalysis
}: {
  label: string;
  participants: LiveGameParticipant[];
  analysisByPuuid: Map<string, LiveGameParticipantAnalysis>;
  teamAnalysis: TeamAnalysis | undefined;
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
          />
        ))}
      </ul>
    </section>
  );
}

export function LiveGameCard({
  region,
  gameName,
  tagLine
}: {
  region: string;
  gameName: string;
  tagLine: string;
}) {
  const [busy, setBusy] = useState(false);
  const [checked, setChecked] = useState(false);
  const [data, setData] = useState<LiveGameResponse | null>(null);
  const [error, setError] = useState<string | null>(null);

  async function check() {
    setBusy(true);
    setError(null);
    try {
      const res = await fetch(
        `/api/trn/app/summoners/${encodeURIComponent(region)}/${encodeURIComponent(
          gameName
        )}/${encodeURIComponent(tagLine)}/live-game`,
        { cache: "no-store" }
      );

      const json = (await res.json().catch(() => null)) as
        | (LiveGameResponse & LiveGameErrorFields)
        | null;
      if (!res.ok) {
        const msg = json?.message ?? `Live game request failed (${res.status}).`;
        const rid = json?.requestId ? ` Request ID: ${json.requestId}` : "";
        setError(`${msg}${rid}`);
        return;
      }

      setData(json);
    } catch (e) {
      setError(e instanceof Error ? e.message : "Live game error.");
    } finally {
      setBusy(false);
      setChecked(true);
    }
  }

  const participants = data?.participants ?? [];
  const inGame = data?.state === "IN_PROGRESS" || participants.length > 0;

  const analysisByPuuid = new Map<string, LiveGameParticipantAnalysis>();
  for (const entry of data?.analysis?.participants ?? []) {
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
      <div className="flex items-center justify-between">
        <div>
          <h3 className="type-section">Live Game</h3>
          <p className="type-ui mt-2 text-fg/75">
            Check whether this player is currently in a game.
          </p>
        </div>
        <Button variant="outline" onClick={check} disabled={busy}>
          {busy ? "Checking..." : "Check"}
        </Button>
      </div>

      {error ? <p className="type-ui mt-3 text-danger">{error}</p> : null}

      {!error && checked ? (
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
                  />
                );
              })}
            </div>
          </div>
        ) : (
          <p className="type-ui mt-4 text-muted">Not currently in a game.</p>
        )
      ) : !error ? (
        <p className="type-ui mt-4 text-muted">No live game data loaded yet.</p>
      ) : null}
    </Card>
  );
}
