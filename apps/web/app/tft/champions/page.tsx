import Link from "next/link";

import { Card } from "@/components/ui/Card";
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
      <section className="glass-card rounded-[2rem] p-6">
        <h1 className="font-[var(--font-sora)] text-3xl font-semibold tracking-tight">TFT Units</h1>
        <p className="mt-2 text-sm text-fg/75">Catalog data comes from the TFT static-data pipeline, not the LoL static-data tables.</p>
      </section>
      <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-3">
        {champions.map((champion) => (
          <Link key={champion.apiName} href={`/tft/champions/${champion.apiName}`}>
            <Card className="p-5 transition hover:bg-white/8">
              <p className="text-lg font-semibold text-fg">{champion.name}</p>
              <p className="mt-1 text-xs text-muted">{champion.apiName}</p>
            </Card>
          </Link>
        ))}
      </div>
    </div>
  );
}
