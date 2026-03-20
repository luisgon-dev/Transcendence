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

  if (isHeader) {
    return (
      <Button
        type="button"
        variant="ghost"
        size={size}
        className={cn(
          "h-10 rounded-full border border-border/55 bg-transparent px-3 text-fg/72 hover:border-border/75 hover:bg-white/[0.05] hover:text-fg",
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
        "group relative justify-between overflow-hidden border-border/70 bg-surface/65 text-fg/85",
        "before:absolute before:inset-0 before:bg-gradient-to-r before:from-primary/6 before:to-transparent before:opacity-0 before:transition before:duration-300 hover:before:opacity-100",
        className
      )}
      onClick={() => window.dispatchEvent(new Event(GLOBAL_SEARCH_OPEN_EVENT))}
    >
      <span className="relative z-10 inline-flex items-center gap-2">
        <span className="font-heading text-sm font-semibold text-primary/85" aria-hidden="true">
          /
        </span>
        <span className="type-ui">Search champions, players, or pages</span>
      </span>
      <span className="type-kicker relative z-10 hidden rounded-md border border-border/70 bg-surface/80 px-2 py-1 text-muted sm:inline">
        Ctrl/Cmd+K
      </span>
    </Button>
  );
}

function SearchIcon({ className }: { className?: string }) {
  return (
    <svg
      viewBox="0 0 16 16"
      aria-hidden="true"
      className={className}
      fill="none"
      stroke="currentColor"
      strokeWidth="1.6"
      strokeLinecap="round"
      strokeLinejoin="round"
    >
      <circle cx="7" cy="7" r="4.5" />
      <path d="m10.5 10.5 3 3" />
    </svg>
  );
}
