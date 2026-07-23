import type { components } from "@transcendence/api-client";

import { FavoriteListClient } from "@/components/FavoriteListClient";
import { RiotAccountPanel } from "@/components/RiotAccountPanel";
import { Toolbar } from "@/components/ui/Toolbar";
import { getAccessTokenOrRefresh } from "@/lib/sessionToken";
import { getTrnClient } from "@/lib/trnClient";

type FavoriteSummonerDto = components["schemas"]["FavoriteSummonerDto"];

type FavoritesLoadResult = {
  authenticated: boolean;
  items: FavoriteSummonerDto[];
  error: string | null;
};

async function loadFavorites(): Promise<FavoritesLoadResult> {
  const token = await getAccessTokenOrRefresh();
  if (!token.ok) {
    return {
      authenticated: false,
      items: [],
      error:
        token.reason === "unavailable"
          ? "Account services are temporarily unavailable. Try again shortly."
          : "Sign in to view saved players."
    };
  }

  try {
    const { data, error, response } = await getTrnClient().GET("/api/users/me/favorites", {
      headers: { authorization: `Bearer ${token.accessToken}` },
      cache: "no-store"
    });

    if (!data) {
      return {
        authenticated: response.status !== 401,
        items: [],
        error:
          (error as { detail?: string; title?: string } | undefined)?.detail ??
          (error as { detail?: string; title?: string } | undefined)?.title ??
          "We couldn't load favorites right now."
      };
    }

    return { authenticated: true, items: data as FavoriteSummonerDto[], error: null };
  } catch {
    return {
      authenticated: true,
      items: [],
      error: "We couldn't reach favorites right now. Try again shortly."
    };
  }
}

export default async function FavoritesPage() {
  const result = await loadFavorites();

  return (
    <div className="grid gap-4">
      <Toolbar
        eyebrow="Account"
        title="Favorites"
        meta={<span>Saved players, with fresh live-game signals</span>}
      />

      {result.authenticated ? <RiotAccountPanel /> : null}

      <FavoriteListClient
        initialItems={result.items}
        initialError={result.error}
        authenticated={result.authenticated}
      />
    </div>
  );
}
