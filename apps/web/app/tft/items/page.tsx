import { TftCatalogGrid } from "@/components/TftCatalogGrid";
import { fetchBackendJson } from "@/lib/backendCall";
import { getBackendBaseUrl } from "@/lib/env";
import { type TftStaticEntity } from "@/lib/tft";

export default async function TftItemsPage() {
  const result = await fetchBackendJson<TftStaticEntity[]>(`${getBackendBaseUrl()}/api/tft/analytics/items`, {
    next: { revalidate: 60 * 60 }
  });
  const items = result.ok ? result.body ?? [] : [];

  return (
    <div className="grid gap-6">
      <section className="page-hero p-6">
        <p className="type-kicker text-muted">TFT Catalog</p>
        <h1 className="type-page-title mt-3">TFT Items</h1>
        <p className="type-ui mt-3 text-fg/75">Browse completed items and quickly check what each one does.</p>
      </section>
      <TftCatalogGrid items={items} basePath="/tft/items" />
    </div>
  );
}
