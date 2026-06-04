import Image from "next/image";
import Link from "next/link";
import { AnimatePresence, motion } from "framer-motion";

import { Input } from "@/components/ui/Input";
import { Badge } from "@/components/ui/Badge";
import { Button } from "@/components/ui/Button";
import { Card } from "@/components/ui/Card";
import { Select } from "@/components/ui/Select";
import { Skeleton } from "@/components/ui/Skeleton";
import { RuneSetupDisplay } from "@/components/RuneSetupDisplay";
import {
  formatDateTimeMs,
  formatDurationSeconds,
  formatRelativeTime
} from "@/lib/format";
import { formatQueueLabel } from "@/lib/queues";
import { roleDisplayLabel } from "@/lib/roles";
import { encodeRiotIdPath } from "@/lib/riotid";
import {
  championIconUrl,
  itemIconUrl,
  runeIconUrl,
  summonerSpellIconUrl
} from "@/lib/staticData";

import {
  buildAlignedParticipantRows,
  buildRuneRowKey,
  hasRunes,
  isCurrentProfilePlayer,
  matchKdaRatio,
  normalizeInitialSort,
  participantDisplayName,
  type ChampionStatic,
  type ItemStatic,
  type MatchDetail,
  type MatchParticipant,
  type MatchSortOption,
  type MatchSummary,
  type PagedResultDto,
  type QueueOption,
  type RuneStatic,
  type SpellStatic
} from "@/components/lol-profile/shared";

type ChampionOption = {
  id: number;
  label: string;
};

type MatchHistorySectionProps = {
  region: string;
  gameName: string;
  tagLine: string;
  page: number;
  queue: string;
  championFilter: string;
  sort: MatchSortOption;
  history: PagedResultDto<MatchSummary> | null;
  historyBusy: boolean;
  historyError: string | null;
  visibleMatches: MatchSummary[];
  queueOptions: QueueOption[];
  championOptions: ChampionOption[];
  sortOptions: Array<{ value: MatchSortOption; label: string }>;
  expandedMatchId: string | null;
  details: Record<string, MatchDetail | null>;
  detailBusy: Record<string, boolean>;
  expandedRunes: Record<string, boolean>;
  championStatic: ChampionStatic | null;
  itemStatic: ItemStatic | null;
  spellStatic: SpellStatic | null;
  runeStatic: RuneStatic | null;
  prefersReducedMotion: boolean;
  onQueueChange(value: string): void;
  onChampionFilterChange(value: string): void;
  onSortChange(value: MatchSortOption): void;
  onToggleExpanded(matchId: string): void | Promise<void>;
  onToggleRuneRow(runeRowKey: string): void;
  onToggleAllRunesForMatch(matchId: string, participants: MatchParticipant[], expanded: boolean): void;
  onPreviousPage(): void;
  onNextPage(): void;
};

function sortPrimarySelections(selectionIds: number[], runeStatic: RuneStatic | null) {
  return selectionIds.slice().sort((a, b) => {
    const aSort = runeStatic?.runeSortById[String(a)] ?? Number.MAX_SAFE_INTEGER;
    const bSort = runeStatic?.runeSortById[String(b)] ?? Number.MAX_SAFE_INTEGER;
    return aSort - bSort;
  });
}

function MatchParticipantCard({
  participant,
  matchId,
  teamId,
  roleKey,
  rowIndex,
  region,
  gameName,
  tagLine,
  championStatic,
  itemStatic,
  spellStatic,
  runeStatic,
  expandedRunes,
  onToggleRuneRow
}: {
  participant: MatchParticipant | null;
  matchId: string;
  teamId: 100 | 200;
  roleKey: string;
  rowIndex: number;
  region: string;
  gameName: string;
  tagLine: string;
  championStatic: ChampionStatic | null;
  itemStatic: ItemStatic | null;
  spellStatic: SpellStatic | null;
  runeStatic: RuneStatic | null;
  expandedRunes: Record<string, boolean>;
  onToggleRuneRow(runeRowKey: string): void;
}) {
  if (!participant) {
    return (
      <div className="rounded-control border border-dashed border-border/35 bg-surface/35 px-3 py-3 text-xs text-muted">
        {roleDisplayLabel(roleKey)} unavailable
      </div>
    );
  }

  const isCurrent = isCurrentProfilePlayer(participant, gameName, tagLine);
  const champion = championStatic?.champions[String(participant.championId)];
  const itemIds = (participant.items ?? []).slice(0, 7);
  const cs = (participant.totalMinionsKilled + participant.neutralMinionsKilled).toLocaleString();
  const runeRowKey = buildRuneRowKey(matchId, teamId, rowIndex, participant);
  const runesExpanded = expandedRunes[runeRowKey] === true;
  const orderedPrimarySelections = sortPrimarySelections(
    participant.runes?.primarySelections ?? [],
    runeStatic
  );
  const hasRunesData = hasRunes(participant.runes);
  const primaryRuneId = orderedPrimarySelections[0] ?? 0;
  const primaryRuneMeta = runeStatic?.runeById[String(primaryRuneId)];
  const canExpandRunes = Boolean(runeStatic && hasRunesData);

  return (
    <div
      className={`rounded-control border px-3 py-3 ${
        isCurrent ? "border-primary/45 bg-primary/10 shadow-card" : "surface-subtle"
      }`}
    >
      <div className="grid items-center gap-2 sm:grid-cols-[minmax(0,1fr)_auto]">
        <div className="flex items-center gap-3">
          {champion && championStatic ? (
            <Image
              src={championIconUrl(championStatic.version, champion.id)}
              alt={champion.name}
              width={34}
              height={34}
              className="rounded-lg border border-border/50"
            />
          ) : (
            <div className="h-[34px] w-[34px] rounded-lg border border-border/50 bg-surface/70" />
          )}
          <div className="min-w-0 flex-1">
            {participant.gameName && participant.tagLine && !isCurrent ? (
              <Link
                href={`/lol/summoners/${region}/${encodeRiotIdPath({ gameName: participant.gameName, tagLine: participant.tagLine })}`}
                className="block truncate text-sm font-medium text-fg/95 transition-colors hover:text-primary hover:underline"
              >
                {participantDisplayName(participant.gameName, participant.tagLine)}
              </Link>
            ) : (
              <p className="truncate text-sm font-medium text-fg/95">
                {participantDisplayName(participant.gameName, participant.tagLine)}
              </p>
            )}
            <div className="type-caption mt-1 flex flex-wrap items-center gap-2 text-muted">
              <span>{participant.kills}/{participant.deaths}/{participant.assists}</span>
              <span>{cs} CS</span>
              <span>{participant.goldEarned.toLocaleString()}g</span>
              <span>{participant.totalDamageDealtToChampions.toLocaleString()} dmg</span>
            </div>
          </div>
        </div>

        <div className="flex items-center gap-1.5 lg:justify-end">
          {[participant.summonerSpell1Id, participant.summonerSpell2Id].map((spellId, spellIdx) => {
            const spellMeta = spellStatic?.spells[String(spellId)];
            return spellMeta && spellStatic ? (
              <Image
                key={`${spellId}-${spellIdx}`}
                src={summonerSpellIconUrl(spellStatic.version, spellMeta.id)}
                alt={spellMeta.name}
                title={spellMeta.name}
                width={18}
                height={18}
                className="rounded-md border border-border/40"
              />
            ) : (
              <div
                key={`${spellId}-${spellIdx}`}
                className="h-[18px] w-[18px] rounded-md border border-border/40 bg-surface/60"
              />
            );
          })}
        </div>
      </div>

      <div className="mt-3 flex flex-wrap items-center justify-between gap-2">
        <div className="flex flex-wrap items-center gap-1">
          {itemIds.map((itemId, itemIdx) => {
            if (!itemId) {
              return (
                <div
                  key={`empty-${itemIdx}`}
                  className="h-5 w-5 rounded-md border border-border/35 bg-surface/60"
                />
              );
            }

            const itemMeta = itemStatic?.items[String(itemId)];
            return itemStatic ? (
              <Image
                key={`${itemId}-${itemIdx}`}
                src={itemIconUrl(itemStatic.version, itemId)}
                alt={itemMeta?.name ?? `Item ${itemId}`}
                title={itemMeta?.name ?? `Item ${itemId}`}
                width={20}
                height={20}
                className="rounded-md border border-border/35"
              />
            ) : (
              <div
                key={`${itemId}-${itemIdx}`}
                className="h-5 w-5 rounded-md border border-border/35 bg-surface/60"
              />
            );
          })}
        </div>

        <button
          type="button"
          onClick={() => onToggleRuneRow(runeRowKey)}
          disabled={!canExpandRunes}
          className={`type-caption inline-flex items-center gap-1.5 rounded-full border px-2.5 py-1 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40 ${
            canExpandRunes
              ? "border-border/50 bg-surface-2/55 text-fg/85 hover:bg-surface-2/72"
              : "border-border/25 bg-surface/25 text-muted"
          }`}
          aria-expanded={runesExpanded}
          aria-label={runesExpanded ? "Hide runes" : "Show runes"}
        >
          {primaryRuneMeta ? (
            <Image
              src={runeIconUrl(primaryRuneMeta.icon)}
              alt={primaryRuneMeta.name}
              title={primaryRuneMeta.name}
              width={20}
              height={20}
              className="rounded-full border border-border/35 bg-surface-2/70 p-0.5"
            />
          ) : (
            <span className="h-5 w-5 rounded-full border border-border/35 bg-surface-2/70" />
          )}
          <span>{canExpandRunes ? (runesExpanded ? "Hide Runes" : "Show Runes") : "Runes Unavailable"}</span>
        </button>
      </div>

      {runeStatic && canExpandRunes && runesExpanded ? (
        <RuneSetupDisplay
          primaryStyleId={participant.runes?.primaryStyleId ?? 0}
          subStyleId={participant.runes?.subStyleId ?? 0}
          primarySelections={participant.runes?.primarySelections ?? []}
          subSelections={participant.runes?.subSelections ?? []}
          statShards={participant.runes?.statShards ?? []}
          runeById={runeStatic.runeById}
          styleById={runeStatic.styleById}
          runeSortById={runeStatic.runeSortById}
          iconSize={20}
          density="compact"
          className="mt-3"
        />
      ) : null}
    </div>
  );
}

function MatchDetailPanel({
  match,
  detail,
  region,
  gameName,
  tagLine,
  championStatic,
  itemStatic,
  spellStatic,
  runeStatic,
  expandedRunes,
  onToggleRuneRow,
  onToggleAllRunesForMatch
}: {
  match: MatchSummary;
  detail: MatchDetail;
  region: string;
  gameName: string;
  tagLine: string;
  championStatic: ChampionStatic | null;
  itemStatic: ItemStatic | null;
  spellStatic: SpellStatic | null;
  runeStatic: RuneStatic | null;
  expandedRunes: Record<string, boolean>;
  onToggleRuneRow(runeRowKey: string): void;
  onToggleAllRunesForMatch(matchId: string, participants: MatchParticipant[], expanded: boolean): void;
}) {
  const alignedRows = buildAlignedParticipantRows(detail.participants ?? []);
  const runeRowKeys: string[] = [];

  alignedRows.forEach((row, rowIndex) => {
    if (row.blue && hasRunes(row.blue.runes)) {
      runeRowKeys.push(buildRuneRowKey(match.matchId, 100, rowIndex, row.blue));
    }
    if (row.red && hasRunes(row.red.runes)) {
      runeRowKeys.push(buildRuneRowKey(match.matchId, 200, rowIndex, row.red));
    }
  });

  const allRunesExpanded =
    runeRowKeys.length > 0 && runeRowKeys.every((runeRowKey) => expandedRunes[runeRowKey] === true);
  const canToggleAllRunes = Boolean(runeStatic && runeRowKeys.length > 0);

  return (
    <div className="match-detail-shell p-3 md:p-4">
      <div className="mb-4 flex items-center justify-between gap-2">
        <div>
          <p className="type-kicker text-muted">Matchup details</p>
          <p className="mt-1 text-xs text-fg/62">
            Compare both teams with runes, spells, and item paths side by side.
          </p>
        </div>
        <button
          type="button"
          onClick={() => onToggleAllRunesForMatch(match.matchId, detail.participants ?? [], !allRunesExpanded)}
          disabled={!canToggleAllRunes}
          className={`type-caption inline-flex items-center gap-1 rounded-full border px-2.5 py-1 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40 ${
            canToggleAllRunes
              ? "border-border/55 bg-surface-2/55 text-fg/85 hover:bg-surface-2/72"
              : "border-border/25 bg-surface/25 text-muted"
          }`}
        >
          {allRunesExpanded ? "Collapse all runes" : "Expand all runes"}
        </button>
      </div>

      <div className="mb-3 grid grid-cols-2 gap-2">
        <p className="rounded-full border border-team-blue/18 bg-team-blue/10 px-3 py-1 text-xs font-semibold text-team-blue">
          Blue Team
        </p>
        <p className="rounded-full border border-team-red/18 bg-team-red/10 px-3 py-1 text-xs font-semibold text-team-red">
          Red Team
        </p>
      </div>

      <div className="grid gap-3">
        {alignedRows.map((row, rowIndex) => (
          <div key={`${match.matchId}-${row.roleKey}-${rowIndex}`} className="grid gap-2">
            <p className="type-overline px-1 text-muted">
              {roleDisplayLabel(row.roleKey)}
            </p>
            <div className="grid gap-2 sm:grid-cols-2">
              <MatchParticipantCard
                participant={row.blue}
                matchId={match.matchId}
                teamId={100}
                roleKey={row.roleKey}
                rowIndex={rowIndex}
                region={region}
                gameName={gameName}
                tagLine={tagLine}
                championStatic={championStatic}
                itemStatic={itemStatic}
                spellStatic={spellStatic}
                runeStatic={runeStatic}
                expandedRunes={expandedRunes}
                onToggleRuneRow={onToggleRuneRow}
              />
              <MatchParticipantCard
                participant={row.red}
                matchId={match.matchId}
                teamId={200}
                roleKey={row.roleKey}
                rowIndex={rowIndex}
                region={region}
                gameName={gameName}
                tagLine={tagLine}
                championStatic={championStatic}
                itemStatic={itemStatic}
                spellStatic={spellStatic}
                runeStatic={runeStatic}
                expandedRunes={expandedRunes}
                onToggleRuneRow={onToggleRuneRow}
              />
            </div>
          </div>
        ))}
      </div>
    </div>
  );
}

function MatchHistoryCard({
  match,
  expanded,
  detail,
  detailBusy,
  prefersReducedMotion,
  region,
  gameName,
  tagLine,
  championStatic,
  itemStatic,
  spellStatic,
  runeStatic,
  expandedRunes,
  onToggleExpanded,
  onToggleRuneRow,
  onToggleAllRunesForMatch
}: {
  match: MatchSummary;
  expanded: boolean;
  detail: MatchDetail | null;
  detailBusy: boolean;
  prefersReducedMotion: boolean;
  region: string;
  gameName: string;
  tagLine: string;
  championStatic: ChampionStatic | null;
  itemStatic: ItemStatic | null;
  spellStatic: SpellStatic | null;
  runeStatic: RuneStatic | null;
  expandedRunes: Record<string, boolean>;
  onToggleExpanded(matchId: string): void | Promise<void>;
  onToggleRuneRow(runeRowKey: string): void;
  onToggleAllRunesForMatch(matchId: string, participants: MatchParticipant[], expanded: boolean): void;
}) {
  const queueLabel = formatQueueLabel(match.queueType, match.queueId);
  const champion = championStatic?.champions[String(match.championId)];
  const championName = champion?.name ?? `Champion ${match.championId}`;
  const roleLabel = match.teamPosition ? roleDisplayLabel(match.teamPosition) : "Unknown";
  const orderedPrimarySelections = sortPrimarySelections(
    match.runesDetail?.primarySelections ?? [],
    runeStatic
  );
  const primaryRuneId = orderedPrimarySelections[0] ?? 0;
  const primaryRuneMeta = runeStatic?.runeById[String(primaryRuneId)];
  const subStyleMeta = runeStatic?.styleById[String(match.runesDetail?.subStyleId ?? 0)];
  const spellIds = [match.summonerSpell1Id, match.summonerSpell2Id];
  const itemSlots = Array.from({ length: 7 }, (_, idx) => match.items[idx] ?? 0);
  const matchMetaId = `match-meta-${match.matchId}`;
  const matchPanelId = `match-panel-${match.matchId}`;

  return (
    <motion.div
      layout={!prefersReducedMotion}
      className={`match-card-shell ${
        match.win ? "match-card-shell--win border-success/28" : "match-card-shell--loss border-danger/28"
      } rounded-panel border`}
    >
      <span
        className={`absolute inset-y-0 left-0 w-1.5 ${match.win ? "bg-success/75" : "bg-danger/75"}`}
        aria-hidden="true"
      />
      <button
        className="relative z-10 w-full px-4 py-4 text-left focus-visible:outline-none md:px-5 md:py-5"
        onClick={() => void onToggleExpanded(match.matchId)}
        aria-expanded={expanded}
        aria-controls={matchPanelId}
        aria-describedby={matchMetaId}
        aria-label={`${match.win ? "Victory" : "Defeat"} on ${championName}. KDA ${match.kills}/${match.deaths}/${match.assists}. ${formatDurationSeconds(match.durationSeconds)}.`}
      >
        <div className="grid gap-4 xl:grid-cols-[minmax(0,1.1fr)_minmax(280px,0.95fr)_auto] xl:items-center">
          <div className="grid gap-3">
            <div className="flex flex-wrap items-center gap-2">
              <span
                className={`type-overline rounded-full px-2.5 py-1 ${
                  match.win ? "bg-success/15 text-success" : "bg-danger/15 text-danger"
                }`}
              >
                {match.win ? "VICTORY" : "DEFEAT"}
              </span>
              <span className="type-caption surface-chip rounded-full px-2.5 py-1 font-medium text-fg/92">
                {queueLabel}
              </span>
              <span className="type-caption surface-chip rounded-full px-2.5 py-1 font-medium text-fg/92">
                {roleLabel}
              </span>
              <span className="type-caption surface-chip rounded-full px-2.5 py-1 font-medium text-fg/92">
                {formatDurationSeconds(match.durationSeconds)}
              </span>
            </div>

            <div className="grid gap-3 sm:grid-cols-[auto_minmax(0,1fr)] sm:items-center">
              <div className="flex min-w-0 items-center gap-3">
                {champion && championStatic ? (
                  <Image
                    src={championIconUrl(championStatic.version, champion.id)}
                    alt={championName}
                    width={52}
                    height={52}
                    className="rounded-control border border-border/60 shadow-soft"
                  />
                ) : (
                  <div className="h-[52px] w-[52px] rounded-control border border-border/60 bg-surface/60" />
                )}
                <div className="min-w-0">
                  <div className="flex flex-wrap items-end gap-x-3 gap-y-1">
                    <p className="truncate text-lg font-semibold">{championName}</p>
                    <p className="text-xs text-fg/55">{formatRelativeTime(match.matchDate)}</p>
                  </div>
                  <p id={matchMetaId} className="mt-1 text-sm text-fg/72">
                    {formatDateTimeMs(match.matchDate)}
                  </p>
                </div>
              </div>
            </div>
          </div>

          <div className="grid gap-3">
            <div className="surface-subtle grid gap-3 rounded-card p-3 sm:grid-cols-[auto_minmax(0,1fr)] sm:items-center">
              <div className="flex items-center gap-3">
                <div className="flex items-center gap-1.5" aria-label="Summoner spells">
                  {spellIds.map((spellId, spellIdx) => {
                    const spellMeta = spellStatic?.spells[String(spellId)];
                    return spellMeta && spellStatic ? (
                      <Image
                        key={`${match.matchId}-spell-${spellIdx}-${spellId}`}
                        src={summonerSpellIconUrl(spellStatic.version, spellMeta.id)}
                        alt={spellMeta.name}
                        title={spellMeta.name}
                        width={24}
                        height={24}
                        className="rounded-md border border-border/50"
                      />
                    ) : (
                      <div
                        key={`${match.matchId}-spell-empty-${spellIdx}-${spellId}`}
                        className="h-6 w-6 rounded-md border border-border/40 bg-surface/60"
                        aria-hidden="true"
                      />
                    );
                  })}
                </div>

                <div className="flex items-center gap-1.5" aria-label="Rune preview">
                  {primaryRuneMeta ? (
                    <Image
                      src={runeIconUrl(primaryRuneMeta.icon)}
                      alt={primaryRuneMeta.name}
                      title={primaryRuneMeta.name}
                      width={24}
                      height={24}
                      className="rounded-full border border-border/40 bg-surface-2/70 p-0.5"
                    />
                  ) : (
                    <span
                      className="h-6 w-6 rounded-full border border-border/40 bg-surface-2/70"
                      aria-hidden="true"
                    />
                  )}
                  {subStyleMeta ? (
                    <Image
                      src={runeIconUrl(subStyleMeta.icon)}
                      alt={subStyleMeta.name}
                      title={subStyleMeta.name}
                      width={24}
                      height={24}
                      className="rounded-full border border-border/40 bg-surface-2/70 p-0.5"
                    />
                  ) : (
                    <span
                      className="h-6 w-6 rounded-full border border-border/40 bg-surface-2/70"
                      aria-hidden="true"
                    />
                  )}
                </div>
              </div>

              <div className="flex flex-wrap items-center gap-1.5 sm:justify-end" aria-label="Item build preview">
                {itemSlots.map((itemId, itemIdx) => {
                  if (!itemId) {
                    return (
                      <div
                        key={`${match.matchId}-item-empty-${itemIdx}`}
                        className="h-6 w-6 rounded-md border border-border/35 bg-surface/60"
                        aria-hidden="true"
                      />
                    );
                  }

                  const itemMeta = itemStatic?.items[String(itemId)];
                  return itemStatic ? (
                    <Image
                      key={`${match.matchId}-item-${itemIdx}-${itemId}`}
                      src={itemIconUrl(itemStatic.version, itemId)}
                      alt={itemMeta?.name ?? `Item ${itemId}`}
                      title={itemMeta?.name ?? `Item ${itemId}`}
                      width={24}
                      height={24}
                      className="rounded-md border border-border/35"
                    />
                  ) : (
                    <div
                      key={`${match.matchId}-item-loading-${itemIdx}-${itemId}`}
                      className="h-6 w-6 rounded-md border border-border/35 bg-surface/60"
                      aria-hidden="true"
                    />
                  );
                })}
              </div>
            </div>

            <div className="flex flex-wrap gap-2 text-xs text-fg/70">
              <span className="surface-chip rounded-full px-2.5 py-1">
                {match.damageToChamps.toLocaleString()} damage
              </span>
              <span className="surface-chip rounded-full px-2.5 py-1">
                {match.visionScore} vision
              </span>
              <span className="surface-chip rounded-full px-2.5 py-1">
                {match.csPerMin.toFixed(1)} CS/min
              </span>
            </div>
          </div>

          <div className="grid gap-2 xl:justify-items-end">
            <div className="surface-subtle rounded-card px-4 py-3 text-right">
              <p className="text-xl font-semibold leading-tight tracking-tight text-fg">
                <span>{match.kills}</span>/<span className="text-danger/90">{match.deaths}</span>/<span>{match.assists}</span>
              </p>
              <p className="mt-1 text-xs font-medium text-fg/82">
                {matchKdaRatio(match).toFixed(2)} KDA
              </p>
            </div>
            <span className="type-overline text-fg/65">
              {expanded ? "Collapse details" : "Expand details"}
            </span>
          </div>
        </div>
      </button>

      <AnimatePresence initial={false}>
        {expanded ? (
          <motion.div
            id={matchPanelId}
            initial={prefersReducedMotion ? undefined : { height: 0, opacity: 0 }}
            animate={prefersReducedMotion ? undefined : { height: "auto", opacity: 1 }}
            exit={prefersReducedMotion ? undefined : { height: 0, opacity: 0 }}
            className="overflow-hidden"
            style={prefersReducedMotion ? { height: "auto", opacity: 1 } : undefined}
          >
            <div className="mt-4 border-t border-white/8 pt-4">
              {detailBusy ? <Skeleton className="h-12 w-full" /> : null}
              {!detailBusy && !detail ? (
                <p className="text-sm text-fg/75">Detailed rows are unavailable for this match.</p>
              ) : null}
              {detail ? (
                <MatchDetailPanel
                  match={match}
                  detail={detail}
                  region={region}
                  gameName={gameName}
                  tagLine={tagLine}
                  championStatic={championStatic}
                  itemStatic={itemStatic}
                  spellStatic={spellStatic}
                  runeStatic={runeStatic}
                  expandedRunes={expandedRunes}
                  onToggleRuneRow={onToggleRuneRow}
                  onToggleAllRunesForMatch={onToggleAllRunesForMatch}
                />
              ) : null}
            </div>
          </motion.div>
        ) : null}
      </AnimatePresence>
    </motion.div>
  );
}

export function MatchHistorySection({
  region,
  gameName,
  tagLine,
  page,
  queue,
  championFilter,
  sort,
  history,
  historyBusy,
  historyError,
  visibleMatches,
  queueOptions,
  championOptions,
  sortOptions,
  expandedMatchId,
  details,
  detailBusy,
  expandedRunes,
  championStatic,
  itemStatic,
  spellStatic,
  runeStatic,
  prefersReducedMotion,
  onQueueChange,
  onChampionFilterChange,
  onSortChange,
  onToggleExpanded,
  onToggleRuneRow,
  onToggleAllRunesForMatch,
  onPreviousPage,
  onNextPage
}: MatchHistorySectionProps) {
  return (
    <section className="grid gap-5">
      <Card className="profile-section-card rounded-panel p-5 md:p-6">
        <div className="grid gap-5 xl:grid-cols-[minmax(0,1fr)_minmax(310px,auto)] xl:items-start">
          <div className="grid gap-4">
            <div>
              <p className="type-kicker text-muted">Match history</p>
              <h2 className="type-panel-title mt-2">
                Recent results with clearer scan paths
              </h2>
              <p className="mt-2 max-w-2xl text-sm text-fg/70">
                Filter by queue or champion, then open any game for side-by-side lane and team detail.
              </p>
            </div>

            <div className="flex flex-wrap gap-x-4 gap-y-2">
              {queueOptions.map((option) => (
                <button
                  key={option.value}
                  className="control-tab type-ui focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/45"
                  onClick={() => onQueueChange(option.value)}
                  aria-pressed={option.value === queue}
                  data-active={option.value === queue}
                >
                  {option.label}
                </button>
              ))}
            </div>
          </div>

          <div className="surface-card grid gap-3 rounded-card p-4">
            <div>
              <label htmlFor="match-champion-filter" className="sr-only">
                Filter matches by champion
              </label>
              <Input
                id="match-champion-filter"
                list="match-champion-options"
                placeholder="Filter champion (name or ID)"
                value={championFilter}
                onChange={(event) => onChampionFilterChange(event.currentTarget.value)}
                className="h-10 min-w-[220px] bg-surface-2/55 text-sm shadow-inset"
                spellCheck={false}
              />
              <datalist id="match-champion-options">
                {championOptions.map((option) => (
                  <option key={`champion-filter-${option.id}`} value={option.label} />
                ))}
              </datalist>
            </div>

            <Select
              value={sort}
              onValueChange={(v) => onSortChange(normalizeInitialSort(v))}
              ariaLabel="Sort matches"
              options={sortOptions}
              className="w-full"
            />

            <div className="flex flex-wrap items-center gap-2">
              <Badge className="surface-chip text-fg/72">
                Page {history?.page ?? page}/{history?.totalPages ?? 1}
              </Badge>
              <Badge className="surface-chip text-fg/72">
                {(history?.totalCount ?? 0).toLocaleString()} total
              </Badge>
              <Badge className="surface-chip text-fg/72">{visibleMatches.length} shown</Badge>
            </div>
          </div>
        </div>

        {historyError ? <p className="mt-4 text-sm text-danger">{historyError}</p> : null}
        {historyBusy && !history ? <Skeleton className="mt-4 h-16 w-full" /> : null}

        <div className="mt-5 grid gap-4">
          {visibleMatches.map((match) => (
            <MatchHistoryCard
              key={match.matchId}
              match={match}
              expanded={expandedMatchId === match.matchId}
              detail={details[match.matchId]}
              detailBusy={detailBusy[match.matchId] === true}
              prefersReducedMotion={prefersReducedMotion}
              region={region}
              gameName={gameName}
              tagLine={tagLine}
              championStatic={championStatic}
              itemStatic={itemStatic}
              spellStatic={spellStatic}
              runeStatic={runeStatic}
              expandedRunes={expandedRunes}
              onToggleExpanded={onToggleExpanded}
              onToggleRuneRow={onToggleRuneRow}
              onToggleAllRunesForMatch={onToggleAllRunesForMatch}
            />
          ))}

          {!historyBusy && visibleMatches.length === 0 ? (
            <p className="surface-subtle rounded-card px-4 py-4 text-sm text-fg/80">
              No matches found for the current queue/champion filters.
            </p>
          ) : null}
        </div>

        <div className="mt-5 flex items-center justify-between">
          <Button
            size="sm"
            variant="outline"
            disabled={page <= 1 || historyBusy}
            onClick={onPreviousPage}
          >
            Previous
          </Button>
          <Button
            size="sm"
            variant="outline"
            disabled={historyBusy || (history ? history.page >= history.totalPages : false)}
            onClick={onNextPage}
          >
            Next
          </Button>
        </div>

        <p className="mt-3 text-xs text-muted">
          Match history:{" "}
          <Link
            href={`/lol/summoners/${region}/${encodeRiotIdPath({ gameName, tagLine })}/matches`}
            className="text-primary hover:underline"
          >
            /lol/summoners/.../matches
          </Link>
        </p>
      </Card>
    </section>
  );
}
