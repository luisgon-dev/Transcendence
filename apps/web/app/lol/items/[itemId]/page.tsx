import type { Metadata } from "next";

import { BuildResourceDetailPage } from "@/components/BuildResourcePages";
import { fetchItemMap } from "@/lib/staticData";

export async function generateMetadata({ params }: { params: Promise<{ itemId: string }> }): Promise<Metadata> {
  const { itemId } = await params;
  const itemMap = await fetchItemMap().catch(() => null);
  const name = itemMap?.items[itemId]?.name ?? `Item ${itemId}`;
  return {
    title: `${name} Stats and Best Champions`,
    description: `${name} ranked pick rate, win rate, sample size, and champion-role usage.`
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
