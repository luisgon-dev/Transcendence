"use client";

import Image from "next/image";
import Link from "next/link";
import { useMemo, useState } from "react";

import { Input } from "@/components/ui/Input";
import { championIconUrl } from "@/lib/staticData";

type ChampionOption = {
  championId: number;
  slug: string;
  name: string;
  title?: string;
};

export function BuildLabChampionPicker({
  champions,
  version
}: {
  champions: ChampionOption[];
  version: string;
}) {
  const [query, setQuery] = useState("");
  const filtered = useMemo(() => {
    const normalized = query.trim().toLowerCase();
    if (!normalized) return champions;
    return champions.filter((champion) =>
      `${champion.name} ${champion.title ?? ""}`.toLowerCase().includes(normalized)
    );
  }, [champions, query]);

  return (
    <div className="grid gap-4">
      <Input
        value={query}
        onChange={(event) => setQuery(event.target.value)}
        placeholder="Search champions"
        aria-label="Search champions for Build Lab"
        className="max-w-xl"
      />
      <div className="grid grid-cols-2 gap-px overflow-hidden rounded-card border border-border/60 bg-border/50 sm:grid-cols-3 lg:grid-cols-5 xl:grid-cols-6">
        {filtered.map((champion) => (
          <Link
            key={champion.championId}
            href={`/lol/builds/${champion.championId}`}
            className="group flex min-w-0 items-center gap-3 bg-surface px-3 py-3 transition hover:bg-surface-2 focus-visible:z-10 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-primary/45"
          >
            <Image
              src={championIconUrl(version, champion.slug)}
              alt=""
              width={40}
              height={40}
              className="size-10 rounded-control border border-border/55"
            />
            <span className="min-w-0">
              <span className="block truncate text-sm font-semibold text-fg">{champion.name}</span>
              <span className="block truncate text-xs text-muted">{champion.title}</span>
            </span>
          </Link>
        ))}
      </div>
      {filtered.length === 0 ? (
        <p className="text-sm text-muted">No champion matches that search.</p>
      ) : null}
    </div>
  );
}
