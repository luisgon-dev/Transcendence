import Link from "next/link";

import { Card } from "@/components/ui/Card";
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
      </section>
      <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-3">
        {augments.map((augment) => (
          <Link key={augment.apiName} href={`/tft/augments/${augment.apiName}`}>
            <Card className="p-5 transition hover:bg-white/8">
              <p className="text-lg font-semibold text-fg">{augment.name}</p>
              {augment.description ? <p className="mt-2 text-sm text-fg/75 line-clamp-3">{augment.description}</p> : null}
            </Card>
          </Link>
        ))}
      </div>
    </div>
  );
}
