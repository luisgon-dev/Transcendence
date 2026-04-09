import { AnalyticsSampleBanner } from "@/components/AnalyticsSampleBanner";
import { TftCompList } from "@/components/TftCompList";
import { type AnalyticsSampleLike } from "@/lib/analyticsSample";
import { fetchBackendJson } from "@/lib/backendCall";
import { getBackendBaseUrl } from "@/lib/env";
import {
  buildTftEntityMap,
  buildTftLabelMap,
  TFT_RANK_OPTIONS,
  TFT_REGION_OPTIONS,
  type TftCompListItem,
  type TftStaticEntity
} from "@/lib/tft";

export default async function TftCompsPage({
  searchParams
}: {
  searchParams?: Promise<{ rankTier?: string; region?: string }>;
}) {
  const resolved = searchParams ? await searchParams : undefined;
  const rankTier = resolved?.rankTier?.trim().toUpperCase() || "EMERALD_PLUS";
  const region = resolved?.region?.trim().toUpperCase() || "ALL";
  const requestedQs = new URLSearchParams();
  if (rankTier !== "EMERALD_PLUS") requestedQs.set("rankTier", rankTier);
  if (region !== "ALL") requestedQs.set("region", region);

  const [compsResult, championsResult, traitsResult] = await Promise.all([
    fetchBackendJson<TftCompListItem[]>(
      `${getBackendBaseUrl()}/api/tft/analytics/comps${requestedQs.toString() ? `?${requestedQs.toString()}` : ""}`,
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
  let comps = compsResult.ok ? compsResult.body ?? [] : [];
  if (comps.length === 0 && rankTier === "EMERALD_PLUS") {
    const fallback = await fetchBackendJson<TftCompListItem[]>(
      `${getBackendBaseUrl()}/api/tft/analytics/comps?rankTier=ALL${region !== "ALL" ? `&region=${encodeURIComponent(region)}` : ""}`,
      { next: { revalidate: 60 * 10 } }
    );
    if (fallback.ok && (fallback.body?.length ?? 0) > 0) {
      comps = fallback.body ?? [];
      effectiveRankTier = "ALL";
    }
  }

  const qs = new URLSearchParams();
  if (effectiveRankTier !== "EMERALD_PLUS") qs.set("rankTier", effectiveRankTier);
  if (region !== "ALL") qs.set("region", region);
  const unitLabels = championsResult.ok ? buildTftLabelMap(championsResult.body ?? []) : undefined;
  const unitEntities = championsResult.ok ? buildTftEntityMap(championsResult.body ?? []) : undefined;
  const traitLabels = traitsResult.ok ? buildTftLabelMap(traitsResult.body ?? []) : undefined;

  return (
    <div className="grid gap-6">
      <section className="page-hero p-6">
        <p className="type-kicker text-muted">TFT Analytics</p>
        <h1 className="type-page-title mt-3">TFT Meta Comps</h1>
        <p className="type-ui mt-3 text-fg/75">
          Compare the strongest boards for the live set and sort by the stat that matters most to you.
        </p>
        {effectiveRankTier !== rankTier ? (
          <p className="mt-3 text-sm text-warning">
            Local data does not have enough {rankTier.replace(/_/g, " ")} matches yet, so this view is falling back to all tracked ranks.
          </p>
        ) : null}
        <div className="mt-4">
          <AnalyticsSampleBanner sample={(comps[0] ?? null) as AnalyticsSampleLike} />
        </div>
        <form className="mt-4 flex flex-wrap gap-2" action="/tft/comps" method="get">
          <select name="rankTier" defaultValue={rankTier} className="control-select max-w-[220px]">
            {TFT_RANK_OPTIONS.map((option) => (
              <option key={option} value={option}>
                {option}
              </option>
            ))}
          </select>
          <select name="region" defaultValue={region} className="control-select max-w-[180px]">
            {TFT_REGION_OPTIONS.map((option) => (
              <option key={option} value={option}>
                {option}
              </option>
            ))}
          </select>
          <button type="submit" className="type-ui h-11 rounded-xl border border-primary/45 bg-primary/12 px-4 font-semibold text-primary">
            Apply
          </button>
        </form>
      </section>

      <TftCompList
        comps={comps}
        qs={qs.toString()}
        requestedRankTier={effectiveRankTier}
        requestedRegion={region}
        traitLabels={traitLabels}
        unitLabels={unitLabels}
        unitEntities={unitEntities}
      />
    </div>
  );
}
