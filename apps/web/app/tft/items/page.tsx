import { TftCatalogError } from "@/components/TftCatalogError";
import { TftCatalogGrid } from "@/components/TftCatalogGrid";
import { Toolbar } from "@/components/ui/Toolbar";
import { fetchBackendJson } from "@/lib/backendCall";
import { getBackendBaseUrl } from "@/lib/env";
import { type TftStaticEntity } from "@/lib/tft";

export default async function TftItemsPage() {
  const result = await fetchBackendJson<TftStaticEntity[]>(`${getBackendBaseUrl()}/api/tft/analytics/items`, {
    next: { revalidate: 60 * 60 }
  });

  if (!result.ok) {
    return <TftCatalogError title="TFT Items" noun="item" result={result} />;
  }
  const items = result.body ?? [];

  return (
    <div className="grid gap-4">
      <Toolbar
        eyebrow="TFT Catalog"
        title="Items"
        meta={<span>Completed items and what each one does</span>}
      />
      <TftCatalogGrid items={items} basePath="/tft/items" />
    </div>
  );
}
