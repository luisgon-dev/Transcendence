import { TftCatalogError } from "@/components/TftCatalogError";
import { TftCatalogGrid } from "@/components/TftCatalogGrid";
import { Toolbar } from "@/components/ui/Toolbar";
import { fetchBackendJson } from "@/lib/backendCall";
import { getBackendBaseUrl } from "@/lib/env";
import { type TftStaticEntity } from "@/lib/tft";

export default async function TftTraitsPage() {
  const result = await fetchBackendJson<TftStaticEntity[]>(`${getBackendBaseUrl()}/api/tft/analytics/traits`, {
    next: { revalidate: 60 * 60 }
  });

  if (!result.ok) {
    return <TftCatalogError title="TFT Traits" noun="trait" result={result} />;
  }
  const traits = result.body ?? [];

  return (
    <div className="grid gap-4">
      <Toolbar
        eyebrow="TFT Catalog"
        title="Traits"
        meta={<span>All traits in the live set and their breakpoints</span>}
      />
      <TftCatalogGrid items={traits} basePath="/tft/traits" />
    </div>
  );
}
