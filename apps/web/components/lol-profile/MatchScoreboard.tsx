"use client";

import { useState } from "react";
import Image from "next/image";

import { ParticipantRuneCard } from "@/components/lol-profile/ParticipantRuneCard";
import { ScoreboardTeamTable } from "@/components/lol-profile/ScoreboardTeamTable";
import { SegmentedControl } from "@/components/ui/SegmentedControl";
import { roleDisplayLabel } from "@/lib/roles";
import { championIconUrl } from "@/lib/staticData";

import {
  buildAlignedParticipantRows,
  matchKdaRatio,
  participantDisplayName,
  type ChampionStatic,
  type ItemStatic,
  type MatchDetail,
  type MatchParticipant,
  type MatchTeamObjectives,
  type RuneStatic,
  type SpellStatic
} from "@/components/lol-profile/shared";

type ScoreboardTab = "overview" | "runes";

const TABS = [
  { value: "overview" as const, label: "Overview" },
  { value: "runes" as const, label: "Runes" }
];

function RuneTabCell({
  participant,
  roleKey,
  championStatic,
  runeStatic
}: {
  participant: MatchParticipant | null;
  roleKey: string;
  championStatic: ChampionStatic | null;
  runeStatic: RuneStatic | null;
}) {
  if (!participant) {
    return (
      <div className="rounded-control border border-dashed border-border/35 bg-surface/35 px-3 py-3 type-caption text-muted">
        {roleDisplayLabel(roleKey)} unavailable
      </div>
    );
  }

  const champion = championStatic?.champions[String(participant.championId)];

  return (
    <div className="surface-subtle grid content-start gap-2 rounded-control p-3">
      <div className="flex items-center gap-2">
        {champion && championStatic ? (
          <Image
            src={championIconUrl(championStatic.version, champion.id)}
            alt={champion.name}
            width={28}
            height={28}
            className="rounded-lg border border-border/55"
          />
        ) : (
          <div className="h-7 w-7 rounded-lg border border-border/55 bg-surface/70" />
        )}
        <div className="min-w-0">
          <p className="truncate text-sm font-medium text-fg/95">
            {participantDisplayName(participant.gameName, participant.tagLine)}
          </p>
          <p className="type-caption text-muted tabular-nums">
            {participant.kills}/{participant.deaths}/{participant.assists} · {matchKdaRatio(participant).toFixed(2)} KDA
          </p>
        </div>
      </div>
      <ParticipantRuneCard participant={participant} runeStatic={runeStatic} />
    </div>
  );
}

function TeamObjectivesSummary({ team, side }: { team: MatchTeamObjectives | undefined; side: "blue" | "red" }) {
  const items = team
    ? [
        team.dragon.kills > 0 ? `${team.dragon.kills} Dragon` : null,
        team.baron.kills > 0 ? `${team.baron.kills} Baron` : null,
        team.riftHerald.kills > 0 ? `${team.riftHerald.kills} Herald` : null,
        team.horde.kills > 0 ? `${team.horde.kills} Grubs` : null,
        `${team.tower.kills} Towers`,
        team.inhibitor.kills > 0 ? `${team.inhibitor.kills} Inhib` : null
      ].filter(Boolean)
    : [];

  return (
    <div className="flex flex-wrap items-center gap-x-2 gap-y-1">
      <span className={`type-overline font-semibold ${side === "blue" ? "text-team-blue" : "text-team-red"}`}>
        {side === "blue" ? "Blue" : "Red"}
      </span>
      {team?.firstBlood ? (
        <span className="surface-chip type-caption rounded-full px-2 py-0.5 text-fg/80">First Blood</span>
      ) : null}
      <span className="type-caption text-fg/72 tabular-nums">{items.length > 0 ? items.join(" · ") : "—"}</span>
    </div>
  );
}

function ObjectivesStrip({ objectives }: { objectives: MatchTeamObjectives[] }) {
  const blue = objectives.find((o) => o.teamId === 100);
  const red = objectives.find((o) => o.teamId === 200);
  return (
    <div className="surface-subtle flex flex-wrap items-center justify-between gap-x-6 gap-y-2 rounded-control px-3 py-2">
      <TeamObjectivesSummary team={blue} side="blue" />
      <TeamObjectivesSummary team={red} side="red" />
    </div>
  );
}

// Compact, tabbed post-game view that replaces the old 10-tall-cards detail panel.
// Overview = two dense team scoreboards (op.gg-style); Runes = role-aligned rune
// pages for side-by-side comparison. Runes also surface via a hover tooltip on each
// scoreboard row's keystone, so a single player can be inspected without leaving Overview.
export function MatchScoreboard({
  detail,
  region,
  gameName,
  tagLine,
  championStatic,
  itemStatic,
  spellStatic,
  runeStatic
}: {
  detail: MatchDetail;
  region: string;
  gameName: string;
  tagLine: string;
  championStatic: ChampionStatic | null;
  itemStatic: ItemStatic | null;
  spellStatic: SpellStatic | null;
  runeStatic: RuneStatic | null;
}) {
  const [tab, setTab] = useState<ScoreboardTab>("overview");
  const participants = detail.participants ?? [];
  const blue = participants.filter((p) => p.teamId === 100);
  const red = participants.filter((p) => p.teamId === 200);
  const blueBans = detail.bans?.find((b) => b.teamId === 100)?.bannedChampionIds ?? [];
  const redBans = detail.bans?.find((b) => b.teamId === 200)?.bannedChampionIds ?? [];
  // Normalize damage bars to the whole lobby's top dealer so the carry reads across both teams.
  const matchMaxDamage = Math.max(1, ...participants.map((p) => p.totalDamageDealtToChampions));
  const alignedRows = buildAlignedParticipantRows(participants);

  return (
    <div className="flex flex-col gap-3">
      <SegmentedControl<ScoreboardTab>
        options={TABS}
        value={tab}
        onValueChange={setTab}
        ariaLabel="Match detail view"
        className="w-fit"
      />

      {tab === "overview" ? (
        <div className="flex flex-col gap-3">
          {detail.objectives && detail.objectives.length > 0 ? (
            <ObjectivesStrip objectives={detail.objectives} />
          ) : null}
          <ScoreboardTeamTable
            participants={blue}
            teamId={100}
            durationSeconds={detail.duration}
            maxDamage={matchMaxDamage}
            bans={blueBans}
            region={region}
            gameName={gameName}
            tagLine={tagLine}
            championStatic={championStatic}
            itemStatic={itemStatic}
            spellStatic={spellStatic}
            runeStatic={runeStatic}
          />
          <ScoreboardTeamTable
            participants={red}
            teamId={200}
            durationSeconds={detail.duration}
            maxDamage={matchMaxDamage}
            bans={redBans}
            region={region}
            gameName={gameName}
            tagLine={tagLine}
            championStatic={championStatic}
            itemStatic={itemStatic}
            spellStatic={spellStatic}
            runeStatic={runeStatic}
          />
        </div>
      ) : (
        <div className="flex flex-col gap-3">
          {alignedRows.map((row, rowIndex) => (
            <div key={`${row.roleKey}-${rowIndex}`} className="flex flex-col gap-2">
              <p className="type-overline px-1 text-muted">{roleDisplayLabel(row.roleKey)}</p>
              <div className="grid grid-cols-1 gap-2 sm:grid-cols-2">
                <RuneTabCell participant={row.blue} roleKey={row.roleKey} championStatic={championStatic} runeStatic={runeStatic} />
                <RuneTabCell participant={row.red} roleKey={row.roleKey} championStatic={championStatic} runeStatic={runeStatic} />
              </div>
            </div>
          ))}
        </div>
      )}
    </div>
  );
}
