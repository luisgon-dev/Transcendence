"use client";

import { Command } from "cmdk";
import Image from "next/image";
import { useRouter } from "next/navigation";
import { useEffect, useMemo, useRef, useState } from "react";

import { GLOBAL_SEARCH_OPEN_EVENT } from "@/lib/globalSearch";
import { DEFAULT_TIERLIST_RANK_TIER, rankTierDisplayLabel } from "@/lib/ranks";
import { encodeRiotIdPath, parseRiotIdInput } from "@/lib/riotid";

type ChampionSearchItem = {
  championId: number;
  name: string;
};

type ChampionsResponse = {
  version: string;
  champions: Record<string, { id: string; name: string }>;
};

type SummonerSearchItem = {
  platformRegion: string;
  region: string;
  gameName: string;
  tagLine: string;
  profileIconId: number;
};

type SummonerSearchResponse = {
  items: SummonerSearchItem[];
};

const REGIONS = [
  { value: "na", label: "NA" },
  { value: "euw", label: "EUW" },
  { value: "eune", label: "EUNE" },
  { value: "kr", label: "KR" },
  { value: "br", label: "BR" },
  { value: "lan", label: "LAN" },
  { value: "las", label: "LAS" },
  { value: "oce", label: "OCE" },
  { value: "jp", label: "JP" },
  { value: "tr", label: "TR" },
  { value: "ru", label: "RU" }
] as const;

const TIER_LINKS = [
  {
    label: `Tier List · All Roles (${rankTierDisplayLabel(DEFAULT_TIERLIST_RANK_TIER)})`,
    href: "/tierlist"
  },
  {
    label: `Tier List · Top (${rankTierDisplayLabel(DEFAULT_TIERLIST_RANK_TIER)})`,
    href: "/tierlist?role=TOP"
  },
  {
    label: `Tier List · Jungle (${rankTierDisplayLabel(DEFAULT_TIERLIST_RANK_TIER)})`,
    href: "/tierlist?role=JUNGLE"
  },
  {
    label: `Tier List · Middle (${rankTierDisplayLabel(DEFAULT_TIERLIST_RANK_TIER)})`,
    href: "/tierlist?role=MIDDLE"
  },
  {
    label: `Tier List · Bottom (${rankTierDisplayLabel(DEFAULT_TIERLIST_RANK_TIER)})`,
    href: "/tierlist?role=BOTTOM"
  },
  {
    label: `Tier List · Support (${rankTierDisplayLabel(DEFAULT_TIERLIST_RANK_TIER)})`,
    href: "/tierlist?role=UTILITY"
  },
  { label: "Tier List · All Ranks", href: "/tierlist?rankTier=all" },
  { label: "Tier List · Challenger", href: "/tierlist?rankTier=CHALLENGER" },
  { label: "Matchup Analysis", href: "/matchups" },
  { label: "Pro Builds Preview", href: "/pro-builds" }
] as const;

function isEditableTarget(target: EventTarget | null) {
  if (!(target instanceof HTMLElement)) return false;
  if (target.isContentEditable) return true;
  const tag = target.tagName.toLowerCase();
  return tag === "input" || tag === "textarea" || tag === "select";
}

function useDebouncedValue<T>(value: T, delayMs: number) {
  const [debounced, setDebounced] = useState(value);

  useEffect(() => {
    const timeoutId = window.setTimeout(() => {
      setDebounced(value);
    }, delayMs);
    return () => window.clearTimeout(timeoutId);
  }, [value, delayMs]);

  return debounced;
}

function profileIconSrc(version: string, profileIconId: number) {
  return `https://ddragon.leagueoflegends.com/cdn/${version}/img/profileicon/${profileIconId}.png`;
}

export function GlobalCommandPalette() {
  const router = useRouter();
  const inputRef = useRef<HTMLInputElement | null>(null);
  const suggestionCacheRef = useRef<Map<string, SummonerSearchItem[]>>(new Map());
  const [open, setOpen] = useState(false);
  const [query, setQuery] = useState("");
  const [region, setRegion] = useState("na");
  const [champions, setChampions] = useState<ChampionSearchItem[]>([]);
  const [ddragonVersion, setDdragonVersion] = useState<string | null>(null);
  const [championsLoaded, setChampionsLoaded] = useState(false);
  const [summonerResults, setSummonerResults] = useState<SummonerSearchItem[]>([]);
  const [summonerLoading, setSummonerLoading] = useState(false);

  const debouncedQuery = useDebouncedValue(query, 120);
  const normalizedQuery = debouncedQuery.trim().toLowerCase();
  const parsedRiotId = parseRiotIdInput(query.trim());

  useEffect(() => {
    function onKeyDown(e: KeyboardEvent) {
      if ((e.metaKey || e.ctrlKey) && e.key.toLowerCase() === "k" && !e.altKey && !e.shiftKey) {
        if (isEditableTarget(e.target)) return;
        e.preventDefault();
        setOpen(true);
      }
      if (open && e.key === "Escape") {
        e.preventDefault();
        setOpen(false);
      }
    }

    function onOpenEvent() {
      setOpen(true);
    }

    window.addEventListener("keydown", onKeyDown);
    window.addEventListener(GLOBAL_SEARCH_OPEN_EVENT, onOpenEvent);
    return () => {
      window.removeEventListener("keydown", onKeyDown);
      window.removeEventListener(GLOBAL_SEARCH_OPEN_EVENT, onOpenEvent);
    };
  }, [open]);

  useEffect(() => {
    if (!open) return;
    const rafId = window.requestAnimationFrame(() => {
      inputRef.current?.focus();
    });
    return () => window.cancelAnimationFrame(rafId);
  }, [open]);

  useEffect(() => {
    if (!open || championsLoaded) return;

    let active = true;
    void fetch("/api/static/champions", { cache: "force-cache" })
      .then(async (res) => {
        if (!res.ok) throw new Error("Failed to load champions.");
        const json = (await res.json()) as ChampionsResponse;
        const parsed = Object.entries(json.champions)
          .map(([championId, data]) => ({
            championId: Number(championId),
            name: data.name
          }))
          .filter((item) => Number.isFinite(item.championId))
          .sort((a, b) => a.name.localeCompare(b.name));

        if (!active) return;
        setDdragonVersion(json.version);
        setChampions(parsed);
        setChampionsLoaded(true);
      })
      .catch(() => {
        if (!active) return;
        setChampionsLoaded(true);
      });

    return () => {
      active = false;
    };
  }, [open, championsLoaded]);

  useEffect(() => {
    if (!open) return;

    const trimmedQuery = debouncedQuery.trim();
    if (trimmedQuery.length < 2) {
      setSummonerResults([]);
      setSummonerLoading(false);
      return;
    }

    const cacheKey = `${region}|${trimmedQuery.toLowerCase()}`;
    const cached = suggestionCacheRef.current.get(cacheKey);
    if (cached) {
      setSummonerResults(cached);
      setSummonerLoading(false);
      return;
    }

    const abortController = new AbortController();
    setSummonerLoading(true);

    void fetch(
      `/api/trn/public/summoners/search?region=${encodeURIComponent(region)}&q=${encodeURIComponent(trimmedQuery)}&limit=8`,
      { cache: "no-store", signal: abortController.signal }
    )
      .then(async (res) => {
        if (!res.ok) throw new Error("Summoner search failed.");
        const json = (await res.json()) as SummonerSearchResponse;
        if (abortController.signal.aborted) return;
        const items = Array.isArray(json.items) ? json.items : [];
        setSummonerResults(items);

        suggestionCacheRef.current.set(cacheKey, items);
        if (suggestionCacheRef.current.size > 60) {
          const oldest = suggestionCacheRef.current.keys().next().value as string | undefined;
          if (oldest) suggestionCacheRef.current.delete(oldest);
        }
      })
      .catch(() => {
        if (abortController.signal.aborted) return;
        setSummonerResults([]);
      })
      .finally(() => {
        if (abortController.signal.aborted) return;
        setSummonerLoading(false);
      });

    return () => {
      abortController.abort();
    };
  }, [debouncedQuery, open, region]);

  const championResults = useMemo(() => {
    if (!normalizedQuery) return champions.slice(0, 8);
    return champions
      .filter((champion) => champion.name.toLowerCase().includes(normalizedQuery))
      .sort((a, b) => {
        const aStarts = a.name.toLowerCase().startsWith(normalizedQuery) ? 0 : 1;
        const bStarts = b.name.toLowerCase().startsWith(normalizedQuery) ? 0 : 1;
        return aStarts - bStarts || a.name.localeCompare(b.name);
      })
      .slice(0, 10);
  }, [champions, normalizedQuery]);

  const tierResults = useMemo(() => {
    if (!normalizedQuery) return TIER_LINKS;
    return TIER_LINKS.filter((item) =>
      item.label.toLowerCase().includes(normalizedQuery)
    );
  }, [normalizedQuery]);

  const summonerResultPaths = useMemo(
    () =>
      summonerResults.map((item) =>
        `/summoners/${item.region}/${encodeRiotIdPath({
          gameName: item.gameName,
          tagLine: item.tagLine
        })}`
      ),
    [summonerResults]
  );

  const prefetchTargets = useMemo(() => {
    const paths = summonerResultPaths.slice(0, 3);
    if (paths.length === 0 && parsedRiotId) {
      paths.push(`/summoners/${region}/${encodeRiotIdPath(parsedRiotId)}`);
    }

    for (const championPath of championResults.slice(0, 3).map((c) => `/champions/${c.championId}`)) {
      paths.push(championPath);
    }
    for (const tier of tierResults.slice(0, 2)) paths.push(tier.href);
    return paths;
  }, [championResults, tierResults, parsedRiotId, region, summonerResultPaths]);

  useEffect(() => {
    if (!open) return;
    for (const path of prefetchTargets) {
      router.prefetch(path);
    }
  }, [open, prefetchTargets, router]);

  function navigate(path: string) {
    setOpen(false);
    setQuery("");
    router.push(path);
  }

  function handleQueryKeyDown(e: React.KeyboardEvent<HTMLInputElement>) {
    if (e.key !== "Enter") return;
    if (summonerResultPaths.length > 0) return;
    if (!parsedRiotId) return;

    e.preventDefault();
    navigate(`/summoners/${region}/${encodeRiotIdPath(parsedRiotId)}`);
  }

  if (!open) return null;

  const showEmpty =
    championResults.length === 0 &&
    tierResults.length === 0 &&
    summonerResults.length === 0 &&
    !parsedRiotId;

  return (
    <div className="fixed inset-0 z-50">
      <button
        type="button"
        className="absolute inset-0 bg-black/60 backdrop-blur-[1px]"
        aria-label="Close search"
        onClick={() => setOpen(false)}
      />

      <div className="absolute left-1/2 top-[10vh] w-[min(760px,calc(100vw-24px))] -translate-x-1/2 overflow-hidden rounded-xl border border-border/70 bg-surface/95 shadow-glass">
        <Command shouldFilter={false} className="w-full">
          <div className="flex items-center gap-2 border-b border-border/60 p-3">
            <input
              ref={inputRef}
              value={query}
              onChange={(e) => setQuery(e.target.value)}
              onKeyDown={handleQueryKeyDown}
              placeholder="Search champions, tier list, or summoner"
              className="h-11 w-full rounded-md border border-border/70 bg-surface/35 px-3 text-sm text-fg shadow-glass outline-none placeholder:text-muted/70 focus:border-primary/70 focus:ring-2 focus:ring-primary/25"
              aria-label="Global search input"
            />
            <select
              className="h-11 min-w-[92px] rounded-md border border-border/70 bg-surface/35 px-3 text-sm text-fg shadow-glass outline-none focus:border-primary/70 focus:ring-2 focus:ring-primary/25"
              value={region}
              onChange={(e) => setRegion(e.target.value)}
              aria-label="Summoner region"
            >
              {REGIONS.map((item) => (
                <option key={item.value} value={item.value}>
                  {item.label}
                </option>
              ))}
            </select>
          </div>

          <Command.List className="max-h-[65vh] overflow-y-auto p-2">
            <Command.Group heading="Summoners">
              {summonerResults.map((item) => {
                const path = `/summoners/${item.region}/${encodeRiotIdPath({
                  gameName: item.gameName,
                  tagLine: item.tagLine
                })}`;

                return (
                  <Command.Item
                    key={`summoner-${item.platformRegion}-${item.gameName}-${item.tagLine}`}
                    value={`summoner-${item.gameName}-${item.tagLine}-${item.platformRegion}`}
                    onSelect={() => navigate(path)}
                    className="flex cursor-pointer items-center gap-3 rounded-md px-3 py-2 text-sm text-fg/90 data-[selected=true]:bg-white/10"
                  >
                    {ddragonVersion ? (
                      <Image
                        src={profileIconSrc(ddragonVersion, item.profileIconId)}
                        alt=""
                        width={28}
                        height={28}
                        className="h-7 w-7 rounded-md border border-border/60 object-cover"
                        unoptimized
                      />
                    ) : (
                      <span className="inline-flex h-7 w-7 items-center justify-center rounded-md border border-border/60 bg-surface/60 text-[11px] text-muted">
                        ?
                      </span>
                    )}
                    <span className="font-medium">
                      {item.gameName}#{item.tagLine}
                    </span>
                    <span className="ml-auto text-xs uppercase tracking-wide text-muted">
                      {item.platformRegion}
                    </span>
                  </Command.Item>
                );
              })}

              {summonerLoading ? (
                <p className="px-3 py-2 text-sm text-muted">Searching summoners...</p>
              ) : null}

              {!summonerLoading &&
              summonerResults.length === 0 &&
              parsedRiotId ? (
                <Command.Item
                  key="summoner-open"
                  value={`summoner-${parsedRiotId.gameName}-${parsedRiotId.tagLine}-${region}`}
                  onSelect={() =>
                    navigate(
                      `/summoners/${region}/${encodeRiotIdPath(parsedRiotId)}`
                    )
                  }
                  className="flex cursor-pointer items-center rounded-md px-3 py-2 text-sm text-fg/90 data-[selected=true]:bg-white/10"
                >
                  Open {parsedRiotId.gameName}#{parsedRiotId.tagLine} ({region.toUpperCase()})
                </Command.Item>
              ) : null}

              {!summonerLoading &&
              summonerResults.length === 0 &&
              !parsedRiotId &&
              query.trim().length > 0 ? (
                <p className="px-3 py-2 text-sm text-muted">
                  No summoners found. Enter GameName#TAG to open directly.
                </p>
              ) : null}
            </Command.Group>

            <Command.Group heading="Champions">
              {championResults.map((champion) => (
                <Command.Item
                  key={`champion-${champion.championId}`}
                  value={`champion-${champion.name}`}
                  onSelect={() => navigate(`/champions/${champion.championId}`)}
                  className="flex cursor-pointer items-center rounded-md px-3 py-2 text-sm text-fg/90 data-[selected=true]:bg-white/10"
                >
                  {champion.name}
                </Command.Item>
              ))}
            </Command.Group>

            <Command.Group heading="Tier List">
              {tierResults.map((item) => (
                <Command.Item
                  key={item.href}
                  value={`tier-${item.label}`}
                  onSelect={() => navigate(item.href)}
                  className="flex cursor-pointer items-center rounded-md px-3 py-2 text-sm text-fg/90 data-[selected=true]:bg-white/10"
                >
                  {item.label}
                </Command.Item>
              ))}
            </Command.Group>

            {showEmpty ? (
              <Command.Empty className="px-3 py-2 text-sm text-muted">
                No results.
              </Command.Empty>
            ) : null}
          </Command.List>
        </Command>
      </div>
    </div>
  );
}
