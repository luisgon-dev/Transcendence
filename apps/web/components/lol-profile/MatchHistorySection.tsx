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
import { MatchScoreboard } from "@/components/lol-profile/MatchScoreboard";
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
  matchKdaRatio,
  normalizeInitialSort,
  sortRuneSelections,
  type ChampionStatic,
  type ItemStatic,
  type MatchDetail,
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
  championStatic: ChampionStatic | null;
  itemStatic: ItemStatic | null;
  spellStatic: SpellStatic | null;
  runeStatic: RuneStatic | null;
  prefersReducedMotion: boolean;
  onQueueChange(value: string): void;
  onChampionFilterChange(value: string): void;
  onSortChange(value: MatchSortOption): void;
  onToggleExpanded(matchId: string): void | Promise<void>;
  onPreviousPage(): void;
  onNextPage(): void;
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
  championStatic,
  itemStatic,
  spellStatic,
  runeStatic,
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
  championStatic: ChampionStatic | null;
  itemStatic: ItemStatic | null;
  spellStatic: SpellStatic | null;
  runeStatic: RuneStatic | null;
  onToggleExpanded(matchId: string): void | Promise<void>;
}) {
  const queueLabel = formatQueueLabel(match.queueType, match.queueId);
  const champion = championStatic?.champions[String(match.championId)];
  const championName = champion?.name ?? `Champion ${match.championId}`;
  const roleLabel = match.teamPosition ? roleDisplayLabel(match.teamPosition) : "Unknown";
  const primaryRuneId = sortRuneSelections(match.runesDetail?.primarySelections ?? [], runeStatic?.runeSortById)[0] ?? 0;
  const primaryRuneMeta = runeStatic?.runeById[String(primaryRuneId)];
  const subStyleMeta = runeStatic?.styleById[String(match.runesDetail?.subStyleId ?? 0)];
  const spellIds = [match.summonerSpell1Id, match.summonerSpell2Id];
  const itemSlots = Array.from({ length: 7 }, (_, idx) => match.items[idx] ?? 0);
  const matchMetaId = `match-meta-${match.matchId}`;
  const matchPanelId = `match-panel-${match.matchId}`;

  return (
    <div
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
                <p className="text-sm text-fg/75">Detailed rows are unavailable for this match.</p>
              ) : null}
              {detail ? (
                <MatchScoreboard
                  detail={detail}
                  region={region}
                  gameName={gameName}
                  tagLine={tagLine}
                  championStatic={championStatic}
                  itemStatic={itemStatic}
                  spellStatic={spellStatic}
                  runeStatic={runeStatic}
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
  championStatic,
  itemStatic,
  spellStatic,
  runeStatic,
  prefersReducedMotion,
  onQueueChange,
  onChampionFilterChange,
  onSortChange,
  onToggleExpanded,
  onPreviousPage,
  onNextPage
}: MatchHistorySectionProps) {
  return (
    <section className="flex flex-col gap-5">
      <Card className="profile-section-card rounded-panel p-5 md:p-6">
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
        {historyBusy && !history ? <Skeleton className="mt-4 h-16 w-full" /> : null}

        <div className="mt-5 flex flex-col gap-4">
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
              onToggleExpanded={onToggleExpanded}
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
