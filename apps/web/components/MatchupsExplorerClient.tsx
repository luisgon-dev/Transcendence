"use client";

import Image from "next/image";
import Link from "next/link";
import { motion } from "framer-motion";
import { useMemo, useState } from "react";

import { roleDisplayLabel } from "@/lib/roles";
import { championIconUrl } from "@/lib/staticData";

type ChampionMap = Record<string, { id: string; name: string; title?: string }>;

type MatchupEntry = {
  championId: number;
  role: string;
  games: number;
  winRate: number;
};

export function MatchupsExplorerClient({
  entries,
  champions,
  version
}: {
  entries: MatchupEntry[];
  champions: ChampionMap;
  version: string;
}) {
  const [query, setQuery] = useState("");
  const [role, setRole] = useState("ALL");

  const roles = useMemo(
    () => ["ALL", ...Array.from(new Set(entries.map((e) => e.role))).sort()],
    [entries]
  );

  const filtered = useMemo(() => {
    const q = query.trim().toLowerCase();
    return entries
      .filter((entry) => role === "ALL" || entry.role === role)
      .filter((entry) => {
        const champ = champions[String(entry.championId)];
        const name = (champ?.name ?? `Champion ${entry.championId}`).toLowerCase();
        return q.length === 0 || name.includes(q);
      })
      .sort((a, b) => b.games - a.games)
      .slice(0, 48);
  }, [champions, entries, query, role]);

  return (
    <div className="grid gap-4">
      <div className="glass-panel rounded-2xl p-4">
        <div className="flex flex-wrap items-center gap-3">
          <input
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            placeholder="Search champion..."
            className="h-11 w-full rounded-xl border border-border/70 bg-surface/65 px-3 text-sm text-fg outline-none placeholder:text-muted sm:max-w-sm"
          />
          <div className="flex flex-wrap gap-2">
            {roles.map((roleOption) => (
              <button
                key={roleOption}
                type="button"
                onClick={() => setRole(roleOption)}
                className={`rounded-full border px-3 py-1 text-xs font-medium transition ${
                  role === roleOption
                    ? "border-primary/45 bg-primary/15 text-primary"
                    : "border-border/70 bg-surface/70 text-fg/80 hover:bg-surface/90"
                }`}
              >
                {roleOption === "ALL" ? "All Roles" : roleDisplayLabel(roleOption)}
              </button>
            ))}
          </div>
        </div>
      </div>

      <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4">
        {filtered.map((entry) => {
          const champion = champions[String(entry.championId)];
          const championName = champion?.name ?? `Champion ${entry.championId}`;
          return (
            <motion.div key={`${entry.championId}-${entry.role}`} whileHover={{ y: -2 }}>
              <Link
                href={`/matchups/${entry.championId}?role=${encodeURIComponent(entry.role)}`}
                className="block rounded-2xl border border-border/60 bg-surface/65 p-3 transition hover:border-border-strong hover:bg-surface/80"
              >
                <div className="flex items-center gap-3">
                  <Image
                    src={championIconUrl(version, champion?.id ?? "Unknown")}
                    alt={championName}
                    width={38}
                    height={38}
                    className="rounded-lg border border-border/50"
                  />
                  <div className="min-w-0">
                    <p className="truncate text-sm font-semibold text-fg">{championName}</p>
                    <p className="truncate text-xs text-muted">
                      {roleDisplayLabel(entry.role)} · {entry.games.toLocaleString()} games
                    </p>
                  </div>
                </div>
              </Link>
            </motion.div>
          );
        })}
      </div>
    </div>
  );
}
