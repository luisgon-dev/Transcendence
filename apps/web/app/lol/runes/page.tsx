import type { Metadata } from "next";

import { BuildResourceIndexPage } from "@/components/BuildResourcePages";

export const metadata: Metadata = {
  title: "League of Legends Rune Stats",
  description: "See rune pick rates, win rates, and which champions take each rune in ranked."
};

export default function RunesPage({ searchParams }: { searchParams?: Promise<{ region?: string; q?: string; sort?: string }> }) {
  return <BuildResourceIndexPage kind="runes" searchParams={searchParams} />;
}
