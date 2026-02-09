"use client";

import Link from "next/link";
import { useEffect, useState } from "react";

import { Button } from "@/components/ui/Button";
import { Card } from "@/components/ui/Card";
import { Skeleton } from "@/components/ui/Skeleton";
import { encodeRiotIdPath, parseRiotIdInput } from "@/lib/riotid";

type FavoriteSummonerDto = {
  id: string;
  summonerPuuid: string;
  platformRegion: string;
  displayName?: string | null;
  createdAtUtc: string;
};

export default function FavoritesPage() {
  const [items, setItems] = useState<FavoriteSummonerDto[] | null>(null);
  const [error, setError] = useState<string | null>(null);

  async function load() {
    setError(null);
    const res = await fetch("/api/trn/user/users/me/favorites", {
      cache: "no-store"
    });

    if (res.status === 401) {
      setItems([]);
      setError("Login required to view favorites.");
      return;
    }

    if (!res.ok) {
      setItems([]);
      setError(`Failed to load favorites (${res.status}).`);
      return;
    }

    const json = (await res.json()) as FavoriteSummonerDto[];
    setItems(json);
  }

  useEffect(() => {
    void load();
  }, []);

  async function removeFavorite(id: string) {
    const res = await fetch(`/api/trn/user/users/me/favorites/${id}`, {
      method: "DELETE"
    });
    if (!res.ok) {
      setError(`Failed to remove favorite (${res.status}).`);
      return;
    }
    await load();
  }

  return (
    <div className="grid gap-6">
      <header className="grid gap-2">
        <h1 className="font-[var(--font-sora)] text-3xl font-semibold tracking-tight">
          Favorites
        </h1>
        <p className="text-sm text-fg/75">
          Your saved summoners. Add favorites from a summoner profile.
        </p>
      </header>

      {error ? (
        <Card className="p-5">
          <p className="text-sm text-red-300">{error}</p>
          <div className="mt-3 flex items-center gap-3">
            <Link
              className="text-sm text-primary hover:underline"
              href="/account/login"
            >
              Login
            </Link>
          </div>
        </Card>
      ) : null}

      {!items ? (
        <div className="grid gap-3">
          <Card className="p-5">
            <Skeleton className="h-5 w-40" />
            <Skeleton className="mt-3 h-12 w-full" />
          </Card>
        </div>
      ) : items.length === 0 ? (
        <Card className="p-5">
          <p className="text-sm text-muted">
            No favorites yet. Search a summoner and click{" "}
            <span className="font-medium text-fg">Add Favorite</span>.
          </p>
        </Card>
      ) : (
        <div className="grid gap-2">
          {items.map((f) => (
            <Card key={f.id} className="p-4">
              <div className="flex flex-wrap items-center justify-between gap-3">
                <div>
                  {f.displayName && parseRiotIdInput(f.displayName) ? (
                    <Link
                      className="text-sm font-semibold text-fg hover:underline"
                      href={`/summoners/${encodeURIComponent(
                        f.platformRegion
                      )}/${encodeRiotIdPath(parseRiotIdInput(f.displayName)!)}`}
                    >
                      {f.displayName}
                    </Link>
                  ) : (
                    <p className="text-sm font-semibold text-fg">
                      {f.displayName ?? f.summonerPuuid}
                    </p>
                  )}
                  <p className="text-xs text-muted">
                    {f.platformRegion} · Added{" "}
                    {new Date(f.createdAtUtc).toLocaleString()}
                  </p>
                </div>

                <div className="flex items-center gap-2">
                  <Button
                    variant="outline"
                    size="sm"
                    onClick={() => removeFavorite(f.id)}
                  >
                    Remove
                  </Button>
                </div>
              </div>
            </Card>
          ))}
        </div>
      )}
    </div>
  );
}
