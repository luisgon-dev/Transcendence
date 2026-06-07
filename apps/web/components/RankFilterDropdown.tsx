"use client";

import { useTransition } from "react";
import { useRouter } from "next/navigation";

import { Select } from "@/components/ui/Select";
import { Spinner } from "@/components/ui/Spinner";
import { cn } from "@/lib/cn";
import { rankTierDisplayLabel } from "@/lib/ranks";

export function RankFilterDropdown({
  ranks,
  activeRank,
  baseHref,
  extraParams,
  emitAllRank = false,
  className
}: {
  ranks: readonly string[];
  activeRank: string;
  baseHref: string;
  extraParams?: Record<string, string>;
  /** Emit an explicit `rankTier=all` for the "all" option instead of omitting it (Emerald+-default pages). */
  emitAllRank?: boolean;
  className?: string;
}) {
  const router = useRouter();
  const [isPending, startTransition] = useTransition();

  function handleChange(rank: string) {
    const params = new URLSearchParams();
    if (extraParams) {
      for (const [k, v] of Object.entries(extraParams)) {
        if (v && v.toLowerCase() !== "all") params.set(k, v);
      }
    }
    const isAll = !rank || rank.toLowerCase() === "all";
    if (!isAll) {
      params.set("rankTier", rank);
    } else if (emitAllRank) {
      params.set("rankTier", "all");
    }
    const qs = params.toString();
    startTransition(() => {
      router.push(qs ? `${baseHref}?${qs}` : baseHref);
    });
  }

  return (
    <span className="relative inline-flex" aria-busy={isPending}>
      <Select
        value={activeRank || "all"}
        onValueChange={handleChange}
        ariaLabel="Rank"
        options={ranks.map((rank) => ({ value: rank, label: rankTierDisplayLabel(rank) }))}
        className={cn(isPending && "opacity-60", className)}
      />
      {isPending ? (
        <Spinner className="pointer-events-none absolute right-8 top-1/2 -translate-y-1/2" />
      ) : null}
    </span>
  );
}
