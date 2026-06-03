"use client";

import { cn } from "@/lib/cn";

export type TierSpineItem = {
  tier: string;
  count: number;
  hint?: string;
};

const tierVar: Record<string, string> = {
  S: "var(--t-tier-s)",
  A: "var(--t-tier-a)",
  B: "var(--t-tier-b)",
  C: "var(--t-tier-c)",
  D: "var(--t-tier-d)"
};

// The signature element: a continuous tier-colored vertical spine the data
// hangs off of. Each segment is a jump target into the corresponding tier
// section. The left rail reads as one S→D gradient ladder.
export function TierSpine({
  items,
  active,
  onSelect,
  className
}: {
  items: TierSpineItem[];
  active?: string;
  onSelect?: (tier: string) => void;
  className?: string;
}) {
  return (
    <nav className={cn("relative grid gap-1.5 pl-4", className)} aria-label="Jump to tier">
      {/* the spine */}
      <span
        aria-hidden="true"
        className="absolute inset-y-1 left-0 w-1 rounded-full"
        style={{
          background: `linear-gradient(to bottom, ${items
            .map((it) => tierVar[it.tier] ?? "var(--t-tier-d)")
            .join(", ")})`
        }}
      />
      {items.map((item) => {
        const color = tierVar[item.tier] ?? "var(--t-tier-d)";
        const isActive = active === item.tier;
        return (
          <button
            key={item.tier}
            type="button"
            onClick={() => onSelect?.(item.tier)}
            data-active={isActive}
            className={cn(
              "group flex items-center justify-between gap-3 rounded-lg border border-transparent px-3 py-2 text-left transition-colors duration-150",
              "hover:bg-surface-2/60 data-[active=true]:border-border data-[active=true]:bg-surface-2/70"
            )}
          >
            <span className="flex items-baseline gap-2">
              <span
                className="font-display text-base font-bold leading-none"
                style={{ color }}
              >
                {item.tier}
              </span>
              {item.hint ? (
                <span className="type-caption text-muted">{item.hint}</span>
              ) : null}
            </span>
            <span className="type-tabular tabular-nums text-sm text-muted">{item.count}</span>
          </button>
        );
      })}
    </nav>
  );
}
