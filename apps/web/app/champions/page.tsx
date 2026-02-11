import Image from "next/image";
import Link from "next/link";

import { Card } from "@/components/ui/Card";
import { fetchChampionMap, championIconUrl } from "@/lib/staticData";

export default async function ChampionsPage() {
  const { version, champions } = await fetchChampionMap();

  const list = Object.entries(champions)
    .map(([key, value]) => ({ championId: Number(key), ...value }))
    .sort((a, b) => a.name.localeCompare(b.name));

  return (
    <div className="grid gap-6">
      <header className="grid gap-2">
        <h1 className="font-[var(--font-sora)] text-3xl font-semibold tracking-tight">
          Champions
        </h1>
        <p className="text-sm text-fg/75">
          Builds, matchups, and win rates per role.
        </p>
      </header>

      <div className="grid grid-cols-2 gap-3 sm:grid-cols-3 md:grid-cols-4 lg:grid-cols-6">
        {list.map((c) => (
          <Link key={c.championId} href={`/champions/${c.championId}`}>
            <Card className="group p-3 transition hover:bg-white/10">
              <div className="flex items-center gap-3">
                <Image
                  src={championIconUrl(version, c.id)}
                  alt={c.name}
                  width={40}
                  height={40}
                  className="rounded-lg"
                />
                <div className="min-w-0">
                  <p className="truncate text-sm font-semibold text-fg group-hover:underline">
                    {c.name}
                  </p>
                </div>
              </div>
            </Card>
          </Link>
        ))}
      </div>
    </div>
  );
}
