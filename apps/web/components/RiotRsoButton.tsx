"use client";

import Link from "next/link";
import { useState } from "react";

import { Select } from "@/components/ui/Select";
import { LOL_REGION_OPTIONS } from "@/lib/lolRegions";

export function RiotRsoButton({
  mode,
  returnTo,
  label
}: {
  mode: "login" | "link";
  returnTo: string;
  label: string;
}) {
  const [region, setRegion] = useState("na");
  const href = `/api/session/riot/start?mode=${mode}&region=${encodeURIComponent(region)}&returnTo=${encodeURIComponent(returnTo)}`;

  return (
    <div className="grid gap-2 sm:grid-cols-[120px_minmax(0,1fr)]">
      <Select
        value={region}
        onValueChange={setRegion}
        options={[...LOL_REGION_OPTIONS]}
        ariaLabel="Riot account region"
        className="h-11 w-full"
      />
      <Link
        href={href}
        className="type-ui flex min-h-11 items-center justify-center rounded-control border border-border bg-surface px-4 font-semibold text-fg transition hover:border-primary/45 hover:bg-surface-2"
      >
        {label}
      </Link>
    </div>
  );
}
