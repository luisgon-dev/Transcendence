import { TftCatalogGrid } from "@/components/TftCatalogGrid";
import { fetchBackendJson } from "@/lib/backendCall";
import { getBackendBaseUrl } from "@/lib/env";
import { type TftStaticEntity } from "@/lib/tft";

export default async function TftChampionsPage() {
  const result = await fetchBackendJson<TftStaticEntity[]>(`${getBackendBaseUrl()}/api/tft/analytics/champions`, {
    next: { revalidate: 60 * 60 }
  });
  const champions = result.ok ? result.body ?? [] : [];

  return (
    <div className="grid gap-6">
      <section className="glass-card rounded-[2rem] p-6">
        <h1 className="font-[var(--font-sora)] text-3xl font-semibold tracking-tight">TFT Units</h1>
        <p className="mt-2 text-sm text-fg/75">Browse every unit in the live set and jump into quick detail pages.</p>
      </section>
      <TftCatalogGrid items={champions} basePath="/tft/champions" />
    </div>
  );
}
