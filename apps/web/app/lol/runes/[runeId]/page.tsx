import type { Metadata } from "next";

import { BuildResourceDetailPage } from "@/components/BuildResourcePages";
import { runeDisplayName } from "@/lib/gameDisplay";
import { fetchRunesReforged } from "@/lib/staticData";

export async function generateMetadata({ params }: { params: Promise<{ runeId: string }> }): Promise<Metadata> {
  const { runeId } = await params;
  const runeData = await fetchRunesReforged().catch(() => null);
  const name = runeDisplayName(runeData?.runeById[runeId]);
  return {
    title: `${name} Stats and Best Champions`,
    description: `${name} ranked pick rate, win rate, sample size, and champion-role usage.`
  };
}

export default async function RuneDetailPage({
  params,
  searchParams
}: {
  params: Promise<{ runeId: string }>;
  searchParams?: Promise<{ region?: string }>;
}) {
  const { runeId } = await params;
  return <BuildResourceDetailPage kind="runes" resourceId={Number(runeId)} searchParams={searchParams} />;
}
