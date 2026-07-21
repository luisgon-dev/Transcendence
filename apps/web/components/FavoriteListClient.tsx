"use client";

import type { components } from "@transcendence/api-client";
import Link from "next/link";
import { useMemo, useState } from "react";

import { Button } from "@/components/ui/Button";
import { Card } from "@/components/ui/Card";
import { EmptyState } from "@/components/ui/EmptyState";
import { platformRegionToSlug } from "@/lib/lolRegions";
import { encodeRiotIdPath, parseRiotIdInput } from "@/lib/riotid";

export type FavoriteSummonerDto = components["schemas"]["FavoriteSummonerDto"];

export function FavoriteListClient({
  initialItems,
  initialError,
  authenticated
}: {
  initialItems: FavoriteSummonerDto[];
  initialError: string | null;
  authenticated: boolean;
}) {
  const [items, setItems] = useState(initialItems);
  const [error, setError] = useState(initialError);
  const [removingId, setRemovingId] = useState<string | null>(null);
  const orderedItems = useMemo(
    () =>
      [...items].sort(
        (left, right) =>
          Number(Boolean(right.isLive)) - Number(Boolean(left.isLive)) ||
          Date.parse(right.createdAtUtc) - Date.parse(left.createdAtUtc)
      ),
    [items]
  );

  async function removeFavorite(id: string) {
    setRemovingId(id);
    setError(null);
    try {
      const response = await fetch(`/api/trn/user/users/me/favorites/${id}`, {
        method: "DELETE"
      });
      if (!response.ok) {
        const json = (await response.json().catch(() => null)) as
          | { message?: string; requestId?: string }
          | null;
        const message = json?.message ?? `Failed to remove favorite (${response.status}).`;
        const requestId = json?.requestId ? ` Request ID: ${json.requestId}` : "";
        setError(`${message}${requestId}`);
        return;
      }

      setItems((current) => current.filter((favorite) => favorite.id !== id));
    } catch (caught: unknown) {
      setError(caught instanceof Error ? caught.message : "Failed to remove favorite.");
    } finally {
      setRemovingId(null);
    }
  }

  if (error && items.length === 0) {
    return (
      <Card className="p-5">
        <p className="type-ui text-danger" role="alert">
          {error}
        </p>
        {!authenticated ? (
          <Link className="type-ui mt-3 inline-flex font-semibold text-primary hover:underline" href="/account/login">
            Sign in
          </Link>
        ) : null}
      </Card>
    );
  }

  if (items.length === 0) {
    return (
      <EmptyState
        title="No saved players yet"
        description="Open any League player profile and use Add Favorite to pin it here. Fresh worker observations will surface when a saved player enters a game."
        action={
          <Link href="/lol/leaderboards" className="type-ui font-semibold text-primary hover:underline">
            Browse players on the leaderboards
          </Link>
        }
      />
    );
  }

  return (
    <section aria-label="Saved players" className="grid gap-2">
      {error ? (
        <p className="rounded-control border border-danger/30 bg-danger/5 px-4 py-3 text-sm text-danger" role="alert">
          {error}
        </p>
      ) : null}

      {orderedItems.map((favorite) => {
        const riotId = favorite.displayName ? parseRiotIdInput(favorite.displayName) : null;
        const region = platformRegionToSlug(favorite.platformRegion);
        const profileHref = riotId
          ? `/lol/summoners/${region}/${encodeRiotIdPath(riotId)}`
          : null;
        const liveHref = riotId
          ? `/lol/live?region=${encodeURIComponent(region)}&riotId=${encodeURIComponent(`${riotId.gameName}#${riotId.tagLine}`)}`
          : null;

        return (
          <Card key={favorite.id} className="p-4">
            <div className="flex flex-wrap items-center justify-between gap-4">
              <div className="min-w-0">
                <div className="flex flex-wrap items-center gap-2">
                  {profileHref ? (
                    <Link className="type-ui font-semibold text-fg hover:underline" href={profileHref}>
                      {favorite.displayName}
                    </Link>
                  ) : (
                    <p className="type-ui truncate font-semibold text-fg">
                      {favorite.displayName ?? favorite.summonerPuuid}
                    </p>
                  )}
                  {favorite.isLive ? (
                    <span className="rounded-control border border-success/35 bg-success/10 px-2 py-0.5 text-xs font-semibold text-success">
                      Live now
                    </span>
                  ) : null}
                </div>
                <p className="type-ui type-tabular mt-1 text-muted">
                  {favorite.platformRegion} · Added {new Date(favorite.createdAtUtc).toLocaleDateString()}
                  {favorite.liveObservedAtUtc
                    ? ` · Checked ${new Date(favorite.liveObservedAtUtc).toLocaleTimeString([], { hour: "numeric", minute: "2-digit" })}`
                    : " · Awaiting first live check"}
                </p>
              </div>

              <div className="flex flex-wrap items-center gap-2">
                {favorite.isLive && liveHref ? (
                  <Link
                    href={liveHref}
                    className="inline-flex min-h-9 items-center rounded-control bg-primary px-3 text-sm font-semibold text-primary-fg transition hover:bg-primary/92 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40 focus-visible:ring-offset-2"
                  >
                    Scout live game
                  </Link>
                ) : null}
                {profileHref ? (
                  <Link
                    href={profileHref}
                    className="inline-flex min-h-9 items-center rounded-control border border-border px-3 text-sm font-semibold text-fg transition hover:border-border-strong hover:bg-surface-2/60 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/35"
                  >
                    View profile
                  </Link>
                ) : null}
                <Button
                  variant="outline"
                  size="sm"
                  disabled={removingId === favorite.id}
                  onClick={() => void removeFavorite(favorite.id)}
                >
                  {removingId === favorite.id ? "Removing…" : "Remove"}
                </Button>
              </div>
            </div>
          </Card>
        );
      })}
    </section>
  );
}
