"use client";

import { Button } from "@/components/ui/Button";
import { cn } from "@/lib/cn";
import { GLOBAL_SEARCH_OPEN_EVENT } from "@/lib/globalSearch";

export function GlobalSearchLauncher({
  className,
  size = "md",
  variant = "hero"
}: {
  className?: string;
  size?: "sm" | "md";
  variant?: "hero" | "header";
}) {
  const isHeader = variant === "header";

  return (
    <Button
      type="button"
      variant="outline"
      size={size}
      className={cn(
        "group relative justify-between overflow-hidden border-border/70 text-fg/85",
        "before:absolute before:inset-0 before:bg-gradient-to-r before:from-white/[0.05] before:to-transparent before:opacity-0 before:transition before:duration-300 hover:before:opacity-100",
        className
      )}
      onClick={() => window.dispatchEvent(new Event(GLOBAL_SEARCH_OPEN_EVENT))}
    >
      <span className="relative z-10 inline-flex items-center gap-2">
        <span className="text-sm" aria-hidden="true">
          /
        </span>
        <span>{isHeader ? "Search" : "Search champions, summoners, or tier list"}</span>
      </span>
      <span className="relative z-10 rounded-md border border-border/70 bg-black/20 px-2 py-0.5 text-xs text-muted">
        Ctrl/Cmd+K
      </span>
    </Button>
  );
}
