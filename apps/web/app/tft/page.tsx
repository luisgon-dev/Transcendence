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
      <section className="page-hero p-6 sm:p-8">
        <p className="type-kicker text-primary">Teamfight Tactics</p>
        <h1 className="type-display mt-4 max-w-4xl">
          TFT comps, unit lookups, and player pages built for the live set.
        </h1>
        <p className="type-lead mt-4 max-w-3xl">
          Find the best-performing comps, browse units and items, and check how players are finishing their recent games.
        </p>
        <div className="mt-5 flex flex-wrap gap-x-4 gap-y-2">
          <Link href="/tft/comps" className="control-chip type-ui font-semibold" data-active="true">
            Explore Comps
          </Link>
          <Link href="/tft/champions" className="control-chip type-ui">
            Units
          </Link>
          <Link href="/tft/items" className="control-chip type-ui">
            Items
          </Link>
          <Link href="/tft/summoners/na/Faker-KR1" className="control-chip type-ui">
            Player Search
          </Link>
        </div>
      </section>

      <section className="grid gap-4 md:grid-cols-4">
        {counts.map((item) => (
          <Link key={item.label} href={item.href}>
            <Card className="page-stat-card p-5">
              <p className="type-kicker text-primary">{item.label}</p>
              <p className="mt-2 text-3xl font-semibold text-fg">{item.value.toLocaleString()}</p>
            </Card>
          </Link>
        ))}
      </section>

      <section className="grid gap-4">
        <div>
          <h2 className="type-title">Top Comps</h2>
          <p className="type-ui mt-2 text-fg/75">The default view starts on the live set at Emerald+.</p>
        </div>

        <div className="grid gap-3 lg:grid-cols-2 xl:grid-cols-3">
          {topComps.map((comp) => (
            <Link key={comp.compSlug} href={`/tft/comps/${comp.compSlug}`}>
              <Card className="grid gap-3 p-5">
                <div className="flex items-start justify-between gap-3">
                  <div>
                    <p className="text-lg font-semibold text-fg">{comp.name}</p>
                    <p className="text-xs text-muted">
                      {comp.setCoreName ?? `Set ${comp.setNumber ?? "?"}`} · {comp.patch ?? "Unknown patch"}
                    </p>
                  </div>
                  <span className="type-kicker text-primary">
                    {comp.trend}
                  </span>
                </div>

                <div className="grid grid-cols-2 gap-2 text-sm text-fg/80">
                  <p>Avg Place {comp.avgPlacement.toFixed(2)}</p>
                  <p>Top 4 {formatTftPercent(comp.top4Rate)}</p>
                  <p>Win {formatTftPercent(comp.winRate)}</p>
                  <p>Games {comp.sampleSize.toLocaleString()}</p>
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
