import { Card } from "@/components/ui/Card";
import { DataBar } from "@/components/ui/DataBar";
import { LaneIcon } from "@/components/ui/LaneIcon";
import { Stat } from "@/components/ui/Stat";
import { cn } from "@/lib/cn";
import { formatCompactNumber, formatGames, plural } from "@/lib/format";
import { deriveRecentForm, type RecentFormTone } from "@/lib/matchPerformance";
import { LANE_ROLES, roleDisplayLabel } from "@/lib/roles";

import {
  matchKdaRatio,
  normalizeRoleKey,
  type ChampionStatic,
  type MatchSummary,
  type ProfileFullHistoryStatus,
  type ProfileOverviewStats,
  type ProfileSeasonMetadata
} from "@/components/lol-profile/shared";

type RoleRow = {
  role: string;
  games: number;
  winRate: number;
  avgKda: number;
  topChampion: string | null;
};

const FORM_TONE_CLASS: Record<RecentFormTone, string> = {
  up: "border-success/30 bg-success/8 text-success",
  steady: "border-border bg-surface-2/60 text-fg/78",
  down: "border-loss/30 bg-loss/8 text-loss"
};

const FORM_TEXT_CLASS: Record<RecentFormTone, string> = {
  up: "text-success",
  steady: "text-muted",
  down: "text-loss"
};

function FormDirectionIcon({ tone }: { tone: RecentFormTone }) {
  if (tone === "steady") {
    return (
      <svg viewBox="0 0 16 16" className="size-4" aria-hidden="true">
        <path d="M3 8h10" fill="none" stroke="currentColor" strokeWidth="1.75" strokeLinecap="round" />
      </svg>
    );
  }

  return (
    <svg
      viewBox="0 0 16 16"
      className={cn("size-4", tone === "down" && "rotate-90")}
      aria-hidden="true"
    >
      <path
        d="M3 11 11 3m-5 0h5v5"
        fill="none"
        stroke="currentColor"
        strokeWidth="1.75"
        strokeLinecap="round"
        strokeLinejoin="round"
      />
    </svg>
  );
}

function formatStat(value: number | null | undefined, decimals: number): string {
  if (value == null || !Number.isFinite(value)) return "—";
  return value.toFixed(decimals);
}

function formatHistoryCoverage(history: ProfileFullHistoryStatus | null | undefined): string | null {
  if (!history) return null;
  const riotTotal = history.riotTotal;
  if (typeof riotTotal === "number") {
    const delta = history.rankedCountDelta ?? history.completedMatchCount - riotTotal;
    if (delta === 0) return `Riot ranked total matched · ${formatGames(riotTotal)} games`;
    const sign = delta > 0 ? "+" : "";
    return `${formatGames(history.completedMatchCount)} stored / ${formatGames(riotTotal)} Riot · ${sign}${delta}`;
  }

  const normalized = history.status.replaceAll("_", " ").toLowerCase();
  return `${normalized} · ${formatGames(history.completedMatchCount)} ranked games stored`;
}

// An approachable "your performance" lens for the profile: a per-role split of
// the loaded recent matches plus the player's own season averages. Complements
// (does not duplicate) the sidebar "most played champions" — role is the angle
// here, not champion. Purely presentational; safe to render before history loads
// (averages still show from overviewStats).
export function PerformanceCard({
  matches,
  overviewStats,
  championStatic,
  activeSeason,
  fullHistory
}: {
  matches: MatchSummary[];
  overviewStats?: ProfileOverviewStats | null;
  championStatic?: ChampionStatic | null;
  activeSeason?: ProfileSeasonMetadata | null;
  fullHistory?: ProfileFullHistoryStatus | null;
}) {
  // Resolve each role's signature pick to a champion name via static data; championId is the
  // source of truth (the profile's aggregate champion stats no longer carry a placeholder name).
  const resolveChampionName = (championId: number): string | null =>
    championStatic?.champions[String(championId)]?.name ?? null;

  const buckets = new Map<
    string,
    { games: number; wins: number; kdaSum: number; champCounts: Map<number, number> }
  >();
  for (const match of matches) {
    const role = normalizeRoleKey(match.teamPosition);
    const bucket = buckets.get(role) ?? { games: 0, wins: 0, kdaSum: 0, champCounts: new Map() };
    bucket.games += 1;
    if (match.win) bucket.wins += 1;
    bucket.kdaSum += matchKdaRatio(match);
    bucket.champCounts.set(match.championId, (bucket.champCounts.get(match.championId) ?? 0) + 1);
    buckets.set(role, bucket);
  }

  // Keep only the five real lanes. normalizeRoleKey maps every valid position into
  // LANE_ROLES (SUPPORT→UTILITY), so this drops both "UNKNOWN" and the non-standard
  // teamPosition tokens that special modes (e.g. Arena) carry — those render as a
  // meaningless "Unknown" row and teach nothing about how the player lanes.
  const roleRows: RoleRow[] = [...buckets.entries()]
    .filter(([role]) => (LANE_ROLES as readonly string[]).includes(role))
    .map(([role, bucket]) => {
      let topChampionId = -1;
      let topCount = -1;
      for (const [championId, count] of bucket.champCounts) {
        if (count > topCount) {
          topCount = count;
          topChampionId = championId;
        }
      }
      return {
        role,
        games: bucket.games,
        winRate: bucket.wins / bucket.games,
        avgKda: bucket.kdaSum / bucket.games,
        topChampion: resolveChampionName(topChampionId)
      };
    })
    .sort((a, b) => b.games - a.games);

  const hasRoles = roleRows.length > 0;
  const hasAverages = overviewStats != null;
  const recentForm = deriveRecentForm(matches);
  const coverageLabel = formatHistoryCoverage(fullHistory);
  if (!hasRoles && !hasAverages && !recentForm) return null;

  return (
    <Card className="profile-section-card p-5">
      <div>
        <p className="type-kicker text-muted">{activeSeason?.displayName ?? "Active season"} · Solo/Duo</p>
        <h2 className="mt-2 type-section">How you play</h2>
      </div>

      {recentForm ? (
        <div className="mt-4 flex flex-wrap items-center justify-between gap-3 rounded-control border border-border/75 bg-surface-2/45 px-3 py-2.5">
          <div className="flex items-center gap-2.5">
            <span
              className={cn(
                "grid size-8 shrink-0 place-items-center rounded-control border",
                FORM_TONE_CLASS[recentForm.tone]
              )}
            >
              <FormDirectionIcon tone={recentForm.tone} />
            </span>
            <div>
              <p className="type-overline text-muted">Recent form</p>
              <p className="text-sm font-semibold text-fg">{recentForm.label}</p>
            </div>
          </div>
          <div className="text-right tabular-nums">
            <p className="text-sm font-semibold text-fg">{recentForm.recentAverage.toFixed(1)} impact</p>
            <p className={cn("type-caption", FORM_TEXT_CLASS[recentForm.tone])}>
              {recentForm.delta >= 0 ? "+" : ""}
              {recentForm.delta.toFixed(1)} vs previous {recentForm.previousGames}
            </p>
          </div>
        </div>
      ) : null}

      {hasAverages ? (
        <div className="mt-4 grid gap-2">
          <div className="flex items-center justify-between gap-2">
            <p className="type-overline text-muted">Averages</p>
            <p className="type-caption text-muted">Across {formatGames(overviewStats.totalMatches)} {plural(overviewStats.totalMatches, "game")}</p>
          </div>
          {coverageLabel ? (
            <p className="type-caption text-muted">{coverageLabel}</p>
          ) : null}
          <div className="grid grid-cols-2 gap-4 sm:grid-cols-4">
            <Stat label="CS / min" value={formatStat(overviewStats.avgCsPerMin, 1)} />
            <Stat label="Vision" value={formatStat(overviewStats.avgVisionScore, 1)} />
            <Stat label="KDA" value={formatStat(overviewStats.kdaRatio, 2)} />
            <Stat label="Dmg / game" value={formatCompactNumber(overviewStats.avgDamageToChamps)} />
          </div>
        </div>
      ) : null}

      {hasRoles ? (
        // Collapsed by default so the match history sits higher on the page — the
        // season averages above answer "how do you play" at a glance, and the
        // per-role split is one click of depth away. Native <details> keeps it
        // keyboard-accessible with no client state.
        <details className={cn("group", hasAverages && "mt-5 border-t border-border/70 pt-5")}>
          <summary className="flex cursor-pointer list-none items-center justify-between gap-2 rounded-control py-2 [&::-webkit-details-marker]:hidden focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/45">
            <span className="flex items-center gap-2">
              <svg
                viewBox="0 0 12 12"
                aria-hidden="true"
                className="size-3 shrink-0 text-muted transition-transform duration-150 group-open:rotate-90"
              >
                <path d="M4.5 3 7.5 6 4.5 9" stroke="currentColor" strokeWidth="1.4" fill="none" strokeLinecap="round" strokeLinejoin="round" />
              </svg>
              <span className="type-overline text-muted">By role</span>
            </span>
            <span className="type-caption text-muted">From last {formatGames(matches.length)} {plural(matches.length, "game")}</span>
          </summary>
          <div className="mt-3 grid gap-2">
            {roleRows.map((row) => (
              <div key={row.role} className="surface-subtle grid gap-2 rounded-control px-3 py-2.5">
                <div className="flex items-center justify-between gap-3">
                  <div className="flex min-w-0 items-center gap-2">
                    <LaneIcon role={row.role} className="h-4 w-4 shrink-0 text-muted" />
                    <span className="truncate text-sm font-medium text-fg">{roleDisplayLabel(row.role)}</span>
                  </div>
                  {/* A single-game (usually off-role) row lands at 0%/100%, where the
                      Wald CI whisker collapses and the bar would read as a confident
                      verdict. Below 2 games we render a muted "—" instead — matching
                      the played-with guard and the "never present small-n as fact" rule. */}
                  <DataBar value={row.games >= 2 ? row.winRate : null} games={row.games} />
                </div>
                <div className="flex items-center justify-between gap-2 text-xs text-muted">
                  <span className="min-w-0 truncate">
                    {formatGames(row.games)} {plural(row.games, "game")}{row.topChampion ? ` · ${row.topChampion}` : ""}
                  </span>
                  <span className="shrink-0 tabular-nums">{row.avgKda.toFixed(2)} KDA</span>
                </div>
              </div>
            ))}
          </div>
        </details>
      ) : null}
    </Card>
  );
}
