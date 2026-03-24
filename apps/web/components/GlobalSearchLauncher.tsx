"use client";

import { Button } from "@/components/ui/Button";
import { SearchIcon } from "@/components/ui/icons";
import { cn } from "@/lib/cn";
import { dispatchGlobalSearchOpen } from "@/lib/globalSearch";

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
        aria-label="Open search"
        className={cn(
          "h-11 min-w-11 rounded-full border border-border/55 bg-surface/26 px-3 text-fg/72 shadow-inset hover:border-border/78 hover:bg-surface/54 hover:text-fg",
          className
        )}
        onClick={(event) => dispatchGlobalSearchOpen(event.currentTarget)}
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
        "justify-between gap-4 border-border/70 bg-surface/76 text-fg/88 shadow-soft hover:border-border-strong/78 hover:bg-surface/92",
        className
      )}
      onClick={(event) => dispatchGlobalSearchOpen(event.currentTarget)}
    >
      <span className="inline-flex items-center gap-2">
        <span className="font-heading text-sm font-semibold text-muted" aria-hidden="true">
          /
        </span>
        <span className="type-ui">Search players, champions, or pages</span>
      </span>
      <span className="type-kicker hidden rounded-md border border-border/70 bg-surface/88 px-2 py-1 text-muted sm:inline">
        Ctrl/Cmd+K
      </span>
    </Button>
  );
}
