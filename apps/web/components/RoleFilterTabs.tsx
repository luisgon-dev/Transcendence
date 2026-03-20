"use client";

import Link from "next/link";
import { motion } from "framer-motion";

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
    <nav className={cn("flex flex-wrap gap-x-4 gap-y-2", className)}>
      {roles.map((role) => {
        const active = role.toUpperCase() === activeRole.toUpperCase();
        return (
          <Link
            key={role}
            href={buildHref(role)}
            className={cn(
              "control-chip type-ui relative overflow-hidden px-3 py-2",
              active && "font-semibold"
            )}
            data-active={active}
          >
            {active && (
              <motion.div
                layoutId="activeRoleTab"
                className="absolute inset-x-0 bottom-0 h-0.5 bg-primary/80"
                initial={false}
                transition={{ type: "spring", stiffness: 400, damping: 30 }}
              />
            )}
            <span className="relative z-10">{roleDisplayLabel(role)}</span>
          </Link>
        );
      })}
    </nav>
  );
}
