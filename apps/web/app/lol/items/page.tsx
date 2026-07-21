import type { Metadata } from "next";

import { BuildResourceIndexPage } from "@/components/BuildResourcePages";

export const metadata: Metadata = {
  title: "League Item Analytics",
  description: "Ranked item pick rates, win rates, and the champion-role pairs that use each item most."
};

export default function ItemsPage({ searchParams }: { searchParams?: Promise<{ region?: string; q?: string; sort?: string }> }) {
  return <BuildResourceIndexPage kind="items" searchParams={searchParams} />;
}
