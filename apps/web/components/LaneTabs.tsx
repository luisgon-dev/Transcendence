"use client";

import Link from "next/link";

import { cn } from "@/lib/cn";
import { roleDisplayLabel } from "@/lib/roles";
import { LinkPendingDot } from "@/components/ui/LinkPendingDot";

import { LaneIcon } from "./ui/LaneIcon";

/**
 * The prominent lane selector for the tier list — official LoL position icons
 * over gold-underline tabs, URL-driven (sets `?role=`). Sits directly above the
 * champion table so the chosen lane reads right onto the rows it filters.
 */
export function LaneTabs({
  roles,
  activeRole,
  baseHref,
  extraParams,
  className
}: {
  roles: readonly string[];
  activeRole: string;
  baseHref: string;
  extraParams?: Record<string, string>;
  className?: string;
}) {
  function buildHref(role: string) {
    const params = new URLSearchParams();
    if (role.toUpperCase() !== "ALL") params.set("role", role);
    if (extraParams) {
      for (const [key, value] of Object.entries(extraParams)) {
        if (value && value.toLowerCase() !== "all") params.set(key, value);
      }
    }
    const qs = params.toString();
    return qs ? `${baseHref}?${qs}` : baseHref;
  }

  return (
    <nav className={cn("flex flex-wrap gap-x-1 gap-y-1", className)}>
      {roles.map((role) => {
        const active = role.toUpperCase() === activeRole.toUpperCase();
        return (
          <Link
            key={role}
            href={buildHref(role)}
            data-active={active}
            aria-current={active ? "page" : undefined}
            className={cn(
              "group relative inline-flex min-h-11 items-center gap-2 px-3 py-2 text-fg/64 transition-colors duration-200 ease-[cubic-bezier(0.25,1,0.5,1)] hover:text-fg",
              "after:absolute after:inset-x-2 after:bottom-0 after:h-0.5 after:origin-center after:scale-x-0 after:rounded-full after:bg-primary after:transition-transform after:duration-200 after:[transition-timing-function:var(--ease-out-quart)] motion-reduce:after:transition-none",
              active && "text-primary after:scale-x-100"
            )}
          >
            <LaneIcon
              role={role}
              className="h-[18px] w-[18px] shrink-0 opacity-80 transition-opacity group-hover:opacity-100"
            />
            <span className="type-ui inline-flex items-center gap-1.5 font-medium">
              {roleDisplayLabel(role)}
              <LinkPendingDot />
            </span>
          </Link>
        );
      })}
    </nav>
  );
}
