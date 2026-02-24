"use client";

import Link from "next/link";

import { cn } from "@/lib/cn";
import { roleDisplayLabel } from "@/lib/roles";

export function RoleFilterTabs({
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
    if (role !== "ALL") params.set("role", role);
    if (extraParams) {
      for (const [k, v] of Object.entries(extraParams)) {
        if (v && v.toLowerCase() !== "all") params.set(k, v);
      }
    }
    const qs = params.toString();
    return qs ? `${baseHref}?${qs}` : baseHref;
  }

  return (
    <nav className={cn("flex flex-wrap gap-1.5", className)}>
      {roles.map((role) => {
        const active = role.toUpperCase() === activeRole.toUpperCase();
        return (
          <Link
            key={role}
            href={buildHref(role)}
            className={cn(
              "rounded-md border px-3 py-1.5 text-sm transition",
              active
                ? "border-primary/50 bg-primary/15 text-primary font-medium"
                : "border-border/70 bg-white/5 text-fg/80 hover:bg-white/10"
            )}
          >
            {roleDisplayLabel(role)}
          </Link>
        );
      })}
    </nav>
  );
}
