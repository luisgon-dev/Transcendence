import Image from "next/image";
import Link from "next/link";

import { ParticipantRuneCard } from "@/components/lol-profile/ParticipantRuneCard";
import { PerformanceIndicator } from "@/components/lol-profile/PerformanceIndicator";
import { useStaticData } from "@/components/lol-profile/StaticDataContext";
import { TableScroll, Table, Th, Td } from "@/components/ui/Table";
import { Tooltip } from "@/components/ui/Tooltip";
import { cn } from "@/lib/cn";
import { formatCompactNumber } from "@/lib/format";
import { itemDisplayName } from "@/lib/gameDisplay";
import { encodeRiotIdPath } from "@/lib/riotid";
import { roleDisplayLabel } from "@/lib/roles";
import {
  championIconUrl,
  itemIconUrl,
  runeIconUrl,
  summonerSpellIconUrl
} from "@/lib/staticData";

import {
  isCurrentProfilePlayer,
  matchKdaRatio,
  normalizeRoleKey,
  participantDisplayName,
  primaryKeystoneId,
  type MatchParticipant
} from "@/components/lol-profile/shared";

const ROLE_ORDER: Record<string, number> = { TOP: 0, JUNGLE: 1, MIDDLE: 2, BOTTOM: 3, UTILITY: 4 };

function RuneTrigger({ participant }: { participant: MatchParticipant }) {
  const { runeStatic } = useStaticData();
  const keystoneId = primaryKeystoneId(participant.runes, runeStatic);
  const keystone = runeStatic?.runeById[String(keystoneId)];
  const subStyle = runeStatic?.styleById[String(participant.runes?.subStyleId ?? 0)];

  const icons = (
    <span className="flex flex-col items-center gap-0.5">
      {keystone ? (
        <Image
          src={runeIconUrl(keystone.icon)}
          alt={keystone.name}
          width={18}
          height={18}
          className="rounded-full border border-border/40 bg-surface-2/70"
        />
      ) : (
        <span className="h-[18px] w-[18px] rounded-full border border-border/40 bg-surface-2/70" />
      )}
      {subStyle ? (
        <Image
          src={runeIconUrl(subStyle.icon)}
          alt={subStyle.name}
          width={14}
          height={14}
          className="rounded-full"
        />
      ) : (
        <span className="h-[14px] w-[14px] rounded-full bg-surface-2/70" />
      )}
    </span>
  );

  if (!runeStatic) return icons;

  return (
    <Tooltip
      side="right"
      className="max-w-none p-2"
      content={
        <div className="w-[244px]">
          <ParticipantRuneCard participant={participant} />
        </div>
      }
    >
      <button
        type="button"
        aria-label={`Runes for ${participantDisplayName(participant.gameName, participant.tagLine)}`}
        className="rounded-full focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/45"
      >
        {icons}
      </button>
    </Tooltip>
  );
}

function ScoreboardRow({
  participant,
  durationSeconds,
  maxDamage,
  region,
  gameName,
  tagLine
}: {
  participant: MatchParticipant;
  durationSeconds: number;
  maxDamage: number;
  region: string;
  gameName: string;
  tagLine: string;
}) {
  const { championStatic, itemStatic, spellStatic } = useStaticData();
  const isCurrent = isCurrentProfilePlayer(participant, gameName, tagLine);
  const champion = championStatic?.champions[String(participant.championId)];
  const cs = participant.totalMinionsKilled + participant.neutralMinionsKilled;
  const csPerMin = durationSeconds > 0 ? cs / (durationSeconds / 60) : 0;
  const totalDmg = participant.totalDamageDealtToChampions;
  const physDmg = participant.physicalDamageDealtToChampions ?? 0;
  const magicDmg = participant.magicDamageDealtToChampions ?? 0;
  const trueDmg = participant.trueDamageDealtToChampions ?? 0;
  const splitSum = physDmg + magicDmg + trueDmg;
  const damageFillPct = maxDamage > 0 ? Math.min(100, (totalDmg / maxDamage) * 100) : 0;
  const itemSlots = Array.from({ length: 7 }, (_, idx) => participant.items?.[idx] ?? 0);
  const displayName = participantDisplayName(participant.gameName, participant.tagLine);
  const canLink = Boolean(participant.gameName && participant.tagLine && !isCurrent);

  return (
    <tr className={cn("border-t border-border/45", isCurrent && "bg-primary/10")}>
      <Td className="min-w-[200px]">
        <div className="flex items-center gap-2">
          <div className="relative shrink-0">
            {champion && championStatic ? (
              <Image
                src={championIconUrl(championStatic.version, champion.id)}
                alt={champion.name}
                width={34}
                height={34}
                className="rounded-lg border border-border/55"
              />
            ) : (
              <div className="h-[34px] w-[34px] rounded-lg border border-border/55 bg-surface/70" />
            )}
            {participant.champLevel ? (
              <span className="absolute -bottom-1 -right-1 rounded-full border border-border/60 bg-surface px-1 text-[0.625rem] font-semibold leading-tight text-fg/85">
                {participant.champLevel}
              </span>
            ) : null}
          </div>

          <span className="flex shrink-0 flex-col gap-0.5" aria-label="Summoner spells">
            {[participant.summonerSpell1Id, participant.summonerSpell2Id].map((spellId, spellIdx) => {
              const spellMeta = spellStatic?.spells[String(spellId)];
              return spellMeta && spellStatic ? (
                <Image
                  key={`${spellId}-${spellIdx}`}
                  src={summonerSpellIconUrl(spellStatic.version, spellMeta.id)}
                  alt={spellMeta.name}
                  title={spellMeta.name}
                  width={16}
                  height={16}
                  className="rounded border border-border/40"
                />
              ) : (
                <span key={`${spellId}-${spellIdx}`} className="h-4 w-4 rounded border border-border/40 bg-surface/60" />
              );
            })}
          </span>

          <RuneTrigger participant={participant} />

          <div className="min-w-0">
            {canLink ? (
              <Link
                href={`/lol/summoners/${region}/${encodeRiotIdPath({ gameName: participant.gameName as string, tagLine: participant.tagLine as string })}`}
                className="block truncate rounded text-sm font-medium text-fg/95 transition-colors hover:text-primary hover:underline focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/45"
              >
                {displayName}
              </Link>
            ) : (
              <p className={cn("truncate text-sm font-medium", isCurrent ? "text-primary" : "text-fg/95")}>
                {displayName}
              </p>
            )}
            <div className="mt-0.5 flex flex-wrap items-center gap-1.5">
              <p className="type-caption text-muted">{roleDisplayLabel(normalizeRoleKey(participant.teamPosition))}</p>
              <PerformanceIndicator performance={participant.performance} compact />
            </div>
          </div>
        </div>
      </Td>

      <Td align="right" className="whitespace-nowrap">
        <span className="text-sm tabular-nums text-fg/92">
          {participant.kills}<span className="text-muted"> / </span>
          <span className="text-loss">{participant.deaths}</span>
          <span className="text-muted"> / </span>{participant.assists}
        </span>
        <p className="type-caption text-muted tabular-nums">{matchKdaRatio(participant).toFixed(2)} KDA</p>
      </Td>

      <Td align="right" className="whitespace-nowrap">
        <span className="text-sm tabular-nums text-fg/88">{cs}</span>
        <p className="type-caption text-muted tabular-nums">{csPerMin.toFixed(1)}/min</p>
      </Td>

      <Td align="right" className="whitespace-nowrap">
        <span className="text-sm tabular-nums text-fg/88">{totalDmg.toLocaleString()}</span>
        {splitSum > 0 ? (
          <Tooltip
            side="left"
            content={
              <div className="grid gap-0.5 tabular-nums">
                <span><span className="text-loss">■</span> Physical {physDmg.toLocaleString()}</span>
                <span><span className="text-team-blue">■</span> Magic {magicDmg.toLocaleString()}</span>
                <span><span className="text-fg">■</span> True {trueDmg.toLocaleString()}</span>
              </div>
            }
          >
            <button
              type="button"
              aria-label={`Damage ${totalDmg.toLocaleString()}: ${physDmg.toLocaleString()} physical, ${magicDmg.toLocaleString()} magic, ${trueDmg.toLocaleString()} true`}
              className="ml-auto mt-1 block h-1.5 w-16 overflow-hidden rounded-full bg-surface-2/70 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/45"
            >
              <span className="flex h-full" style={{ width: `${damageFillPct}%` }}>
                <span className="h-full" style={{ width: `${(physDmg / splitSum) * 100}%`, backgroundColor: "var(--color-loss)" }} />
                <span className="h-full" style={{ width: `${(magicDmg / splitSum) * 100}%`, backgroundColor: "var(--color-team-blue)" }} />
                <span className="h-full" style={{ width: `${(trueDmg / splitSum) * 100}%`, backgroundColor: "var(--color-fg)" }} />
              </span>
            </button>
          </Tooltip>
        ) : null}
      </Td>

      <Td align="right" className="whitespace-nowrap text-sm tabular-nums text-fg/82">
        {participant.visionScore}
      </Td>

      <Td align="right" className="whitespace-nowrap text-sm tabular-nums text-fg/82">
        {formatCompactNumber(participant.goldEarned)}
      </Td>

      <Td>
        <div className="flex flex-wrap items-center gap-1">
          {itemSlots.map((itemId, itemIdx) => {
            if (!itemId) {
              return (
                <span key={`empty-${itemIdx}`} className="h-[22px] w-[22px] rounded border border-border/35 bg-surface/55" />
              );
            }
            const itemMeta = itemStatic?.items[String(itemId)];
            return itemStatic ? (
              <Image
                key={`${itemId}-${itemIdx}`}
                src={itemIconUrl(itemStatic.version, itemId)}
                alt={itemDisplayName(itemMeta)}
                title={itemDisplayName(itemMeta)}
                width={22}
                height={22}
                className="rounded border border-border/35"
              />
            ) : (
              <span key={`${itemId}-${itemIdx}`} className="h-[22px] w-[22px] rounded border border-border/35 bg-surface/55" />
            );
          })}
        </div>
      </Td>
    </tr>
  );
}

export function ScoreboardTeamTable({
  participants,
  teamId,
  durationSeconds,
  maxDamage,
  bans,
  region,
  gameName,
  tagLine
}: {
  participants: MatchParticipant[];
  teamId: 100 | 200;
  durationSeconds: number;
  maxDamage: number;
  bans: number[];
  region: string;
  gameName: string;
  tagLine: string;
}) {
  const { championStatic } = useStaticData();
  const ordered = participants
    .slice()
    .sort((a, b) => (ROLE_ORDER[normalizeRoleKey(a.teamPosition)] ?? 9) - (ROLE_ORDER[normalizeRoleKey(b.teamPosition)] ?? 9));
  const win = participants[0]?.win ?? false;
  const totalKills = participants.reduce((sum, p) => sum + p.kills, 0);
  const totalGold = participants.reduce((sum, p) => sum + p.goldEarned, 0);
  const isBlue = teamId === 100;

  return (
    <div className="flex flex-col gap-1.5">
      <div className="flex items-center justify-between gap-2 px-3">
        <div className="flex items-center gap-2">
          <span className={cn("type-overline font-semibold", isBlue ? "text-team-blue" : "text-team-red")}>
            {isBlue ? "Blue Team" : "Red Team"}
          </span>
          <span className={cn("type-caption font-semibold", win ? "text-win" : "text-loss")}>
            {win ? "Victory" : "Defeat"}
          </span>
        </div>
        <div className="flex items-center gap-3">
          {bans.length > 0 ? (
            <div className="flex items-center gap-1" aria-label="Bans" title="Champion bans">
              {bans.map((championId, idx) => {
                const champion = championStatic?.champions[String(championId)];
                return champion && championStatic ? (
                  <Image
                    key={`ban-${championId}-${idx}`}
                    src={championIconUrl(championStatic.version, champion.id)}
                    alt={`Banned: ${champion.name}`}
                    title={`Banned: ${champion.name}`}
                    width={16}
                    height={16}
                    className="rounded border border-border/40 opacity-60 grayscale"
                  />
                ) : (
                  <span key={`ban-${championId}-${idx}`} className="h-4 w-4 rounded border border-border/40 bg-surface/60" />
                );
              })}
            </div>
          ) : null}
          <div className="type-caption flex items-center gap-3 text-muted tabular-nums">
            <span>{totalKills} kills</span>
            <span>{formatCompactNumber(totalGold)} gold</span>
          </div>
        </div>
      </div>

      <TableScroll className="match-detail-shell">
        <Table className="min-w-[760px]">
          <thead>
            <tr>
              <Th>{isBlue ? "Blue Team" : "Red Team"}</Th>
              <Th align="right">KDA</Th>
              <Th align="right">CS</Th>
              <Th align="right">Damage</Th>
              <Th align="right">Vision</Th>
              <Th align="right">Gold</Th>
              <Th>Items</Th>
            </tr>
          </thead>
          <tbody>
            {ordered.map((participant, idx) => (
              <ScoreboardRow
                key={`${participant.puuid ?? participant.championId}-${idx}`}
                participant={participant}
                durationSeconds={durationSeconds}
                maxDamage={maxDamage}
                region={region}
                gameName={gameName}
                tagLine={tagLine}
              />
            ))}
          </tbody>
        </Table>
      </TableScroll>
    </div>
  );
}
