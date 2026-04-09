import Image from "next/image";
import Link from "next/link";

import { AnalyticsSampleBanner } from "@/components/AnalyticsSampleBanner";
import { SearchBar } from "@/components/SearchBar";
import { Card } from "@/components/ui/Card";
import { type AnalyticsSampleLike } from "@/lib/analyticsSample";
import { fetchBackendJson } from "@/lib/backendCall";
import { getBackendBaseUrl } from "@/lib/env";
import {
  buildTftEntityMap,
  buildTftLabelMap,
  formatTftPercent,
  formatTftCompName,
  formatTftLabel,
  getTftEffectiveSampleSize,
  getTftMetaScore,
  tftIconUrl,
  type TftCompListItem,
  type TftStaticEntity
} from "@/lib/tft";

const EXAMPLE_SUMMONER_HREF = "/tft/summoners/na/Kronic-NA1";

export default async function TftHomePage() {
  let [compsResult, championsResult, itemsResult, traitsResult, augmentsResult] = await Promise.all([
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

  let comps = compsResult.ok ? compsResult.body ?? [] : [];
  if (comps.length === 0) {
    compsResult = await fetchBackendJson<TftCompListItem[]>(`${getBackendBaseUrl()}/api/tft/analytics/comps?rankTier=ALL`, {
      next: { revalidate: 60 * 10 }
    });
    comps = compsResult.ok ? compsResult.body ?? [] : [];
  }
  const unitEntities = championsResult.ok ? buildTftEntityMap(championsResult.body ?? []) : undefined;
  const unitLabels = championsResult.ok ? buildTftLabelMap(championsResult.body ?? []) : undefined;
  const traitLabels = traitsResult.ok ? buildTftLabelMap(traitsResult.body ?? []) : undefined;
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
        <p className="type-kicker text-muted">Teamfight Tactics</p>
        <h1 className="type-display mt-4 max-w-4xl">
          TFT comps, unit lookups, and player pages built for the live set.
        </h1>
        <p className="type-lead mt-4 max-w-3xl">
          Find the best-performing comps, browse units and items, and check how players are finishing their recent games.
        </p>
        <div className="mt-5 max-w-3xl">
          <SearchBar game="tft" />
        </div>
        <div className="mt-5 flex flex-wrap gap-x-4 gap-y-2">
          <Link href="/tft/comps" className="control-tab type-ui font-semibold" data-active="true">
            Explore Comps
          </Link>
          <Link href="/tft/champions" className="control-tab type-ui">
            Units
          </Link>
          <Link href="/tft/items" className="control-tab type-ui">
            Items
          </Link>
          <Link href={EXAMPLE_SUMMONER_HREF} className="control-tab type-ui">
            Example Profile
          </Link>
        </div>
        {comps.length > 0 && comps[0]?.rankTier === "ALL" ? (
          <p className="mt-3 text-sm text-fg/66">
            Emerald+ local TFT data is not populated yet, so this overview is using all tracked ranks.
          </p>
        ) : null}
        <div className="mt-4">
          <AnalyticsSampleBanner sample={(topComps[0] ?? null) as AnalyticsSampleLike} />
        </div>
      </section>

      <section className="grid gap-4 md:grid-cols-4">
        {counts.map((item) => (
          <Link key={item.label} href={item.href}>
            <Card className="page-stat-card p-5">
              <p className="type-kicker text-muted">{item.label}</p>
              <p className="mt-2 text-3xl font-semibold text-fg">{item.value.toLocaleString()}</p>
            </Card>
          </Link>
        ))}
      </section>

      <section className="grid gap-4">
        <div>
          <h2 className="type-title">Top Comps</h2>
          <p className="type-ui mt-2 text-fg/75">
            {comps[0]?.rankTier === "ALL"
              ? "The local stack is currently showing all tracked TFT ranks."
              : "The default view starts on the live set at Emerald+."}
          </p>
        </div>

        <div className="grid gap-3 lg:grid-cols-2 xl:grid-cols-3">
          {topComps.map((comp) => (
            <Link key={comp.compSlug} href={`/tft/comps/${comp.compSlug}`}>
              <Card className="grid gap-3 p-5">
                <div className="flex items-start justify-between gap-3">
                  <div>
                    <p className="text-lg font-semibold text-fg">
                      {formatTftCompName(comp.name, { traitLabels, unitLabels })}
                    </p>
                    <p className="text-xs text-muted">
                      {comp.setCoreName ?? `Set ${comp.setNumber ?? "?"}`} · {comp.patch ?? "Unknown patch"}
                    </p>
                  </div>
                  <span className="type-kicker text-muted">
                    {comp.trend}
                  </span>
                </div>

                <div className="grid grid-cols-2 gap-2 text-sm text-fg/80">
                  <p>
                    {typeof comp.metaScore === "number"
                      ? `Meta ${getTftMetaScore(comp).toFixed(2)}`
                      : `Top 4 ${formatTftPercent(comp.top4Rate)}`}
                  </p>
                  <p>Avg Place {comp.avgPlacement.toFixed(2)}</p>
                  <p>Win {formatTftPercent(comp.winRate)}</p>
                  <p>Games {Math.round(getTftEffectiveSampleSize(comp)).toLocaleString()}</p>
                </div>

                <div className="flex flex-wrap gap-2">
                  {comp.units.slice(0, 4).map((unit) => {
                    const iconSrc = tftIconUrl(unitEntities?.[unit.characterId]?.icon);
                    return (
                      <span
                        key={unit.characterId}
                        className="inline-flex items-center gap-2 rounded-full border border-primary/20 bg-primary/8 px-2 py-1 text-xs font-medium text-primary"
                      >
                        {iconSrc ? (
                          <Image
                            src={iconSrc}
                            alt={formatTftLabel(unit.characterId, unitLabels)}
                            width={24}
                            height={24}
                            sizes="24px"
                            className="h-6 w-6 rounded-md object-cover"
                          />
                        ) : null}
                        <span>{formatTftLabel(unit.characterId, unitLabels)}</span>
                      </span>
                    );
                  })}
                </div>

                <p className="text-xs text-fg/75">
                  Traits: {comp.traits
                    .map((trait) => `${formatTftLabel(trait.name, traitLabels)} ${trait.numUnits}`)
                    .join(" · ")}
                </p>
              </Card>
            </Link>
          ))}
        </div>
      </section>
    </div>
  );
}
