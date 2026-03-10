import { TftCatalogGrid } from "@/components/TftCatalogGrid";
import { fetchBackendJson } from "@/lib/backendCall";
import { getBackendBaseUrl } from "@/lib/env";
import { type TftStaticEntity } from "@/lib/tft";

export default async function TftAugmentsPage() {
  const result = await fetchBackendJson<TftStaticEntity[]>(`${getBackendBaseUrl()}/api/tft/analytics/augments`, {
    next: { revalidate: 60 * 60 }
  });
  const augments = result.ok ? result.body ?? [] : [];

  return (
    <div className="grid gap-6">
      <section className="glass-card rounded-[2rem] p-6">
        <h1 className="font-[var(--font-sora)] text-3xl font-semibold tracking-tight">TFT Augments</h1>
        <p className="mt-2 text-sm text-fg/75">Browse the full augment pool and open details for any option you are considering.</p>
      </section>
      <TftCatalogGrid items={augments} basePath="/tft/augments" />
    </div>
  );
}
