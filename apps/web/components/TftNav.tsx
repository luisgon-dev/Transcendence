"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";

import { cn } from "@/lib/cn";

const NAV_ITEMS = [
  { href: "/tft/comps", label: "Comps" },
  { href: "/tft/champions", label: "Units" },
  { href: "/tft/items", label: "Items" },
  { href: "/tft/traits", label: "Traits" },
  { href: "/tft/augments", label: "Augments" }
] as const;

export function TftNav() {
  const pathname = usePathname();

  return (
    <nav className="flex flex-wrap gap-1 rounded-xl border border-border/50 bg-surface/30 p-1">
      {NAV_ITEMS.map((item) => {
        const active = pathname.startsWith(item.href);
        return (
          <Link
            key={item.href}
            href={item.href}
            className={cn(
              "rounded-lg px-3 py-1.5 text-sm font-medium transition",
              active
                ? "bg-primary/15 text-primary"
                : "text-fg/65 hover:bg-white/6 hover:text-fg"
            )}
          >
            {item.label}
          </Link>
        );
      })}
    </nav>
  );
}
