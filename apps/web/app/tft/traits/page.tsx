import Link from "next/link";

import { Card } from "@/components/ui/Card";
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
      <section className="glass-card rounded-[2rem] p-6">
        <h1 className="font-[var(--font-sora)] text-3xl font-semibold tracking-tight">TFT Traits</h1>
      </section>
      <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-3">
        {traits.map((trait) => (
          <Link key={trait.apiName} href={`/tft/traits/${trait.apiName}`}>
            <Card className="p-5 transition hover:bg-white/8">
              <p className="text-lg font-semibold text-fg">{trait.name}</p>
              {trait.description ? <p className="mt-2 text-sm text-fg/75 line-clamp-3">{trait.description}</p> : null}
            </Card>
          </Link>
        ))}
      </div>
    </div>
  );
}
