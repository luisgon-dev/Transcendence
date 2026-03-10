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
      <section className="glass-card rounded-[2rem] p-6">
        <h1 className="font-[var(--font-sora)] text-3xl font-semibold tracking-tight">TFT Meta Comps</h1>
        <p className="mt-2 text-sm text-fg/75">
          Compare the strongest boards for the live set and sort by the stat that matters most to you.
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

      <TftCompList comps={comps} qs={qs.toString()} />
    </div>
  );
}
