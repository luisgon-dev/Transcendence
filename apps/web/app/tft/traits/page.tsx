import { TftCatalogGrid } from "@/components/TftCatalogGrid";
import { fetchBackendJson } from "@/lib/backendCall";
import { getBackendBaseUrl } from "@/lib/env";
import { type TftStaticEntity } from "@/lib/tft";

export default async function TftTraitsPage() {
  const result = await fetchBackendJson<TftStaticEntity[]>(`${getBackendBaseUrl()}/api/tft/analytics/traits`, {
    next: { revalidate: 60 * 60 }
  });
  const traits = result.ok ? result.body ?? [] : [];

  return (
    <div className="grid gap-6">
      <section className="page-hero p-6">
        <p className="type-kicker text-muted">TFT Catalog</p>
        <h1 className="type-title mt-3 sm:text-[2.4rem]">TFT Traits</h1>
        <p className="type-ui mt-3 text-fg/75">Browse all traits in the live set and review their breakpoints.</p>
      </section>
      <TftCatalogGrid items={traits} basePath="/tft/traits" />
    </div>
  );
}
