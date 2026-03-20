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
      <section className="page-hero p-6">
        <p className="type-kicker text-primary">TFT Catalog</p>
        <h1 className="type-title mt-3 sm:text-[2.4rem]">TFT Units</h1>
        <p className="type-ui mt-3 text-fg/75">Browse every unit in the live set and jump into quick detail pages.</p>
      </section>
      <TftCatalogGrid items={champions} basePath="/tft/champions" />
    </div>
  );
}
