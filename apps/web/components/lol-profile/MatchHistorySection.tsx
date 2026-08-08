import Image from "next/image";
import Link from "next/link";
import { AnimatePresence, motion } from "framer-motion";

import { Input } from "@/components/ui/Input";
import { Badge } from "@/components/ui/Badge";
import { Button } from "@/components/ui/Button";
import { Card } from "@/components/ui/Card";
import { Select } from "@/components/ui/Select";
import { Skeleton } from "@/components/ui/Skeleton";
import { SegmentedControl } from "@/components/ui/SegmentedControl";
import { ChevronRightIcon } from "@/components/ui/icons";
import { cn } from "@/lib/cn";
import { MatchScoreboard } from "@/components/lol-profile/MatchScoreboard";
import { PerformanceIndicator } from "@/components/lol-profile/PerformanceIndicator";
import { useStaticData } from "@/components/lol-profile/StaticDataContext";
import {
  formatCompactNumber,
  formatDateTimeMs,
  formatDurationSeconds,
  formatRelativeTime
} from "@/lib/format";
import { championDisplayName, itemDisplayName } from "@/lib/gameDisplay";
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
  MATCH_PLACEHOLDER_ROWS,
  matchKdaRatio,
  normalizeInitialSort,
  sortRuneSelections,
  type MatchDetail,
  type MatchSortOption,
  type MatchSummary,
  type PagedResultDto,
  type QueueOption
} from "@/components/lol-profile/shared";

type ChampionOption = {
  id: number;
  label: string;
};

export type MatchHistoryIdentity = {
  region: string;
  gameName: string;
  tagLine: string;
  summonerId: string;
};

export type MatchHistoryFilters = {
  queue: string;
  championFilter: string;
  sort: MatchSortOption;
  queueOptions: QueueOption[];
  championOptions: ChampionOption[];
  sortOptions: Array<{ value: MatchSortOption; label: string }>;
  onQueueChange(value: string): void;
  onChampionFilterChange(value: string): void;
  onSortChange(value: MatchSortOption): void;
};

export type MatchHistoryPageState = {
  page: number;
  history: PagedResultDto<MatchSummary> | null;
  historyBusy: boolean;
  historyError: string | null;
  visibleMatches: MatchSummary[];
  onPreviousPage(): void;
  onNextPage(): void;
};

export type MatchHistoryExpansion = {
  expandedMatchId: string | null;
  details: Record<string, MatchDetail | null>;
  detailBusy: Record<string, boolean>;
  onToggleExpanded(matchId: string): void | Promise<void>;
};

type MatchHistorySectionProps = {
  identity: MatchHistoryIdentity;
  filters: MatchHistoryFilters;
  pageState: MatchHistoryPageState;
  expansion: MatchHistoryExpansion;
  prefersReducedMotion: boolean;
};

function MatchHistoryCard({
  match,
  expanded,
  detail,
  detailBusy,
  prefersReducedMotion,
  region,
  gameName,
  tagLine,
  summonerId,
  onToggleExpanded
}: {
  match: MatchSummary;
  expanded: boolean;
  detail: MatchDetail | null;
  detailBusy: boolean;
  prefersReducedMotion: boolean;
  region: string;
  gameName: string;
  tagLine: string;
  summonerId: string;
  onToggleExpanded(matchId: string): void | Promise<void>;
}) {
  const { championStatic, itemStatic, spellStatic, runeStatic } = useStaticData();
  const queueLabel = formatQueueLabel(match.queueType, match.queueId);
  const champion = championStatic?.champions[String(match.championId)];
  const championName = championDisplayName(champion);
  const roleLabel = match.teamPosition ? roleDisplayLabel(match.teamPosition) : "Unknown";
  const primaryRuneId = sortRuneSelections(match.runesDetail?.primarySelections ?? [], runeStatic?.runeSortById)[0] ?? 0;
  const primaryRuneMeta = runeStatic?.runeById[String(primaryRuneId)];
  const subStyleMeta = runeStatic?.styleById[String(match.runesDetail?.subStyleId ?? 0)];
  const spellIds = [match.summonerSpell1Id, match.summonerSpell2Id];
  const itemSlots = Array.from({ length: 7 }, (_, idx) => match.items[idx] ?? 0);
  const relativeTime = formatRelativeTime(match.matchDate);
  const exactDateTime = formatDateTimeMs(match.matchDate);
  const matchMetaId = `match-meta-${match.matchId}`;
  const matchPanelId = `match-panel-${match.matchId}`;

  return (
    <div
      className={`match-card-shell min-w-0 max-w-full ${
        match.win ? "match-card-shell--win border-win/28" : "match-card-shell--loss border-loss/28"
      } rounded-panel border`}
    >
      <button
        className="match-card-summary relative z-10 block w-full min-w-0 max-w-full px-4 py-4 text-left focus-visible:outline-none md:px-5 md:py-5"
        onClick={() => void onToggleExpanded(match.matchId)}
        aria-expanded={expanded}
        aria-controls={matchPanelId}
        aria-describedby={matchMetaId}
        aria-label={`${match.win ? "Victory" : "Defeat"} on ${championName}. KDA ${match.kills}/${match.deaths}/${match.assists}. ${formatDurationSeconds(match.durationSeconds)}.`}
      >
        <div className="match-snapshot-grid">
          <div className="match-snapshot-identity min-w-0">
            <div className="flex flex-wrap items-center gap-x-2 gap-y-1">
              <span
                className={`type-overline font-semibold ${match.win ? "text-win" : "text-loss"}`}
              >
                {match.win ? "VICTORY" : "DEFEAT"}
              </span>
              <span className="text-border-strong" aria-hidden="true">·</span>
              <span className="type-caption font-medium text-fg/82">{queueLabel}</span>
              <span className="text-border-strong" aria-hidden="true">·</span>
              <span className="type-caption tabular-nums text-fg/72">
                {formatDurationSeconds(match.durationSeconds)}
              </span>
            </div>

            <div className="mt-3 flex min-w-0 items-center gap-3">
              <div className="flex shrink-0 items-center gap-2">
                {champion && championStatic ? (
                  <Image
                    src={championIconUrl(championStatic.version, champion.id)}
                    alt={championName}
                    width={48}
                    height={48}
                    className="rounded-control border border-border/60 shadow-soft"
                  />
                ) : (
                  <div className="h-12 w-12 rounded-control border border-border/60 bg-surface/60" />
                )}

                <div className="grid grid-cols-2 items-center gap-1.5">
                  <div className="flex flex-col gap-1" aria-label="Summoner spells">
                    {spellIds.map((spellId, spellIdx) => {
                      const spellMeta = spellStatic?.spells[String(spellId)];
                      return spellMeta && spellStatic ? (
                        <Image
                          key={`${match.matchId}-spell-${spellIdx}-${spellId}`}
                          src={summonerSpellIconUrl(spellStatic.version, spellMeta.id)}
                          alt={spellMeta.name}
                          title={spellMeta.name}
                          width={21}
                          height={21}
                          className="rounded border border-border/50"
                        />
                      ) : (
                        <span
                          key={`${match.matchId}-spell-empty-${spellIdx}-${spellId}`}
                          className="h-[21px] w-[21px] rounded border border-border/40 bg-surface/60"
                          aria-hidden="true"
                        />
                      );
                    })}
                  </div>

                  <div className="flex flex-col items-center gap-1" aria-label="Rune preview">
                    {primaryRuneMeta ? (
                      <Image
                        src={runeIconUrl(primaryRuneMeta.icon)}
                        alt={primaryRuneMeta.name}
                        title={primaryRuneMeta.name}
                        width={22}
                        height={22}
                        className="rounded-full border border-border/40 bg-surface-2/70 p-0.5"
                      />
                    ) : (
                      <span
                        className="h-[22px] w-[22px] rounded-full border border-border/40 bg-surface-2/70"
                        aria-hidden="true"
                      />
                    )}
                    {subStyleMeta ? (
                      <Image
                        src={runeIconUrl(subStyleMeta.icon)}
                        alt={subStyleMeta.name}
                        title={subStyleMeta.name}
                        width={18}
                        height={18}
                        className="rounded-full border border-border/30 bg-surface-2/70 p-0.5"
                      />
                    ) : (
                      <span
                        className="h-[18px] w-[18px] rounded-full border border-border/30 bg-surface-2/70"
                        aria-hidden="true"
                      />
                    )}
                  </div>
                </div>
              </div>

              <div className="min-w-0">
                <p className="truncate text-lg font-semibold leading-tight text-fg">{championName}</p>
                <p className="mt-1 truncate type-caption text-muted">
                  {roleLabel}
                  <span className="px-1.5" aria-hidden="true">·</span>
                  <time id={matchMetaId} title={exactDateTime} aria-label={`${relativeTime}; ${exactDateTime}`}>
                    {relativeTime}
                  </time>
                </p>
              </div>
            </div>
          </div>

          <div className="match-snapshot-loadout min-w-0">
            <div className="flex flex-wrap items-center gap-1" aria-label="Item build preview">
              {itemSlots.map((itemId, itemIdx) => {
                const itemMeta = itemStatic?.items[String(itemId)];
                return (
                  <span
                    key={`${match.matchId}-item-slot-${itemIdx}`}
                    className={cn(
                      "inline-flex shrink-0",
                      itemIdx === 6 && "ml-1 border-l border-border/65 pl-2"
                    )}
                  >
                    {!itemId ? (
                      <span
                        className="h-7 w-7 rounded border border-border/35 bg-surface/55"
                        aria-hidden="true"
                      />
                    ) : itemStatic ? (
                      <Image
                        src={itemIconUrl(itemStatic.version, itemId)}
                        alt={itemDisplayName(itemMeta)}
                        title={itemDisplayName(itemMeta)}
                        width={28}
                        height={28}
                        className="rounded border border-border/35"
                      />
                    ) : (
                      <span
                        className="h-7 w-7 rounded border border-border/35 bg-surface/55"
                        aria-hidden="true"
                      />
                    )}
                  </span>
                );
              })}
            </div>

            <div className="mt-2 flex flex-wrap items-center gap-x-3 gap-y-1 type-caption text-fg/68">
              <span className="tabular-nums">{formatCompactNumber(match.damageToChamps)} dmg</span>
              <span className="tabular-nums">{match.visionScore} vision</span>
              <span className="tabular-nums">
                {match.csPerMin.toFixed(1)} CS/min
              </span>
            </div>
          </div>

          <div className="match-snapshot-stats">
            <div className="flex flex-col items-start gap-2 @min-[36rem]:items-end">
              <PerformanceIndicator performance={match.performance} compact />
              <p className="text-xl font-semibold leading-tight tracking-tight text-fg tabular-nums">
                <span>{match.kills}</span>
                <span className="text-muted"> / </span>
                <span className="text-loss/90">{match.deaths}</span>
                <span className="text-muted"> / </span>
                <span>{match.assists}</span>
              </p>
              <p className="mt-1 type-caption font-medium text-fg/78 tabular-nums">
                {matchKdaRatio(match).toFixed(2)} KDA
              </p>
            </div>
            <span className="flex items-center justify-end gap-1.5 type-overline text-muted">
              {expanded ? "Collapse" : "Details"}
              <ChevronRightIcon
                className={cn(
                  "size-3 shrink-0 transition-transform duration-150",
                  expanded && "rotate-90"
                )}
              />
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
            transition={
              prefersReducedMotion
                ? undefined
                : { height: { duration: 0.28, ease: [0.25, 1, 0.5, 1] }, opacity: { duration: 0.18 } }
            }
            className="overflow-hidden"
            style={prefersReducedMotion ? { height: "auto", opacity: 1 } : undefined}
          >
            <div className="mt-4 border-t border-border/55 px-4 pt-4 md:px-5">
              {detailBusy ? <Skeleton className="h-12 w-full" /> : null}
              {!detailBusy && !detail ? (
                <p className="text-sm text-fg/75">Detailed stats are unavailable for this match.</p>
              ) : null}
              {detail ? (
                <MatchScoreboard
                  detail={detail}
                  summonerId={summonerId}
                  region={region}
                  gameName={gameName}
                  tagLine={tagLine}
                />
              ) : null}
            </div>
          </motion.div>
        ) : null}
      </AnimatePresence>
    </div>
  );
}

export function MatchHistorySection({
  identity: { region, gameName, tagLine, summonerId },
  filters: {
    queue,
    championFilter,
    sort,
    queueOptions,
    championOptions,
    sortOptions,
    onQueueChange,
    onChampionFilterChange,
    onSortChange
  },
  pageState: {
    page,
    history,
    historyBusy,
    historyError,
    visibleMatches,
    onPreviousPage,
    onNextPage
  },
  expansion: { expandedMatchId, details, detailBusy, onToggleExpanded },
  prefersReducedMotion,
}: MatchHistorySectionProps) {
  return (
    <section className="flex min-w-0 max-w-full flex-col gap-5">
      <Card className="profile-section-card min-w-0 max-w-full rounded-panel p-5 md:p-6">
        <div className="flex flex-col gap-3">
          <div className="flex flex-wrap items-end justify-between gap-3">
            <div>
              <p className="type-overline text-muted">Match history</p>
              <h2 className="type-panel-title mt-1">Matches</h2>
            </div>
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

          <div className="flex flex-wrap items-center gap-2 border-t border-border/70 pt-3">
            <div className="max-w-full overflow-x-auto">
              <SegmentedControl
                options={queueOptions}
                value={queue}
                onValueChange={onQueueChange}
                ariaLabel="Filter matches by queue"
              />
            </div>
            <div className="ml-auto flex flex-wrap items-center gap-2">
              <label htmlFor="match-champion-filter" className="sr-only">
                Filter matches by champion
              </label>
              <Input
                id="match-champion-filter"
                list="match-champion-options"
                placeholder="Filter champion"
                value={championFilter}
                onChange={(event) => onChampionFilterChange(event.currentTarget.value)}
                className="h-9 w-[180px] bg-surface-2/55 text-sm shadow-inset"
                spellCheck={false}
              />
              <datalist id="match-champion-options">
                {championOptions.map((option) => (
                  <option key={`champion-filter-${option.id}`} value={option.label} />
                ))}
              </datalist>
              <Select
                value={sort}
                onValueChange={(v) => onSortChange(normalizeInitialSort(v))}
                ariaLabel="Sort matches"
                options={sortOptions}
                className="w-[160px]"
              />
            </div>
          </div>
        </div>

        {historyError ? <p className="mt-4 text-sm text-danger">{historyError}</p> : null}

        <div className="mt-5 flex flex-col gap-4">
          {/* First load only (`!history`). Reserves the same rows as the dynamic-import
              fallback and the route skeleton, so each handoff between them is a repaint
              rather than a reflow. A refetch (paging, filters) keeps the previous page
              on screen and needs no placeholder. */}
          {historyBusy && !history
            ? Array.from({ length: MATCH_PLACEHOLDER_ROWS }).map((_, i) => (
                <Skeleton key={i} className="h-28 w-full rounded-panel" />
              ))
            : null}

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
              summonerId={summonerId}
              onToggleExpanded={onToggleExpanded}
            />
          ))}

          {!historyBusy && visibleMatches.length === 0 ? (
            <p className="surface-subtle rounded-card px-4 py-4 text-sm text-fg/80">
              {queue !== "ALL" || championFilter.trim() ? (
                "No matches found for the current queue/champion filters."
              ) : (
                "No ranked matches are recorded yet. Use Update Now to fetch the latest history."
              )}
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
          <Link
            href={`/lol/summoners/${region}/${encodeRiotIdPath({ gameName, tagLine })}/matches`}
            className="rounded-control text-primary hover:underline focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/45"
          >
            View full match history →
          </Link>
        </p>
      </Card>
    </section>
  );
}
