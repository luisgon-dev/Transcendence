"use client";

import { Button } from "@/components/ui/Button";
import { SearchIcon } from "@/components/ui/icons";
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

  if (isHeader) {
    return (
      <Button
        type="button"
        variant="ghost"
        size={size}
        aria-label="Search"
        className={cn(
          "h-11 min-w-11 rounded-full border border-border/55 bg-transparent px-3 text-fg/72 hover:border-border/75 hover:bg-white/[0.05] hover:text-fg",
          className
        )}
        onClick={() => window.dispatchEvent(new Event(GLOBAL_SEARCH_OPEN_EVENT))}
      >
        <SearchIcon className="h-4 w-4 shrink-0" />
        <span className="type-ui hidden lg:inline">Search</span>
      </Button>
    );
  }

  return (
    <Button
      type="button"
      variant="outline"
      size={size}
      className={cn(
        "justify-between border-border/70 bg-surface/65 text-fg/85",
        className
      )}
      onClick={() => window.dispatchEvent(new Event(GLOBAL_SEARCH_OPEN_EVENT))}
    >
      <span className="inline-flex items-center gap-2">
        <span className="font-heading text-sm font-semibold text-muted" aria-hidden="true">
          /
        </span>
        <span className="type-ui">Search champions, players, or pages</span>
      </span>
      <span className="type-kicker hidden rounded-md border border-border/70 bg-surface/80 px-2 py-1 text-muted sm:inline">
        Ctrl/Cmd+K
      </span>
    </Button>
  );
}
