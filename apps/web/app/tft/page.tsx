import Link from "next/link";

import { Card } from "@/components/ui/Card";
import { Toolbar } from "@/components/ui/Toolbar";
import { fetchBackendJson } from "@/lib/backendCall";
import { getBackendBaseUrl } from "@/lib/env";
import { formatTftPercent, type TftCompListItem, type TftStaticEntity } from "@/lib/tft";

const EXAMPLE_SUMMONER_HREF = "/tft/summoners/kr/Hide%20on%20bush-KR1";

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
      <Toolbar
        eyebrow="Teamfight Tactics"
        title="Comps & Analytics"
        meta={<span>Best comps, unit lookups, and player pages for the live set</span>}
        actions={
          <>
            <Link
              href="/tft/comps"
              className="type-ui inline-flex h-9 items-center rounded-control bg-primary px-4 font-semibold text-primary-fg transition-colors hover:bg-primary/92"
            >
              Explore Comps
            </Link>
            <Link
              href={EXAMPLE_SUMMONER_HREF}
              className="type-ui inline-flex h-9 items-center rounded-control border border-border px-4 font-medium text-fg/85 transition-colors hover:border-border-strong hover:text-fg"
            >
              Example Profile
            </Link>
          </>
        }
      />

      <section className="flex flex-wrap items-center gap-2">
        <p className="type-kicker mr-1 text-muted">Browse</p>
        {counts.map((item) => (
          <Link
            key={item.label}
            href={item.href}
            className="surface-chip type-ui inline-flex items-center gap-2 rounded-xl px-3 py-1.5 text-fg/80 transition hover:border-border-strong/70 hover:text-fg"
          >
            {item.label}
            <span className="type-tabular text-fg/55">{item.value.toLocaleString()}</span>
          </Link>
        ))}
      </section>

      <section className="grid gap-4">
        <div>
          <h2 className="type-title">Top Comps</h2>
          <p className="type-ui mt-2 text-fg/75">Emerald+ ranked · live set.</p>
        </div>

        {topComps.length === 0 ? (
          <Card className="p-5">
            <p className="text-sm text-fg/75">No comp data yet for the live set.</p>
            <p className="mt-1 text-sm text-fg/60">
              Comps populate as ranked TFT matches are ingested — check back soon.
            </p>
          </Card>
        ) : (
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
                  <span className="type-kicker text-muted">
                    {comp.trend}
                  </span>
                </div>

                <div className="type-tabular grid grid-cols-2 gap-2 text-sm text-fg/80">
                  <p>Avg Place {comp.avgPlacement.toFixed(2)}</p>
                  <p>Top 4 {formatTftPercent(comp.top4Rate)}</p>
                  <p>Win {formatTftPercent(comp.winRate)}</p>
                  <p>Games {comp.sampleSize.toLocaleString()}</p>
                </div>

                <p className="text-xs text-fg/75">
                  Traits: {comp.traits.length > 0
                    ? comp.traits.map((trait) => `${trait.name} ${trait.numUnits}`).join(" · ")
                    : "None listed"}
                </p>
              </Card>
            </Link>
          ))}
        </div>
        )}
      </section>
    </div>
  );
}
