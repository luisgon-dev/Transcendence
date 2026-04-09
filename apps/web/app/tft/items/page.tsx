import { TftItemsGrid } from "@/components/TftItemsGrid";
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
        <p className="type-ui mt-3 text-fg/75">
          Browse craftable completed items for the live set. Hover an item to inspect the two components it builds from.
        </p>
      </section>
      <TftItemsGrid items={items} />
    </div>
  );
}
