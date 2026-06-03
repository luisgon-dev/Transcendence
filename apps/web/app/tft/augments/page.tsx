import { TftCatalogError } from "@/components/TftCatalogError";
import { TftCatalogGrid } from "@/components/TftCatalogGrid";
import { Toolbar } from "@/components/ui/Toolbar";
import { fetchBackendJson } from "@/lib/backendCall";
import { getBackendBaseUrl } from "@/lib/env";
import { type TftStaticEntity } from "@/lib/tft";

export default async function TftAugmentsPage() {
  const result = await fetchBackendJson<TftStaticEntity[]>(`${getBackendBaseUrl()}/api/tft/analytics/augments`, {
    next: { revalidate: 60 * 60 }
  });

  if (!result.ok) {
    return <TftCatalogError title="TFT Augments" noun="augment" result={result} />;
  }
  const augments = result.body ?? [];

  return (
    <div className="grid gap-4">
      <Toolbar
        eyebrow="TFT Catalog"
        title="Augments"
        meta={<span>The full augment pool, with details for any option</span>}
      />
      <TftCatalogGrid items={augments} basePath="/tft/augments" />
    </div>
  );
}
