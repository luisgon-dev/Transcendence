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
      <section className="glass-card rounded-[2rem] p-6">
        <h1 className="font-[var(--font-sora)] text-3xl font-semibold tracking-tight">TFT Items</h1>
        <p className="mt-2 text-sm text-fg/75">Browse completed items and quickly check what each one does.</p>
      </section>
      <TftCatalogGrid items={items} basePath="/tft/items" />
    </div>
  );
}
