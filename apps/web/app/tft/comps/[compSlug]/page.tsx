import Image from "next/image";
import Link from "next/link";

import { Badge } from "@/components/ui/Badge";
import { Card } from "@/components/ui/Card";
import { fetchBackendJson } from "@/lib/backendCall";
import { getBackendBaseUrl } from "@/lib/env";
import {
  buildTftLabelMap,
  compTierColorClass,
  compTierLabel,
  formatTftPercent,
  formatTftCompName,
  formatTftLabel,
  formatTftUnitLabel,
  tftIconUrl,
  type TftCompDetail,
  type TftStaticEntity
} from "@/lib/tft";

export default async function TftCompDetailPage({
  params,
  searchParams
}: {
  params: Promise<{ compSlug: string }>;
  searchParams?: Promise<{ rankTier?: string; region?: string }>;
}) {
  const { compSlug } = await params;
  const resolved = searchParams ? await searchParams : undefined;
  const rankTier = resolved?.rankTier?.trim().toUpperCase() || "EMERALD_PLUS";
  const region = resolved?.region?.trim().toUpperCase() || "ALL";
  const qs = new URLSearchParams();
  if (rankTier !== "EMERALD_PLUS") qs.set("rankTier", rankTier);
  if (region !== "ALL") qs.set("region", region);

  let [result, championsResult, traitsResult] = await Promise.all([
    fetchBackendJson<TftCompDetail>(
      `${getBackendBaseUrl()}/api/tft/analytics/comps/${encodeURIComponent(compSlug)}${qs.toString() ? `?${qs.toString()}` : ""}`,
      { next: { revalidate: 60 * 10 } }
    ),
    fetchBackendJson<TftStaticEntity[]>(`${getBackendBaseUrl()}/api/tft/analytics/champions`, {
      next: { revalidate: 60 * 60 }
    }),
    fetchBackendJson<TftStaticEntity[]>(`${getBackendBaseUrl()}/api/tft/analytics/traits`, {
      next: { revalidate: 60 * 60 }
    })
  ]);

  let effectiveRankTier = rankTier;
  if ((!result.ok || !result.body) && rankTier === "EMERALD_PLUS") {
    const fallbackQs = new URLSearchParams();
    fallbackQs.set("rankTier", "ALL");
    if (region !== "ALL") fallbackQs.set("region", region);
    result = await fetchBackendJson<TftCompDetail>(
      `${getBackendBaseUrl()}/api/tft/analytics/comps/${encodeURIComponent(compSlug)}?${fallbackQs.toString()}`,
      { next: { revalidate: 60 * 10 } }
    );
    if (result.ok && result.body) effectiveRankTier = "ALL";
  }

  if (!result.ok || !result.body) {
    return (
      <Card className="p-6">
        <p className="text-sm font-medium text-fg">Comp not found</p>
        <p className="mt-1 text-xs text-muted">This comp may no longer be tracked or may have changed with a recent patch.</p>
        <Link href="/tft/comps" className="mt-3 inline-block text-sm text-primary hover:underline">Browse all comps</Link>
      </Card>
    );
  }

  const comp = result.body;
  const unitLabels = championsResult.ok ? buildTftLabelMap(championsResult.body ?? []) : undefined;
  const traitLabels = traitsResult.ok ? buildTftLabelMap(traitsResult.body ?? []) : undefined;
  const tier = compTierLabel(comp.summary.avgPlacement);
  const tierColor = compTierColorClass(tier);

  return (
    <div className="grid gap-6">
      <section className="page-hero p-6">
        <Link href="/tft/comps" className="type-ui font-semibold text-primary hover:underline">
          Back to comps
        </Link>
        {effectiveRankTier !== rankTier ? (
          <p className="mt-3 text-sm text-warning">
            This comp is being shown from all tracked ranks because Emerald+ local TFT data is not populated yet.
          </p>
        ) : null}
        <div className="mt-3 flex items-center gap-3">
          <h1 className="type-page-title">
            {formatTftCompName(comp.summary.name, { traitLabels, unitLabels })}
          </h1>
          <Badge className={`rounded-md px-2.5 py-1 text-sm font-bold ${tierColor}`}>
            {tier} Tier
          </Badge>
        </div>
        <p className="type-ui mt-3 text-fg/75">
          {comp.summary.setCoreName ?? `Set ${comp.summary.setNumber ?? "?"}`} · {comp.summary.patch ?? "Unknown patch"} · {comp.summary.region}
        </p>

        <div className="mt-4 grid gap-3 md:grid-cols-4">
          <Card className="page-stat-card p-4"><p className="type-kicker text-muted">Avg Placement</p><p className="mt-1 text-xl font-semibold">{comp.summary.avgPlacement.toFixed(2)}</p></Card>
          <Card className="page-stat-card p-4"><p className="type-kicker text-muted">Top 4 Rate</p><p className="mt-1 text-xl font-semibold">{formatTftPercent(comp.summary.top4Rate)}</p></Card>
          <Card className="page-stat-card p-4"><p className="type-kicker text-muted">Win Rate</p><p className="mt-1 text-xl font-semibold">{formatTftPercent(comp.summary.winRate)}</p></Card>
          <Card className="page-stat-card p-4"><p className="type-kicker text-muted">Games</p><p className="mt-1 text-xl font-semibold">{comp.summary.sampleSize.toLocaleString()}</p></Card>
        </div>
      </section>

      <div className="grid gap-4 lg:grid-cols-2">
        <Card className="p-5">
          <h2 className="type-section">Core Board</h2>
          <div className="mt-3 flex flex-wrap gap-2">
            {comp.summary.units.map((unit) => (
              <span key={unit.characterId} className="rounded border border-primary/35 bg-primary/10 px-3 py-1.5 text-sm font-medium text-primary">
                {formatTftUnitLabel(unit, unitLabels)} {unit.tier > 1 ? `${"★".repeat(unit.tier)}` : ""}
              </span>
            ))}
          </div>
          <div className="mt-3 flex flex-wrap gap-1">
            {comp.summary.traits.map((trait) => (
              <span key={trait.name} className="rounded-full border border-border/50 bg-surface/40 px-2 py-0.5 text-xs text-fg/70">
                {formatTftLabel(trait.name, traitLabels)} {trait.numUnits}
              </span>
            ))}
          </div>
          {comp.summary.augments.length > 0 && (
            <p className="mt-3 text-sm text-fg/75">
              Augments: {comp.summary.augments.map((a) => formatTftLabel(a)).join(", ")}
            </p>
          )}
        </Card>

        <Card className="p-5">
          <h2 className="type-section">Recommended Extras</h2>
          <div className="mt-4 grid gap-4">
            <div>
              <p className="text-sm font-medium text-fg">Core Items</p>
              {comp.coreItems.length > 0 ? (
                <div className="mt-2 flex flex-wrap gap-2">
                  {comp.coreItems.map((item) => {
                    const iconSrc = tftIconUrl(item.icon);
                    return (
                      <div key={item.apiName} className="flex items-center gap-1.5 rounded border border-border/50 bg-surface/40 px-2 py-1">
                        {iconSrc && (
                          <Image src={iconSrc} alt={item.name} width={20} height={20} sizes="20px" className="rounded" />
                        )}
                        <span className="text-xs text-fg/80">{item.name}</span>
                      </div>
                    );
                  })}
                </div>
              ) : (
                <p className="mt-2 text-sm text-fg/60">Item recommendations are not available for this comp yet.</p>
              )}
            </div>
            <div>
              <p className="text-sm font-medium text-fg">Core Augments</p>
              {comp.coreAugments.length > 0 ? (
                <div className="mt-2 flex flex-wrap gap-2">
                  {comp.coreAugments.map((augment) => {
                    const iconSrc = tftIconUrl(augment.icon);
                    return (
                      <div key={augment.apiName} className="flex items-center gap-1.5 rounded border border-border/50 bg-surface/40 px-2 py-1">
                        {iconSrc && (
                          <Image src={iconSrc} alt={augment.name} width={20} height={20} sizes="20px" className="rounded" />
                        )}
                        <span className="text-xs text-fg/80">{augment.name}</span>
                      </div>
                    );
                  })}
                </div>
              ) : (
                <p className="mt-2 text-sm text-fg/60">Augment recommendations are not available for this comp yet.</p>
              )}
            </div>
          </div>
        </Card>
      </div>
    </div>
  );
}
