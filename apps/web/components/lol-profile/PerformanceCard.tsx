import { Card } from "@/components/ui/Card";
import { DataBar } from "@/components/ui/DataBar";
import { LaneIcon } from "@/components/ui/LaneIcon";
import { Stat } from "@/components/ui/Stat";
import { cn } from "@/lib/cn";
import { formatGames } from "@/lib/format";
import { LANE_ROLES, roleDisplayLabel } from "@/lib/roles";

import {
  matchKdaRatio,
  normalizeRoleKey,
  type MatchSummary,
  type ProfileChampionStat,
  type ProfileOverviewStats
} from "@/components/lol-profile/shared";

type RoleRow = {
  role: string;
  games: number;
  winRate: number;
  avgKda: number;
  topChampion: string | null;
};

function formatStat(value: number | null | undefined, decimals: number): string {
  if (value == null || !Number.isFinite(value)) return "—";
  return value.toFixed(decimals);
}

// Damage-to-champions averages run ~5k–40k; compact to keep the stat row tabular
// and scannable rather than a wall of digits.
function formatCompactNumber(value: number | null | undefined): string {
  if (value == null || !Number.isFinite(value)) return "—";
  if (value >= 1000) return `${(value / 1000).toFixed(1)}k`;
  return Math.round(value).toLocaleString();
}

// An approachable "your performance" lens for the profile: a per-role split of
// the loaded recent matches plus the player's own season averages. Complements
// (does not duplicate) the sidebar "most played champions" — role is the angle
// here, not champion. Purely presentational; safe to render before history loads
// (averages still show from overviewStats).
export function PerformanceCard({
  matches,
  overviewStats,
  topChampions
}: {
  matches: MatchSummary[];
  overviewStats?: ProfileOverviewStats | null;
  topChampions?: ProfileChampionStat[] | null;
}) {
  // championId → name, sourced from the profile's aggregate champion stats so we
  // can label each role's signature pick without depending on static asset data.
  const championNameById = new Map<number, string>();
  for (const champion of topChampions ?? []) {
    championNameById.set(champion.championId, champion.championName);
  }

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
        topChampion: championNameById.get(topChampionId) ?? null
      };
    })
    .sort((a, b) => b.games - a.games);

  const hasRoles = roleRows.length > 0;
  const hasAverages = overviewStats != null;
  if (!hasRoles && !hasAverages) return null;

  return (
    <Card className="profile-section-card p-5">
      <div>
        <p className="type-kicker text-muted">Performance</p>
        <h2 className="mt-2 type-section">How you play</h2>
      </div>

      {hasAverages ? (
        <div className="mt-4 grid gap-2">
          <div className="flex items-center justify-between gap-2">
            <p className="type-overline text-muted">Averages</p>
            <p className="type-caption text-muted">Across {formatGames(overviewStats.totalMatches)} games</p>
          </div>
          <div className="grid grid-cols-2 gap-4 sm:grid-cols-4">
            <Stat label="CS / min" value={formatStat(overviewStats.avgCsPerMin, 1)} />
            <Stat label="Vision" value={formatStat(overviewStats.avgVisionScore, 1)} />
            <Stat label="KDA" value={formatStat(overviewStats.kdaRatio, 2)} />
            <Stat label="Dmg / game" value={formatCompactNumber(overviewStats.avgDamageToChamps)} />
          </div>
        </div>
      ) : null}

      {hasRoles ? (
        <div className={cn("grid gap-2", hasAverages && "mt-5 border-t border-border pt-5")}>
          <div className="flex items-center justify-between gap-2">
            <p className="type-overline text-muted">By role</p>
            <p className="type-caption text-muted">From last {formatGames(matches.length)} games</p>
          </div>
          {roleRows.map((row) => (
            <div key={row.role} className="surface-subtle grid gap-2 rounded-control px-3 py-2.5">
              <div className="flex items-center justify-between gap-3">
                <div className="flex min-w-0 items-center gap-2">
                  <LaneIcon role={row.role} className="h-4 w-4 shrink-0 text-muted" />
                  <span className="truncate text-sm font-medium text-fg">{roleDisplayLabel(row.role)}</span>
                </div>
                <DataBar value={row.winRate} games={row.games} />
              </div>
              <div className="flex items-center justify-between gap-2 text-xs text-fg/64">
                <span className="min-w-0 truncate">
                  {formatGames(row.games)} games{row.topChampion ? ` · ${row.topChampion}` : ""}
                </span>
                <span className="shrink-0 tabular-nums">{row.avgKda.toFixed(2)} KDA</span>
              </div>
            </div>
          ))}
        </div>
      ) : null}
    </Card>
  );
}
