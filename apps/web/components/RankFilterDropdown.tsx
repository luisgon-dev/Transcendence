"use client";

import { useRouter } from "next/navigation";

import { cn } from "@/lib/cn";

export function RankFilterDropdown({
  ranks,
  activeRank,
  baseHref,
  extraParams,
  className
}: {
  ranks: readonly string[];
  activeRank: string;
  baseHref: string;
  extraParams?: Record<string, string>;
  className?: string;
}) {
  const router = useRouter();

  function handleChange(e: React.ChangeEvent<HTMLSelectElement>) {
    const rank = e.target.value;
    const params = new URLSearchParams();
    if (extraParams) {
      for (const [k, v] of Object.entries(extraParams)) {
        if (v && v.toLowerCase() !== "all") params.set(k, v);
      }
    }
    if (rank && rank.toLowerCase() !== "all") params.set("rankTier", rank);
    const qs = params.toString();
    router.push(qs ? `${baseHref}?${qs}` : baseHref);
  }

  return (
    <select
      value={activeRank || "all"}
      onChange={handleChange}
      className={cn(
        "h-9 min-w-[140px] rounded-md border border-border/70 bg-surface/35 px-3 text-sm text-fg shadow-glass outline-none focus:border-primary/70 focus:ring-2 focus:ring-primary/25",
        className
      )}
    >
      {ranks.map((rank) => (
        <option key={rank} value={rank}>
          {rank === "all" ? "All Ranks" : rank}
        </option>
      ))}
    </select>
  );
}
