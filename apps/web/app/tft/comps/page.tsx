import Link from "next/link";

import { Card } from "@/components/ui/Card";
import { fetchBackendJson } from "@/lib/backendCall";
import { getBackendBaseUrl } from "@/lib/env";
import { formatTftPercent, TFT_RANK_OPTIONS, TFT_REGION_OPTIONS, type TftCompListItem } from "@/lib/tft";

export default async function TftCompsPage({
  searchParams
}: {
  searchParams?: Promise<{ rankTier?: string; region?: string }>;
}) {
  const resolved = searchParams ? await searchParams : undefined;
  const rankTier = resolved?.rankTier?.trim().toUpperCase() || "EMERALD_PLUS";
  const region = resolved?.region?.trim().toUpperCase() || "ALL";
  const qs = new URLSearchParams();
  if (rankTier !== "EMERALD_PLUS") qs.set("rankTier", rankTier);
  if (region !== "ALL") qs.set("region", region);

  const result = await fetchBackendJson<TftCompListItem[]>(
    `${getBackendBaseUrl()}/api/tft/analytics/comps${qs.toString() ? `?${qs.toString()}` : ""}`,
    { next: { revalidate: 60 * 10 } }
  );

  const comps = result.ok ? result.body ?? [] : [];

  return (
    <div className="grid gap-6">
      <section className="glass-card rounded-[2rem] p-6">
        <h1 className="font-[var(--font-sora)] text-3xl font-semibold tracking-tight">TFT Comp Tier List</h1>
        <p className="mt-2 text-sm text-fg/75">
          Fully separate from the LoL tier list surface. This page reads only `/api/tft/analytics/comps`.
        </p>
        <form className="mt-4 flex flex-wrap gap-2" action="/tft/comps" method="get">
          <select name="rankTier" defaultValue={rankTier} className="h-11 rounded-md border border-border/70 bg-surface/35 px-3 text-sm text-fg">
            {TFT_RANK_OPTIONS.map((option) => (
              <option key={option} value={option}>
                {option}
              </option>
            ))}
          </select>
          <select name="region" defaultValue={region} className="h-11 rounded-md border border-border/70 bg-surface/35 px-3 text-sm text-fg">
            {TFT_REGION_OPTIONS.map((option) => (
              <option key={option} value={option}>
                {option}
              </option>
            ))}
          </select>
          <button type="submit" className="rounded-md border border-primary/45 bg-primary/12 px-4 text-sm font-medium text-primary">
            Apply
          </button>
        </form>
      </section>

      <div className="grid gap-3 lg:grid-cols-2">
        {comps.map((comp) => (
          <Link key={comp.compSlug} href={`/tft/comps/${comp.compSlug}${qs.toString() ? `?${qs.toString()}` : ""}`}>
            <Card className="grid gap-3 p-5 transition hover:bg-white/8">
              <div className="flex items-start justify-between gap-3">
                <div>
                  <p className="text-lg font-semibold text-fg">{comp.name}</p>
                  <p className="text-xs text-muted">
                    {comp.setCoreName ?? `Set ${comp.setNumber ?? "?"}`} · {comp.patch ?? "Unknown patch"}
                  </p>
                </div>
                <span className="rounded-full border border-border/60 px-2.5 py-1 text-xs text-fg/80">
                  {comp.rankTier}
                </span>
              </div>

              <div className="grid grid-cols-2 gap-2 text-sm text-fg/80">
                <p>Avg Place {comp.avgPlacement.toFixed(2)}</p>
                <p>Top 4 {formatTftPercent(comp.top4Rate)}</p>
                <p>Win {formatTftPercent(comp.winRate)}</p>
                <p>Pick {formatTftPercent(comp.pickRate)}</p>
              </div>

              <p className="text-xs text-fg/75">
                Units: {comp.units.map((unit) => unit.name ?? unit.characterId).join(" · ")}
              </p>
              <p className="text-xs text-fg/75">
                Traits: {comp.traits.map((trait) => `${trait.name} ${trait.numUnits}`).join(" · ")}
              </p>
            </Card>
          </Link>
        ))}
      </div>
    </div>
  );
}
