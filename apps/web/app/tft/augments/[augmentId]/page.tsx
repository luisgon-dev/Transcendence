import Link from "next/link";

import { Card } from "@/components/ui/Card";
import { fetchBackendJson } from "@/lib/backendCall";
import { getBackendBaseUrl } from "@/lib/env";
import { type TftStaticEntity } from "@/lib/tft";

export default async function TftAugmentDetailPage({
  params
}: {
  params: Promise<{ augmentId: string }>;
}) {
  const { augmentId } = await params;
  const result = await fetchBackendJson<TftStaticEntity>(
    `${getBackendBaseUrl()}/api/tft/analytics/augments/${encodeURIComponent(augmentId)}`,
    { next: { revalidate: 60 * 60 } }
  );

  if (!result.ok || !result.body) {
    return <Card className="p-6">Augment not found.</Card>;
  }

  return (
    <Card className="grid gap-3 p-6">
      <Link href="/tft/augments" className="text-sm text-primary hover:underline">Back to augments</Link>
      <h1 className="font-[var(--font-sora)] text-3xl font-semibold tracking-tight">{result.body.name}</h1>
      <p className="text-sm text-muted">{result.body.apiName}</p>
      {result.body.description ? <p className="text-sm text-fg/80">{result.body.description}</p> : null}
    </Card>
  );
}
