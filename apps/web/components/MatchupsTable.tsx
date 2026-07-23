"use client";

import { useState } from "react";
import Link from "next/link";

import { ChampionPortrait } from "@/components/ChampionPortrait";
import { DataBar } from "@/components/ui/DataBar";
import { formatGames } from "@/lib/format";

export type MatchupRow = {
  opponentChampionId: number;
  winRate: number | null;
  games: number | null;
  opponentSlug: string;
  opponentName: string;
  verdict: string;
};

type SortKey = "winRate" | "games";

// Client island for the full matchup table: sorting happens in-memory on the rows
// already streamed with the page, so toggling Win Rate / Games is instant with no
// navigation or refetch. Opponent slug/name/verdict are resolved server-side and
// passed in, so this stays a thin presentational sorter.
export function MatchupsTable({
  title,
  subtitle,
  rows,
  version,
  linkQuery
}: {
  title: string;
  subtitle: string;
  rows: MatchupRow[];
  version: string;
  linkQuery: string;
}) {
  const [sortKey, setSortKey] = useState<SortKey>("winRate");

  const sorted = [...rows].sort((a, b) =>
    sortKey === "games"
      ? (b.games ?? 0) - (a.games ?? 0)
      : (a.winRate ?? 0) - (b.winRate ?? 0)
  );

  return (
    <>
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h2 className="type-section">{title}</h2>
          <p className="mt-1 text-xs text-muted">{subtitle}</p>
        </div>
        <div className="flex items-center gap-2 text-xs">
          <button
            type="button"
            onClick={() => setSortKey("winRate")}
            className="control-tab type-ui px-3 py-2"
            data-active={sortKey === "winRate"}
            aria-pressed={sortKey === "winRate"}
            aria-label="Sort by win rate, toughest first"
          >
            Toughest first <span aria-hidden="true">↑</span>
          </button>
          <button
            type="button"
            onClick={() => setSortKey("games")}
            className="control-tab type-ui px-3 py-2"
            data-active={sortKey === "games"}
            aria-pressed={sortKey === "games"}
            aria-label="Sort by games, most played first"
          >
            Most played <span aria-hidden="true">↓</span>
          </button>
        </div>
      </div>
      <div className="mt-4 overflow-x-auto">
        <table className="w-full min-w-[640px] text-left text-sm">
          <thead className="type-overline text-muted">
            <tr className="border-b border-border/30">
              <th className="py-2 pr-4">Opponent</th>
              <th
                className="py-2 pr-4 text-right"
                aria-sort={sortKey === "winRate" ? "ascending" : undefined}
              >
                Win Rate
              </th>
              <th
                className="py-2 pr-4 text-right"
                aria-sort={sortKey === "games" ? "descending" : undefined}
              >
                Games
              </th>
              <th className="py-2 pr-4 text-right">Verdict</th>
            </tr>
          </thead>
          <tbody>
            {sorted.length === 0 ? (
              <tr>
                <td colSpan={4} className="py-4 text-sm text-muted">
                  No matchup data is available for the selected filters yet.
                </td>
              </tr>
            ) : (
              sorted.map((entry) => {
                const verdictClass =
                  entry.verdict === "Favored"
                    ? "text-win"
                    : entry.verdict === "Unfavored"
                      ? "text-loss"
                      : "text-muted";
                return (
                  <tr
                    key={entry.opponentChampionId}
                    className="border-b border-border/40 transition hover:bg-surface-2/40"
                  >
                    <td className="py-2.5 pr-4">
                      <Link
                        href={`/lol/champions/${entry.opponentChampionId}${linkQuery ? `?${linkQuery}` : ""}`}
                        className="hover:underline"
                      >
                        <ChampionPortrait
                          championSlug={entry.opponentSlug}
                          championName={entry.opponentName}
                          version={version}
                          size={24}
                          showName
                        />
                      </Link>
                    </td>
                    <td className="py-2.5 pr-4 text-right">
                      <DataBar
                        value={entry.winRate}
                        decimals={1}
                        games={entry.games ?? undefined}
                        className="justify-end"
                      />
                    </td>
                    <td className="type-tabular py-2.5 pr-4 text-right tabular-nums text-fg/70">
                      {formatGames(entry.games)}
                    </td>
                    <td className={`py-2.5 pr-4 text-right font-medium ${verdictClass}`}>
                      {entry.verdict}
                    </td>
                  </tr>
                );
              })
            )}
          </tbody>
        </table>
      </div>
    </>
  );
}
