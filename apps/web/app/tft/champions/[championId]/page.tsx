import Image from "next/image";
import Link from "next/link";

import { Card } from "@/components/ui/Card";
import { fetchBackendJson } from "@/lib/backendCall";
import { getBackendBaseUrl } from "@/lib/env";
import { tftIconUrl, type TftStaticEntity } from "@/lib/tft";

export default async function TftChampionDetailPage({
  params
}: {
  params: Promise<{ championId: string }>;
}) {
  const { championId } = await params;
  const result = await fetchBackendJson<TftStaticEntity>(
    `${getBackendBaseUrl()}/api/tft/analytics/champions/${encodeURIComponent(championId)}`,
    { next: { revalidate: 60 * 60 } }
  );

  if (!result.ok || !result.body) {
    return (
      <Card className="p-6">
        <p className="text-sm font-medium text-fg">Unit not found</p>
        <p className="mt-1 text-xs text-muted">This unit may have been removed or reworked in a recent set update.</p>
        <Link href="/tft/champions" className="mt-3 inline-block text-sm text-primary hover:underline">Browse all units</Link>
      </Card>
    );
  }

  const entity = result.body;
  const iconSrc = tftIconUrl(entity.icon);

  return (
    <Card className="page-panel grid gap-4 p-6">
      <Link href="/tft/champions" className="type-ui font-semibold text-primary hover:underline">Back to units</Link>
      <div className="flex items-center gap-4">
        {iconSrc ? (
          <Image src={iconSrc} alt={entity.name} width={64} height={64} sizes="64px" className="rounded-xl" />
        ) : (
          <div className="flex h-16 w-16 items-center justify-center rounded-xl border border-border/50 bg-primary/10 text-xl font-bold text-primary">
            {entity.name.charAt(0)}
          </div>
        )}
        <div>
          <p className="type-kicker text-muted">TFT Unit</p>
          <h1 className="type-page-title mt-2">{entity.name}</h1>
          <p className="type-ui mt-2 text-muted">Unit details</p>
        </div>
      </div>
      {entity.description && <p className="type-ui text-fg/80">{entity.description}</p>}
    </Card>
  );
}
