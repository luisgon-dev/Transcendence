import Link from "next/link";

import { Card } from "@/components/ui/Card";
import { fetchBackendJson } from "@/lib/backendCall";
import { getBackendBaseUrl } from "@/lib/env";
import { type TftStaticEntity } from "@/lib/tft";

export default async function TftItemDetailPage({
  params
}: {
  params: Promise<{ itemId: string }>;
}) {
  const { itemId } = await params;
  const result = await fetchBackendJson<TftStaticEntity>(
    `${getBackendBaseUrl()}/api/tft/analytics/items/${encodeURIComponent(itemId)}`,
    { next: { revalidate: 60 * 60 } }
  );

  if (!result.ok || !result.body) {
    return <Card className="p-6">Item not found.</Card>;
  }

  return (
    <Card className="grid gap-3 p-6">
      <Link href="/tft/items" className="text-sm text-primary hover:underline">Back to items</Link>
      <h1 className="font-[var(--font-sora)] text-3xl font-semibold tracking-tight">{result.body.name}</h1>
      <p className="text-sm text-muted">{result.body.apiName}</p>
      {result.body.description ? <p className="text-sm text-fg/80">{result.body.description}</p> : null}
    </Card>
  );
}
