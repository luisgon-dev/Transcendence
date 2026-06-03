import { TftCatalogError } from "@/components/TftCatalogError";
import { TftCatalogGrid } from "@/components/TftCatalogGrid";
import { Toolbar } from "@/components/ui/Toolbar";
import { fetchBackendJson } from "@/lib/backendCall";
import { getBackendBaseUrl } from "@/lib/env";
import { type TftStaticEntity } from "@/lib/tft";

export default async function TftChampionsPage() {
  const result = await fetchBackendJson<TftStaticEntity[]>(`${getBackendBaseUrl()}/api/tft/analytics/champions`, {
    next: { revalidate: 60 * 60 }
  });

  if (!result.ok) {
    return <TftCatalogError title="TFT Units" noun="unit" result={result} />;
  }
  const champions = result.body ?? [];

  return (
    <div className="grid gap-4">
      <Toolbar
        eyebrow="TFT Catalog"
        title="Units"
        meta={<span>Every unit in the live set</span>}
      />
      <TftCatalogGrid items={champions} basePath="/tft/champions" />
    </div>
  );
}
