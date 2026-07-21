import type { Metadata } from "next";

import { BuildResourceIndexPage } from "@/components/BuildResourcePages";

export const metadata: Metadata = {
  title: "League Rune Analytics",
  description: "Ranked rune pick rates, win rates, and the champion-role pairs that select each rune most."
};

export default function RunesPage({ searchParams }: { searchParams?: Promise<{ region?: string; q?: string; sort?: string }> }) {
  return <BuildResourceIndexPage kind="runes" searchParams={searchParams} />;
}
