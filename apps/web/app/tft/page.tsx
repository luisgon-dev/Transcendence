import Link from "next/link";

import { Card } from "@/components/ui/Card";
import { fetchBackendJson } from "@/lib/backendCall";
import { getBackendBaseUrl } from "@/lib/env";
import { formatTftPercent, type TftCompListItem, type TftStaticEntity } from "@/lib/tft";

export default async function TftHomePage() {
  const [compsResult, championsResult, itemsResult, traitsResult, augmentsResult] = await Promise.all([
    fetchBackendJson<TftCompListItem[]>(`${getBackendBaseUrl()}/api/tft/analytics/comps`, {
      next: { revalidate: 60 * 10 }
    }),
    fetchBackendJson<TftStaticEntity[]>(`${getBackendBaseUrl()}/api/tft/analytics/champions`, {
      next: { revalidate: 60 * 60 }
    }),
    fetchBackendJson<TftStaticEntity[]>(`${getBackendBaseUrl()}/api/tft/analytics/items`, {
      next: { revalidate: 60 * 60 }
    }),
    fetchBackendJson<TftStaticEntity[]>(`${getBackendBaseUrl()}/api/tft/analytics/traits`, {
      next: { revalidate: 60 * 60 }
    }),
    fetchBackendJson<TftStaticEntity[]>(`${getBackendBaseUrl()}/api/tft/analytics/augments`, {
      next: { revalidate: 60 * 60 }
    })
  ]);

  const comps = compsResult.ok ? compsResult.body ?? [] : [];
  const topComps = comps.slice(0, 6);
  const counts = [
    { label: "Units", value: championsResult.ok ? championsResult.body?.length ?? 0 : 0, href: "/tft/champions" },
    { label: "Items", value: itemsResult.ok ? itemsResult.body?.length ?? 0 : 0, href: "/tft/items" },
    { label: "Traits", value: traitsResult.ok ? traitsResult.body?.length ?? 0 : 0, href: "/tft/traits" },
    { label: "Augments", value: augmentsResult.ok ? augmentsResult.body?.length ?? 0 : 0, href: "/tft/augments" }
  ];

  return (
    <div className="grid gap-6">
      <section className="glass-card mesh-highlight rounded-[2rem] p-6 sm:p-8">
        <p className="text-sm font-medium uppercase tracking-[0.24em] text-primary">Teamfight Tactics</p>
        <h1 className="mt-3 max-w-3xl font-[var(--font-sora)] text-4xl font-semibold tracking-tight sm:text-5xl">
          Separate TFT pages, separate TFT data.
        </h1>
        <p className="mt-3 max-w-2xl text-sm text-fg/75 sm:text-base">
          Comp stats, catalog pages, and TFT summoner history now come from the dedicated `/api/tft/*` surface.
        </p>
        <div className="mt-5 flex flex-wrap gap-2">
          <Link href="/tft/comps" className="rounded-full border border-primary/45 bg-primary/12 px-4 py-2 text-sm font-medium text-primary">
            Open Comps
          </Link>
          <Link href="/tft/champions" className="rounded-full border border-border/60 px-4 py-2 text-sm text-fg/80 transition hover:bg-white/8 hover:text-fg">
            Units
          </Link>
          <Link href="/tft/items" className="rounded-full border border-border/60 px-4 py-2 text-sm text-fg/80 transition hover:bg-white/8 hover:text-fg">
            Items
          </Link>
          <Link href="/tft/summoners/na/Faker-KR1" className="rounded-full border border-border/60 px-4 py-2 text-sm text-fg/80 transition hover:bg-white/8 hover:text-fg">
            Profile Flow
          </Link>
        </div>
      </section>

      <section className="grid gap-4 md:grid-cols-4">
        {counts.map((item) => (
          <Link key={item.label} href={item.href}>
            <Card className="p-5 transition hover:bg-white/8">
              <p className="text-xs uppercase tracking-[0.2em] text-primary">{item.label}</p>
              <p className="mt-2 text-3xl font-semibold text-fg">{item.value.toLocaleString()}</p>
            </Card>
          </Link>
        ))}
      </section>

      <section className="grid gap-4">
        <div>
          <h2 className="font-[var(--font-sora)] text-2xl font-semibold">Top Comps</h2>
          <p className="text-sm text-fg/75">Default filters use the active set and Emerald+.</p>
        </div>

        <div className="grid gap-3 lg:grid-cols-2 xl:grid-cols-3">
          {topComps.map((comp) => (
            <Link key={comp.compSlug} href={`/tft/comps/${comp.compSlug}`}>
              <Card className="grid gap-3 p-5 transition hover:bg-white/8">
                <div className="flex items-start justify-between gap-3">
                  <div>
                    <p className="text-lg font-semibold text-fg">{comp.name}</p>
                    <p className="text-xs text-muted">
                      {comp.setCoreName ?? `Set ${comp.setNumber ?? "?"}`} · {comp.patch ?? "Unknown patch"}
                    </p>
                  </div>
                  <span className="rounded-full border border-primary/35 bg-primary/10 px-2.5 py-1 text-xs text-primary">
                    {comp.trend}
                  </span>
                </div>

                <div className="grid grid-cols-2 gap-2 text-sm text-fg/80">
                  <p>Avg Place {comp.avgPlacement.toFixed(2)}</p>
                  <p>Top 4 {formatTftPercent(comp.top4Rate)}</p>
                  <p>Win {formatTftPercent(comp.winRate)}</p>
                  <p>Sample {comp.sampleSize.toLocaleString()}</p>
                </div>

                <p className="text-xs text-fg/75">
                  Traits: {comp.traits.map((trait) => `${trait.name} ${trait.numUnits}`).join(" · ")}
                </p>
              </Card>
            </Link>
          ))}
        </div>
      </section>
    </div>
  );
}
