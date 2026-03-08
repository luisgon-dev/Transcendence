import Link from "next/link";

import { Card } from "@/components/ui/Card";
import { fetchBackendJson } from "@/lib/backendCall";
import { getBackendBaseUrl } from "@/lib/env";
import { formatTftPercent, type TftCompDetail } from "@/lib/tft";

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

  if (!result.ok || !result.body) {
    return (
      <Card className="p-6">
        <p className="text-lg font-semibold text-fg">Comp not found.</p>
        <Link href="/tft/comps" className="mt-3 inline-block text-sm text-primary hover:underline">
          Back to comps
        </Link>
      </Card>
    );
  }

  const comp = result.body;

  return (
    <div className="grid gap-6">
      <section className="glass-card rounded-[2rem] p-6">
        <Link href="/tft/comps" className="text-sm text-primary hover:underline">
          Back to comps
        </Link>
        <h1 className="mt-3 font-[var(--font-sora)] text-3xl font-semibold tracking-tight">
          {comp.summary.name}
        </h1>
        <p className="mt-2 text-sm text-fg/75">
          {comp.summary.setCoreName ?? `Set ${comp.summary.setNumber ?? "?"}`} · {comp.summary.patch ?? "Unknown patch"} · {comp.summary.region}
        </p>

        <div className="mt-4 grid gap-3 md:grid-cols-4">
          <Card className="p-4"><p className="text-xs text-muted">Avg Placement</p><p className="mt-1 text-xl font-semibold">{comp.summary.avgPlacement.toFixed(2)}</p></Card>
          <Card className="p-4"><p className="text-xs text-muted">Top 4 Rate</p><p className="mt-1 text-xl font-semibold">{formatTftPercent(comp.summary.top4Rate)}</p></Card>
          <Card className="p-4"><p className="text-xs text-muted">Win Rate</p><p className="mt-1 text-xl font-semibold">{formatTftPercent(comp.summary.winRate)}</p></Card>
          <Card className="p-4"><p className="text-xs text-muted">Sample</p><p className="mt-1 text-xl font-semibold">{comp.summary.sampleSize.toLocaleString()}</p></Card>
        </div>
      </section>

      <div className="grid gap-4 lg:grid-cols-2">
        <Card className="p-5">
          <h2 className="font-[var(--font-sora)] text-xl font-semibold">Core Board</h2>
          <div className="mt-3 flex flex-wrap gap-2">
            {comp.summary.units.map((unit) => (
              <span key={unit.characterId} className="rounded-full border border-primary/35 bg-primary/10 px-3 py-1.5 text-sm text-primary">
                {unit.name ?? unit.characterId} {unit.tier > 1 ? `★${unit.tier}` : ""}
              </span>
            ))}
          </div>
          <p className="mt-4 text-sm text-fg/75">
            Traits: {comp.summary.traits.map((trait) => `${trait.name} ${trait.numUnits}`).join(" · ")}
          </p>
          <p className="mt-3 text-sm text-fg/75">
            Augments: {comp.summary.augments.length > 0 ? comp.summary.augments.join(", ") : "No augment summary yet."}
          </p>
        </Card>

        <Card className="p-5">
          <h2 className="font-[var(--font-sora)] text-xl font-semibold">Supporting Data</h2>
          <div className="mt-4 grid gap-4">
            <div>
              <p className="text-sm font-medium text-fg">Core Items</p>
              <p className="mt-2 text-sm text-fg/75">
                {comp.coreItems.length > 0 ? comp.coreItems.map((item) => item.name).join(", ") : "No item rollup yet."}
              </p>
            </div>
            <div>
              <p className="text-sm font-medium text-fg">Core Augments</p>
              <p className="mt-2 text-sm text-fg/75">
                {comp.coreAugments.length > 0 ? comp.coreAugments.map((augment) => augment.name).join(", ") : "No augment rollup yet."}
              </p>
            </div>
          </div>
        </Card>
      </div>
    </div>
  );
}
