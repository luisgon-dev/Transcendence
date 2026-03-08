import Link from "next/link";

import { Card } from "@/components/ui/Card";
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
      </section>
      <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-3">
        {items.map((item) => (
          <Link key={item.apiName} href={`/tft/items/${item.apiName}`}>
            <Card className="p-5 transition hover:bg-white/8">
              <p className="text-lg font-semibold text-fg">{item.name}</p>
              {item.description ? <p className="mt-2 text-sm text-fg/75 line-clamp-3">{item.description}</p> : null}
            </Card>
          </Link>
        ))}
      </div>
    </div>
  );
}
