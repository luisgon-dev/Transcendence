import type { Metadata } from "next";

import { BuildResourceDetailPage } from "@/components/BuildResourcePages";
import { itemDisplayName } from "@/lib/gameDisplay";
import { fetchItemMap } from "@/lib/staticData";

export async function generateMetadata({ params }: { params: Promise<{ itemId: string }> }): Promise<Metadata> {
  const { itemId } = await params;
  const itemMap = await fetchItemMap().catch(() => null);
  const name = itemDisplayName(itemMap?.items[itemId]);
  return {
    title: `${name} Stats and Champion Usage`,
    description: `${name} pick rate, win rate, game count, and which champions build it in ranked.`
  };
}

export default async function ItemDetailPage({
  params,
  searchParams
}: {
  params: Promise<{ itemId: string }>;
  searchParams?: Promise<{ region?: string }>;
}) {
  const { itemId } = await params;
  return <BuildResourceDetailPage kind="items" resourceId={Number(itemId)} searchParams={searchParams} />;
}
