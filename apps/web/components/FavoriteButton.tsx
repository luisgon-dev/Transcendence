"use client";

import Link from "next/link";
import { useEffect, useState } from "react";

import { Button } from "@/components/ui/Button";

type FavoriteSummonerDto = {
  id: string;
  summonerPuuid: string;
  platformRegion: string;
  displayName?: string | null;
  createdAtUtc: string;
};

async function readError(res: Response, fallback: string): Promise<string> {
  const json = (await res.json().catch(() => null)) as
    | { message?: string; requestId?: string }
    | null;
  const message = json?.message ?? `${fallback} (${res.status}).`;
  const rid = json?.requestId ? ` (Request ID: ${json.requestId})` : "";
  return `${message}${rid}`;
}

export function FavoriteButton({
  region,
  gameName,
  tagLine
}: {
  region: string;
  gameName: string;
  tagLine: string;
}) {
  const [favoriteId, setFavoriteId] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [needsLogin, setNeedsLogin] = useState(false);
  const [msg, setMsg] = useState<string | null>(null);

  // Seed the toggle from current state so it opens as "Added" when saved.
  useEffect(() => {
    let active = true;
    const target = `${gameName}#${tagLine}`;
    void (async () => {
      try {
        const res = await fetch("/api/trn/user/users/me/favorites", {
          cache: "no-store"
        });
        if (!active || !res.ok) return; // 401/unauth stays silent until action
        const items = (await res.json()) as FavoriteSummonerDto[];
        const match = items.find(
          (f) =>
            f.displayName === target &&
            f.platformRegion.toLowerCase() === region.toLowerCase()
        );
        if (active && match) setFavoriteId(match.id);
      } catch {
        // ignore — button falls back to "Add Favorite"
      }
    })();
    return () => {
      active = false;
    };
  }, [region, gameName, tagLine]);

  async function toggle() {
    if (busy) return;
    setBusy(true);
    setMsg(null);
    setNeedsLogin(false);
    try {
      if (favoriteId) {
        const res = await fetch(
          `/api/trn/user/users/me/favorites/${favoriteId}`,
          { method: "DELETE" }
        );
        if (res.status === 401) {
          setNeedsLogin(true);
          return;
        }
        if (!res.ok && res.status !== 404) {
          setMsg(await readError(res, "Failed to remove favorite"));
          return;
        }
        setFavoriteId(null);
        setMsg("Removed from favorites.");
        return;
      }

      const res = await fetch("/api/trn/user/users/me/favorites", {
        method: "POST",
        headers: { "content-type": "application/json" },
        body: JSON.stringify({ region, gameName, tagLine })
      });
      if (res.status === 401) {
        setNeedsLogin(true);
        return;
      }
      if (!res.ok) {
        setMsg(await readError(res, "Failed to add favorite"));
        return;
      }
      const created = (await res.json()) as FavoriteSummonerDto;
      setFavoriteId(created.id); // prevents duplicate POSTs
      setMsg("Saved to favorites.");
    } catch (e) {
      setMsg(e instanceof Error ? e.message : "Something went wrong.");
    } finally {
      setBusy(false);
    }
  }

  const added = favoriteId !== null;
  const label = busy ? (added ? "Removing…" : "Saving…") : added ? "Added ✓" : "Add Favorite";

  return (
    <div className="flex items-center gap-3">
      <Button
        variant="outline"
        size="sm"
        onClick={toggle}
        disabled={busy}
        aria-pressed={added}
        title={added ? "Remove from favorites" : "Add to favorites"}
      >
        {label}
      </Button>
      {needsLogin ? (
        <span className="text-xs text-muted">
          <Link
            className="font-semibold text-primary hover:underline"
            href="/account/login"
          >
            Sign in
          </Link>{" "}
          to save favorites.
        </span>
      ) : msg ? (
        <span className="text-xs text-muted">{msg}</span>
      ) : null}
    </div>
  );
}

