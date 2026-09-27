import { cn } from "@/lib/cn";

/** A Q/W/E/R key cap. `emphasis` marks the ability maxed first. */
export function AbilityPill({ letter, emphasis = false }: { letter: string; emphasis?: boolean }) {
  return (
    <span
      className={cn(
        "inline-flex h-6 w-6 items-center justify-center rounded-control border text-xs font-semibold",
        emphasis
          ? "border-border-strong bg-surface-2 text-fg"
          : "border-border/60 bg-surface-2/50 text-fg/80"
      )}
    >
      {letter}
    </span>
  );
}
