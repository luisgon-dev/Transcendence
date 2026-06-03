import Image from "next/image";
import Link from "next/link";

import { BackendErrorCard } from "@/components/BackendErrorCard";
import { Badge } from "@/components/ui/Badge";
import { Card } from "@/components/ui/Card";
import { Stat } from "@/components/ui/Stat";
import { fetchBackendJson } from "@/lib/backendCall";
import { getBackendBaseUrl, getErrorVerbosity } from "@/lib/env";
import {
  compTierColorClass,
  compTierLabel,
  formatTftPercent,
  tftIconUrl,
  type TftCompDetail
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
  const qs = new URLSearchParams();
  if (resolved?.rankTier) qs.set("rankTier", resolved.rankTier);
  if (resolved?.region) qs.set("region", resolved.region);

  const result = await fetchBackendJson<TftCompDetail>(
    `${getBackendBaseUrl()}/api/tft/analytics/comps/${encodeURIComponent(compSlug)}${qs.toString() ? `?${qs.toString()}` : ""}`,
    { next: { revalidate: 60 * 10 } }
  );

  if (!result.ok && (result.errorKind === "timeout" || result.errorKind === "unreachable")) {
    const verbosity = getErrorVerbosity();
    return (
      <BackendErrorCard
        title="TFT comp"
        message={
          result.errorKind === "timeout"
            ? "This comp is taking too long to load."
            : "We couldn't reach the TFT service right now."
        }
        hint="Try again in a moment."
        requestId={result.requestId}
        detail={
          verbosity === "verbose"
            ? JSON.stringify({ status: result.status, errorKind: result.errorKind }, null, 2)
            : null
        }
      />
    );
  }

  if (!result.ok || !result.body) {
    return (
      <Card className="p-6">
        <p className="text-sm font-medium text-fg">Comp “{compSlug}” not found</p>
        <p className="mt-1 text-xs text-muted">This comp may no longer be tracked or may have changed with a recent patch.</p>
        <Link href={`/tft/comps${qs.toString() ? `?${qs.toString()}` : ""}`} className="mt-3 inline-block text-sm text-primary hover:underline">Browse all comps</Link>
      </Card>
    );
  }

  const comp = result.body;
  const tier = compTierLabel(comp.summary.avgPlacement);
  const tierColor = compTierColorClass(tier);

  return (
    <div className="grid gap-6">
      <section className="page-hero p-6">
        <Link href={`/tft/comps${qs.toString() ? `?${qs.toString()}` : ""}`} className="type-ui font-semibold text-primary hover:underline">
          Back to comps
        </Link>
        <div className="mt-3 flex items-center gap-3">
          <h1 className="type-page-title">
            {comp.summary.name}
          </h1>
          <Badge className={`rounded-md px-2.5 py-1 text-sm font-bold ${tierColor}`}>
            {tier} Tier
          </Badge>
        </div>
        <p className="type-ui mt-3 text-fg/75">
          {comp.summary.setCoreName ?? `Set ${comp.summary.setNumber ?? "?"}`} · {comp.summary.patch ?? "Unknown patch"} · {comp.summary.region}
        </p>

        <div className="mt-4 grid gap-3 sm:grid-cols-2 md:grid-cols-4">
          <div className="page-stat-card"><Stat label="Avg Placement" value={comp.summary.avgPlacement.toFixed(2)} /></div>
          <div className="page-stat-card"><Stat label="Top 4 Rate" value={formatTftPercent(comp.summary.top4Rate)} /></div>
          <div className="page-stat-card"><Stat label="Win Rate" value={formatTftPercent(comp.summary.winRate)} /></div>
          <div className="page-stat-card"><Stat label="Games" value={comp.summary.sampleSize.toLocaleString()} /></div>
        </div>
      </section>

      <div className="grid gap-4 lg:grid-cols-2">
        <Card className="p-5">
          <h2 className="type-section">Core Board</h2>
          <div className="mt-3 flex flex-wrap gap-2">
            {comp.summary.units.map((unit) => (
              <span key={unit.characterId} className="inline-flex items-center gap-1 rounded-lg border border-border bg-surface-2/70 px-3 py-1.5 text-sm font-medium text-fg/90">
                {unit.name ?? unit.characterId}
                {unit.tier > 1 ? <span className="text-primary">{"★".repeat(unit.tier)}</span> : null}
              </span>
            ))}
          </div>
          <div className="mt-3 flex flex-wrap gap-1">
            {comp.summary.traits.map((trait) => (
              <span key={trait.name} className="rounded-full border border-border/50 bg-surface/40 px-2 py-0.5 text-xs text-fg/70">
                {trait.name} {trait.numUnits}
              </span>
            ))}
          </div>
          {comp.summary.augments.length > 0 && (
            <p className="mt-3 text-sm text-fg/75">
              Augments: {comp.summary.augments.map((a) => a.replace(/^TFT\d+_/i, "").replace(/_/g, " ")).join(", ")}
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
