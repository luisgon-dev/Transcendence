"use client";

import Link from "next/link";

import { cn } from "@/lib/cn";
import { roleDisplayLabel } from "@/lib/roles";
import { LinkPendingDot } from "@/components/ui/LinkPendingDot";

export function RoleFilterTabs({
  roles,
  activeRole,
  baseHref,
  extraParams,
  keepRankAll = false,
  className
}: {
  roles: readonly string[];
  activeRole: string;
  baseHref: string;
  extraParams?: Record<string, string>;
  /** Preserve an explicit `rankTier=all` instead of stripping it (Emerald+-default pages). */
  keepRankAll?: boolean;
  className?: string;
}) {
  function buildHref(role: string) {
    const params = new URLSearchParams();
    if (role !== "ALL") params.set("role", role);
    if (extraParams) {
      for (const [k, v] of Object.entries(extraParams)) {
        if (v && (v.toLowerCase() !== "all" || (keepRankAll && k === "rankTier"))) params.set(k, v);
      }
    }
    const qs = params.toString();
    return qs ? `${baseHref}?${qs}` : baseHref;
  }

  return (
    <nav className={cn("flex flex-wrap gap-x-3 gap-y-3 sm:gap-x-4 sm:gap-y-2", className)}>
      {roles.map((role) => {
        const active = role.toUpperCase() === activeRole.toUpperCase();
        return (
          <Link
            key={role}
            href={buildHref(role)}
            className={cn(
              "control-tab type-ui relative min-h-11 overflow-hidden px-3.5 py-2 after:absolute after:inset-x-0 after:bottom-0 after:h-0.5 after:origin-center after:scale-x-0 after:bg-primary/80 after:transition-transform after:duration-200 after:[transition-timing-function:var(--ease-out-quart)] motion-reduce:after:transition-none",
              active && "after:scale-x-100",
              active && "font-semibold"
            )}
            data-active={active}
            aria-current={active ? "page" : undefined}
          >
            <span className="relative z-10 inline-flex items-center gap-1.5">
              {roleDisplayLabel(role)}
              <LinkPendingDot />
            </span>
          </Link>
        );
      })}
    </nav>
  );
}
