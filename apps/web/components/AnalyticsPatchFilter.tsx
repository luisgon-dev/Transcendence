"use client";

import { usePathname, useRouter, useSearchParams } from "next/navigation";

import { type LolAnalyticsPatchOption } from "@/lib/lolPatchFilters";

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
  const normalizedActivePatch = activePatch?.trim() ?? "";
  const hasActivePatchOption = patches.some((option) => option.patch === normalizedActivePatch);

  function handleChange(event: React.ChangeEvent<HTMLSelectElement>) {
    const patch = event.target.value;
    const nextParams = new URLSearchParams(searchParams.toString());
    if (patch) {
      nextParams.set("patch", patch);
    } else {
      nextParams.delete("patch");
    }

    const nextUrl = nextParams.toString() ? `${pathname}?${nextParams.toString()}` : pathname;
    router.push(nextUrl);
  }

  return (
    <select
      value={normalizedActivePatch}
      onChange={handleChange}
      className={className ?? "control-select min-w-[148px]"}
      aria-label="Patch"
    >
      <option value="">Current Patch</option>
      {normalizedActivePatch && !hasActivePatchOption ? (
        <option value={normalizedActivePatch}>Patch {normalizedActivePatch}</option>
      ) : null}
      {patches.map((option) => (
        <option key={option.patch} value={option.patch}>
          {option.isActive ? `Current (${option.patch})` : `Patch ${option.patch}`}
        </option>
      ))}
    </select>
  );
}
