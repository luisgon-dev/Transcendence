import Image from "next/image";
import Link from "next/link";

import { Card } from "@/components/ui/Card";
import { fetchBackendJson } from "@/lib/backendCall";
import { getBackendBaseUrl } from "@/lib/env";
import {
  buildTftEntityMap,
  formatTftDescription,
  formatTftEntityName,
  getTftCompositionEntities,
  isTftCraftableItem,
  tftIconObjectClass,
  tftIconUrl,
  type TftStaticEntity
} from "@/lib/tft";

export default async function TftItemDetailPage({
  params
}: {
  params: Promise<{ itemId: string }>;
}) {
  const { itemId } = await params;
  const [result, itemsResult] = await Promise.all([
    fetchBackendJson<TftStaticEntity>(
      `${getBackendBaseUrl()}/api/tft/analytics/items/${encodeURIComponent(itemId)}`,
      { next: { revalidate: 60 * 60 } }
    ),
    fetchBackendJson<TftStaticEntity[]>(`${getBackendBaseUrl()}/api/tft/analytics/items`, {
      next: { revalidate: 60 * 60 }
    })
  ]);

  if (!result.ok || !result.body) {
    return (
      <Card className="p-6">
        <p className="text-sm font-medium text-fg">Item not found</p>
        <p className="mt-1 text-xs text-muted">This item may have been removed or renamed in a recent patch.</p>
        <Link href="/tft/items" className="mt-3 inline-block text-sm text-primary hover:underline">Browse all items</Link>
      </Card>
    );
  }

  const entity = result.body;
  const iconSrc = tftIconUrl(entity.icon);
  const displayName = formatTftEntityName(entity);
  const description = formatTftDescription(entity.description);
  const itemMap = buildTftEntityMap(itemsResult.ok ? itemsResult.body ?? [] : []);
  const components = getTftCompositionEntities(entity, itemMap);

  return (
    <Card className="page-panel grid gap-4 p-6">
      <Link href="/tft/items" className="type-ui font-semibold text-primary hover:underline">Back to items</Link>
      <div className="flex items-center gap-4">
        {iconSrc ? (
          <div className="flex h-16 w-16 items-center justify-center overflow-hidden rounded-xl border border-border/50 bg-surface/60">
            <Image
              src={iconSrc}
              alt={displayName}
              width={64}
              height={64}
              sizes="64px"
              className={`h-16 w-16 ${tftIconObjectClass(entity.icon)}`}
            />
          </div>
        ) : (
          <div className="flex h-16 w-16 items-center justify-center rounded-xl border border-border/50 bg-primary/10 text-xl font-bold text-primary">
            {displayName.charAt(0)}
          </div>
        )}
        <div>
          <p className="type-kicker text-muted">TFT Item</p>
          <h1 className="type-page-title mt-2">{displayName}</h1>
          <p className="type-ui mt-2 text-muted">Item details</p>
        </div>
      </div>
      {description && <p className="type-ui text-fg/80">{description}</p>}
      {isTftCraftableItem(entity) && components.length > 0 && (
        <div className="grid gap-2 rounded-2xl border border-border/50 bg-surface/40 p-4">
          <p className="type-kicker text-muted">Recipe</p>
          <div className="flex flex-wrap items-center gap-2">
            {components.map((component, index) => {
              const componentIconSrc = tftIconUrl(component.icon);
              const componentName = formatTftEntityName(component);
              return (
                <div
                  key={`${component.apiName}-${index}`}
                  className="inline-flex items-center gap-2 rounded-full border border-border/50 bg-surface/78 px-3 py-2 text-sm text-fg/82 shadow-inset"
                >
                  {componentIconSrc ? (
                    <Image
                      src={componentIconSrc}
                      alt={componentName}
                      width={20}
                      height={20}
                      className={`h-5 w-5 ${tftIconObjectClass(component.icon)}`}
                      sizes="20px"
                    />
                  ) : (
                    <span className="flex h-5 w-5 items-center justify-center rounded-full bg-primary/12 text-[10px] font-bold text-primary">
                      {componentName.charAt(0)}
                    </span>
                  )}
                  <span>{componentName}</span>
                </div>
              );
            })}
          </div>
        </div>
      )}
    </Card>
  );
}
