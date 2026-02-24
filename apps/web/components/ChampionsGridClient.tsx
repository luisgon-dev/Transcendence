"use client";

import Image from "next/image";
import Link from "next/link";
import { useMemo, useState } from "react";

import { TierBadge } from "@/components/TierBadge";
import { WinRateText } from "@/components/WinRateText";
import { Card } from "@/components/ui/Card";
import { championIconUrl } from "@/lib/staticData";
import { roleDisplayLabel } from "@/lib/roles";
import type { UITierGrade } from "@/lib/tierlist";

export type ChampionGridEntry = {
  championId: number;
  id: string;
  name: string;
  tier: UITierGrade | null;
  winRate: number | null;
  primaryRole: string | null;
};

export function ChampionsGridClient({
  champions,
  version
}: {
  champions: ChampionGridEntry[];
  version: string;
}) {
  const [query, setQuery] = useState("");

  const filtered = useMemo(() => {
    if (!query.trim()) return champions;
    const q = query.trim().toLowerCase();
    return champions.filter((c) => c.name.toLowerCase().includes(q));
  }, [champions, query]);

  return (
    <div className="grid gap-4">
      <input
        type="text"
        value={query}
        onChange={(e) => setQuery(e.target.value)}
        placeholder="Search champions..."
        className="h-10 w-full max-w-sm rounded-md border border-border/70 bg-surface/35 px-4 text-sm text-fg placeholder-muted outline-none focus:border-primary/70 focus:ring-2 focus:ring-primary/25"
      />

      <div className="grid grid-cols-2 gap-3 sm:grid-cols-3 md:grid-cols-4 lg:grid-cols-6">
        {filtered.map((c) => (
          <Link key={c.championId} href={`/champions/${c.championId}`}>
            <Card className="group p-3 transition hover:bg-white/10">
              <div className="flex items-center gap-3">
                <Image
                  src={championIconUrl(version, c.id)}
                  alt={c.name}
                  width={40}
                  height={40}
                  className="rounded-lg"
                />
                <div className="min-w-0 flex-1">
                  <p className="truncate text-sm font-semibold text-fg group-hover:underline">
                    {c.name}
                  </p>
                  <div className="mt-0.5 flex items-center gap-1.5">
                    {c.tier ? <TierBadge tier={c.tier} /> : null}
                    {c.primaryRole ? (
                      <span className="text-[11px] text-muted">
                        {roleDisplayLabel(c.primaryRole)}
                      </span>
                    ) : null}
                  </div>
                  {c.winRate != null ? (
                    <div className="mt-0.5">
                      <WinRateText
                        value={c.winRate}
                        decimals={1}
                        className="text-xs"
                      />
                    </div>
                  ) : null}
                </div>
              </div>
            </Card>
          </Link>
        ))}
      </div>

      {filtered.length === 0 ? (
        <p className="text-center text-sm text-muted">
          No champions match &quot;{query}&quot;
        </p>
      ) : null}
    </div>
  );
}
