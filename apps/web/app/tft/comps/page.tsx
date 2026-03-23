import { TftCompList } from "@/components/TftCompList";
import { fetchBackendJson } from "@/lib/backendCall";
import { getBackendBaseUrl } from "@/lib/env";
import { TFT_RANK_OPTIONS, TFT_REGION_OPTIONS, type TftCompListItem } from "@/lib/tft";

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
      <section className="page-hero p-6">
        <p className="type-kicker text-muted">TFT Analytics</p>
        <h1 className="type-title mt-3 sm:text-[2.4rem]">TFT Meta Comps</h1>
        <p className="type-ui mt-3 text-fg/75">
          Compare the strongest boards for the live set and sort by the stat that matters most to you.
        </p>
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
          <button type="submit" className="h-11 rounded-xl border border-primary/45 bg-primary/12 px-4 text-[0.9375rem] font-semibold text-primary">
            Apply
          </button>
        </form>
      </section>

      <TftCompList comps={comps} qs={qs.toString()} />
    </div>
  );
}
