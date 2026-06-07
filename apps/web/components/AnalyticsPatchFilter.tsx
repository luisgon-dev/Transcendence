"use client";

import { useTransition } from "react";
import { usePathname, useRouter, useSearchParams } from "next/navigation";

import { Spinner } from "@/components/ui/Spinner";
import { cn } from "@/lib/cn";
import {
  getVisibleAnalyticsPatches,
  type LolAnalyticsPatchOption
} from "@/lib/lolPatchFilters";

export function AnalyticsPatchFilter({
  patches,
  activePatch,
  className
}: {
  patches: readonly LolAnalyticsPatchOption[];
  activePatch?: string | null;
  className?: string;
}) {
  const router = useRouter();
  const pathname = usePathname();
  const searchParams = useSearchParams();
  const [isPending, startTransition] = useTransition();
  const visiblePatches = getVisibleAnalyticsPatches(patches);
  const normalizedActivePatch = activePatch?.trim() ?? "";
  const hasActivePatchOption = visiblePatches.some((option) => option.patch === normalizedActivePatch);

  function handleChange(event: React.ChangeEvent<HTMLSelectElement>) {
    const patch = event.target.value;
    const nextParams = new URLSearchParams(searchParams.toString());
    if (patch) {
      nextParams.set("patch", patch);
    } else {
      nextParams.delete("patch");
    }

    const nextUrl = nextParams.toString() ? `${pathname}?${nextParams.toString()}` : pathname;
    startTransition(() => {
      router.push(nextUrl);
    });
  }

  return (
    <span className="relative inline-flex items-center" aria-busy={isPending}>
      <select
        value={normalizedActivePatch}
        onChange={handleChange}
        className={cn(className ?? "control-select min-w-[148px]", isPending && "opacity-60")}
        aria-label="Patch"
      >
        <option value="">Current Patch</option>
        {normalizedActivePatch && !hasActivePatchOption ? (
          <option value={normalizedActivePatch}>Patch {normalizedActivePatch}</option>
        ) : null}
        {visiblePatches.map((option) => (
          <option key={option.patch} value={option.patch}>
            {option.isActive ? `Current (${option.patch})` : `Patch ${option.patch}`}
          </option>
        ))}
      </select>
      {isPending ? (
        <Spinner className="pointer-events-none absolute right-7 top-1/2 -translate-y-1/2" />
      ) : null}
    </span>
  );
}
