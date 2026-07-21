"use client";

import { cn } from "@/lib/cn";

type Theme = "light" | "dark";

function applyTheme(theme: Theme) {
  const root = document.documentElement;
  root.classList.toggle("dark", theme === "dark");
  root.style.colorScheme = theme;
}

// The head script resolves the theme before paint. Both icons render in the SSR
// shell and CSS selects the right one from the root .dark class, so hydration
// never flashes an empty glyph or exposes a stale theme-specific label.
export function ThemeToggle({ className }: { className?: string }) {
  function toggle() {
    const next: Theme = document.documentElement.classList.contains("dark") ? "light" : "dark";
    applyTheme(next);
    try {
      localStorage.setItem("theme", next);
    } catch {
      /* private mode — ignore */
    }
  }

  return (
    <button
      type="button"
      onClick={toggle}
      aria-label="Toggle color theme"
      title="Toggle color theme"
      className={cn(
        "inline-flex size-11 items-center justify-center rounded-lg border border-border/70 bg-surface/60 text-fg/80 transition-colors duration-150 ease-[cubic-bezier(0.25,1,0.5,1)] hover:border-border-strong hover:text-fg focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40 focus-visible:ring-offset-2 focus-visible:ring-offset-bg lg:size-9",
        className
      )}
    >
      <svg viewBox="0 0 24 24" className="hidden size-[18px] dark:block" fill="none" aria-hidden="true">
        <g stroke="currentColor" strokeWidth="1.6" strokeLinecap="round">
          <circle cx="12" cy="12" r="4" />
          <path d="M12 2.5v2M12 19.5v2M2.5 12h2M19.5 12h2M5 5l1.5 1.5M17.5 17.5L19 19M19 5l-1.5 1.5M6.5 17.5L5 19" />
        </g>
      </svg>
      <svg viewBox="0 0 24 24" className="size-[18px] dark:hidden" fill="none" aria-hidden="true">
        <path
          d="M20 14.5A8 8 0 0 1 9.5 4a6.5 6.5 0 1 0 10.5 10.5Z"
          stroke="currentColor"
          strokeWidth="1.6"
          strokeLinejoin="round"
        />
      </svg>
    </button>
  );
}
