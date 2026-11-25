import type { Metadata } from "next";

import { BuildResourceIndexPage } from "@/components/BuildResourcePages";

export const metadata: Metadata = {
  title: "League of Legends Item Stats",
  description: "See item pick rates, win rates, and which champions build each item in ranked."
};

export default function ItemsPage({ searchParams }: { searchParams?: Promise<{ region?: string; q?: string; sort?: string }> }) {
  return <BuildResourceIndexPage kind="items" searchParams={searchParams} />;
}
