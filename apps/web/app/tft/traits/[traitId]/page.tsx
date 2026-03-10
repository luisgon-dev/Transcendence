import Image from "next/image";
import Link from "next/link";

import { Card } from "@/components/ui/Card";
import { fetchBackendJson } from "@/lib/backendCall";
import { getBackendBaseUrl } from "@/lib/env";
import { tftIconUrl, type TftStaticEntity } from "@/lib/tft";

export default async function TftTraitDetailPage({
  params
}: {
  params: Promise<{ traitId: string }>;
}) {
  const { traitId } = await params;
  const result = await fetchBackendJson<TftStaticEntity>(
    `${getBackendBaseUrl()}/api/tft/analytics/traits/${encodeURIComponent(traitId)}`,
    { next: { revalidate: 60 * 60 } }
  );

  if (!result.ok || !result.body) {
    return <Card className="p-6">Trait not found.</Card>;
  }

  const entity = result.body;
  const iconSrc = tftIconUrl(entity.icon);

  return (
    <Card className="grid gap-4 p-6">
      <Link href="/tft/traits" className="text-sm text-primary hover:underline">Back to traits</Link>
      <div className="flex items-center gap-4">
        {iconSrc ? (
          <Image src={iconSrc} alt={entity.name} width={64} height={64} className="rounded-xl" unoptimized />
        ) : (
          <div className="flex h-16 w-16 items-center justify-center rounded-xl border border-border/50 bg-primary/10 text-xl font-bold text-primary">
            {entity.name.charAt(0)}
          </div>
        )}
        <div>
          <h1 className="font-[var(--font-sora)] text-3xl font-semibold tracking-tight">{entity.name}</h1>
          <p className="text-sm text-muted">Trait details</p>
        </div>
      </div>
      {entity.description && <p className="text-sm text-fg/80">{entity.description}</p>}
    </Card>
  );
}
