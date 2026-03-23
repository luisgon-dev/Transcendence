"use client";

import { Command } from "cmdk";
import Image from "next/image";
import { useRouter } from "next/navigation";
import { useEffect, useMemo, useRef, useState } from "react";

import { ArrowCornerIcon, SearchIcon, SparkIcon } from "@/components/ui/icons";
import { GLOBAL_SEARCH_OPEN_EVENT } from "@/lib/globalSearch";
import { TFT_FRONTEND_ENABLED } from "@/lib/featureFlags";
import { buildLolPublicSummonerSearchPath } from "@/lib/lolPublicApi";
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

const LOL_TIER_LINKS = [
  {
    label: `Tier List · All Roles (${rankTierDisplayLabel(DEFAULT_TIERLIST_RANK_TIER)})`,
    href: "/lol/tierlist"
  },
  {
    label: `Tier List · Top (${rankTierDisplayLabel(DEFAULT_TIERLIST_RANK_TIER)})`,
    href: "/lol/tierlist?role=TOP"
  },
  {
    label: `Tier List · Jungle (${rankTierDisplayLabel(DEFAULT_TIERLIST_RANK_TIER)})`,
    href: "/lol/tierlist?role=JUNGLE"
  },
  {
    label: `Tier List · Middle (${rankTierDisplayLabel(DEFAULT_TIERLIST_RANK_TIER)})`,
    href: "/lol/tierlist?role=MIDDLE"
  },
  {
    label: `Tier List · Bottom (${rankTierDisplayLabel(DEFAULT_TIERLIST_RANK_TIER)})`,
    href: "/lol/tierlist?role=BOTTOM"
  },
  {
    label: `Tier List · Support (${rankTierDisplayLabel(DEFAULT_TIERLIST_RANK_TIER)})`,
    href: "/lol/tierlist?role=UTILITY"
  },
  { label: "Tier List · All Ranks", href: "/lol/tierlist?rankTier=all" },
  { label: "Tier List · Challenger", href: "/lol/tierlist?rankTier=CHALLENGER" },
  { label: "Matchup Analysis", href: "/lol/matchups" },
  { label: "Pro Builds", href: "/lol/pro-builds" }
] as const;

const TFT_TIER_LINKS = [
  { label: "TFT Comps", href: "/tft/comps" },
  { label: "TFT Champions", href: "/tft/champions" },
  { label: "TFT Items", href: "/tft/items" },
  { label: "TFT Traits", href: "/tft/traits" },
  { label: "TFT Augments", href: "/tft/augments" }
] as const;

const TIER_LINKS = TFT_FRONTEND_ENABLED
  ? [...LOL_TIER_LINKS, ...TFT_TIER_LINKS]
  : LOL_TIER_LINKS;

const RESULT_ITEM_CLASS =
  "group flex min-h-[52px] cursor-pointer items-center gap-3 rounded-card border border-transparent px-3 py-2.5 text-left text-fg/90 transition duration-150 data-[selected=true]:translate-x-1 data-[selected=true]:border-primary/25 data-[selected=true]:bg-primary/10 data-[selected=true]:shadow-card";

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

function splitQuickLinkLabel(label: string) {
  const [title, detail] = label.split("·").map((part) => part.trim());
  return {
    title: title || label,
    detail: detail || null
  };
}

function SearchSection({
  title,
  countLabel,
  children,
  className
}: {
  title: string;
  countLabel: string;
  children: React.ReactNode;
  className?: string;
}) {
  return (
    <section
      className={`surface-subtle rounded-card p-2.5 sm:p-3 ${className ?? ""}`}
    >
      <div className="mb-2 flex items-center justify-between gap-3 px-1">
        <p className="type-kicker text-primary/88">{title}</p>
        <p className="type-ui type-tabular text-fg/65">{countLabel}</p>
      </div>
      <div className="grid gap-1">{children}</div>
    </section>
  );
}

function SearchHint({
  children,
  tone = "default"
}: {
  children: React.ReactNode;
  tone?: "default" | "accent";
}) {
  return (
    <div
      className={`rounded-xl border px-3 py-3 text-sm leading-6 ${
        tone === "accent"
          ? "border-primary/24 bg-primary/11 text-fg/92"
          : "surface-subtle text-fg/72"
      }`}
    >
      {children}
    </div>
  );
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

    void fetch(buildLolPublicSummonerSearchPath(region, trimmedQuery, 8), {
      cache: "no-store",
      signal: abortController.signal
    })
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
        `/lol/summoners/${item.region}/${encodeRiotIdPath({
          gameName: item.gameName,
          tagLine: item.tagLine
        })}`
      ),
    [summonerResults]
  );

  const prefetchTargets = useMemo(() => {
    const paths = summonerResultPaths.slice(0, 3);
    if (paths.length === 0 && parsedRiotId) {
      paths.push(`/lol/summoners/${region}/${encodeRiotIdPath(parsedRiotId)}`);
    }

    for (const championPath of championResults
      .slice(0, 3)
      .map((c) => `/lol/champions/${c.championId}`)) {
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
    if (!parsedRiotId) return;

    e.preventDefault();
    e.stopPropagation();
    navigate(`/lol/summoners/${region}/${encodeRiotIdPath(parsedRiotId)}`);
  }

  if (!open) return null;

  const regionLabel = REGIONS.find((item) => item.value === region)?.label ?? region.toUpperCase();
  const directOpenPath = parsedRiotId
    ? `/lol/summoners/${region}/${encodeRiotIdPath(parsedRiotId)}`
    : null;
  const showEmpty =
    championResults.length === 0 &&
    tierResults.length === 0 &&
    summonerResults.length === 0 &&
    !parsedRiotId;

  return (
    <div className="command-palette-overlay fixed inset-0 z-50">
      <button
        type="button"
        className="absolute inset-0 bg-black/82 backdrop-blur-md"
        aria-label="Close search"
        onClick={() => setOpen(false)}
      />

      <div className="command-palette-panel absolute left-1/2 top-[9vh] w-[min(880px,calc(100vw-24px))] -translate-x-1/2 overflow-hidden rounded-panel border border-border/70 bg-surface shadow-overlay">
        <Command shouldFilter={false} className="relative w-full">
          <div className="border-b border-border/50 px-4 pb-4 pt-4 sm:px-5 sm:pb-5 sm:pt-5">
            <div className="grid gap-3 lg:grid-cols-[minmax(0,1fr)_auto] lg:items-start">
              <div className="grid max-w-[34rem] gap-1">
                <p className="type-kicker text-primary">Global Search</p>
                <p className="type-ui measure text-fg/78">
                  Jump to champions, meta routes, and player pages without leaving the current screen.
                </p>
              </div>
              <div className="hidden items-center gap-2 self-start lg:flex">
                <span className="type-kicker surface-chip rounded-full px-2.5 py-1 text-fg/68">
                  Enter to open
                </span>
                <span className="type-kicker surface-chip rounded-full px-2.5 py-1 text-fg/68">
                  Esc to close
                </span>
              </div>
            </div>

            <div className="mt-4 flex flex-col gap-3 sm:flex-row sm:items-end">
              <div className="relative flex-1">
                <SearchIcon className="pointer-events-none absolute left-4 top-1/2 h-4 w-4 -translate-y-1/2 text-primary/75" />
                <input
                  ref={inputRef}
                  value={query}
                  onChange={(e) => setQuery(e.target.value)}
                  onKeyDown={handleQueryKeyDown}
                  placeholder="Search champions, tier list, or summoner"
                  className="type-ui h-12 w-full rounded-control border border-border/65 bg-surface-2/55 pl-11 pr-4 text-fg shadow-inset outline-none transition placeholder:text-muted/70 focus:border-primary/65 focus:bg-surface-2/70 focus:ring-2 focus:ring-primary/18"
                  aria-label="Global search input"
                />
              </div>

              <div className="sm:w-[124px]">
                <label className="type-kicker mb-2 block text-fg/65">Region</label>
                <select
                  className="type-ui h-12 w-full rounded-control border border-border/65 bg-surface-2/55 px-3 text-fg shadow-inset outline-none transition focus:border-primary/65 focus:bg-surface-2/70 focus:ring-2 focus:ring-primary/18"
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
            </div>

            {parsedRiotId && directOpenPath ? (
              <button
                type="button"
                onClick={() => navigate(directOpenPath)}
                className="surface-chip-accent mt-3 flex w-full items-center justify-between gap-3 rounded-control px-4 py-3 text-left transition hover:border-primary/38 hover:bg-primary/13"
              >
                <div className="grid gap-1">
                  <span className="type-kicker text-primary/92">Direct Open</span>
                  <span className="type-ui text-fg/92">
                    {parsedRiotId.gameName}#{parsedRiotId.tagLine} in {regionLabel}
                  </span>
                </div>
                <span className="type-kicker rounded-full border border-primary/30 px-2.5 py-1 text-primary">
                  Enter
                </span>
              </button>
            ) : null}
          </div>

          <Command.List className="max-h-[min(68vh,640px)] overflow-y-auto px-3 pb-4 pt-4 sm:px-4 sm:pb-5">
            {showEmpty ? (
              <Command.Empty className="surface-subtle rounded-card px-4 py-8 text-left">
                <p className="type-kicker text-primary/88">No Match Yet</p>
                <p className="mt-3 text-base text-fg/88">
                  Nothing lines up with that search.
                </p>
                <p className="mt-2 max-w-[48ch] text-sm leading-6 text-fg/62">
                  Try a champion name, a route like tier list or pro builds, or a full Riot ID like
                  <span className="font-medium text-fg/82"> Kronic#NA1</span>.
                </p>
              </Command.Empty>
            ) : (
              <div className="grid gap-3 md:grid-cols-[minmax(0,1.15fr)_minmax(280px,0.85fr)]">
                <SearchSection
                  title="Summoners"
                  countLabel={summonerLoading ? "Searching" : `${summonerResults.length}`}
                  className="md:row-span-2"
                >
                  {summonerResults.map((item) => {
                    const path = `/lol/summoners/${item.region}/${encodeRiotIdPath({
                      gameName: item.gameName,
                      tagLine: item.tagLine
                    })}`;

                    return (
                      <Command.Item
                        key={`summoner-${item.platformRegion}-${item.gameName}-${item.tagLine}`}
                        value={`summoner-${item.gameName}-${item.tagLine}-${item.platformRegion}`}
                        onSelect={() => navigate(path)}
                        className={RESULT_ITEM_CLASS}
                      >
                        {ddragonVersion ? (
                          <Image
                            src={profileIconSrc(ddragonVersion, item.profileIconId)}
                            alt=""
                            width={36}
                            height={36}
                            className="h-9 w-9 rounded-lg border border-border/55 object-cover"
                            sizes="36px"
                          />
                        ) : (
                          <span className="inline-flex h-9 w-9 items-center justify-center rounded-lg border border-border/55 bg-surface/60 text-[11px] text-muted">
                            ?
                          </span>
                        )}
                        <div className="min-w-0 flex-1">
                          <p className="truncate text-[0.97rem] font-medium text-fg">
                            {item.gameName}
                            <span className="text-fg/58">#{item.tagLine}</span>
                          </p>
                          <p className="type-ui truncate text-fg/65">
                            Live profile lookup
                          </p>
                        </div>
                        <span className="type-kicker surface-chip rounded-full px-2 py-1 text-fg/68">
                          {item.platformRegion}
                        </span>
                      </Command.Item>
                    );
                  })}

                  {summonerLoading ? (
                    <SearchHint tone="accent">Searching live summoner suggestions for {regionLabel}.</SearchHint>
                  ) : null}

                  {!summonerLoading &&
                  summonerResults.length === 0 &&
                  parsedRiotId ? (
                    <Command.Item
                      key="summoner-open"
                      value={`summoner-${parsedRiotId.gameName}-${parsedRiotId.tagLine}-${region}`}
                      onSelect={() => navigate(directOpenPath!)}
                      className={RESULT_ITEM_CLASS}
                    >
                      <span className="inline-flex h-9 w-9 items-center justify-center rounded-lg border border-primary/25 bg-primary/10 text-primary">
                        <ArrowCornerIcon className="h-4 w-4" />
                      </span>
                      <div className="min-w-0 flex-1">
                        <p className="truncate text-[0.97rem] font-medium text-fg">
                          Open {parsedRiotId.gameName}#{parsedRiotId.tagLine}
                        </p>
                        <p className="type-ui text-fg/65">{regionLabel} direct profile route</p>
                      </div>
                    </Command.Item>
                  ) : null}

                  {!summonerLoading &&
                  summonerResults.length === 0 &&
                  !parsedRiotId &&
                  query.trim().length > 0 ? (
                    <SearchHint>
                      No summoner suggestions yet. Enter a full Riot ID in the format
                      <span className="font-medium text-fg/82"> GameName#TAG</span> to open the profile directly.
                    </SearchHint>
                  ) : null}

                  {!summonerLoading &&
                  summonerResults.length === 0 &&
                  query.trim().length === 0 ? (
                    <SearchHint>
                      Start with a Riot ID, like <span className="font-medium text-fg/82">Kronic#NA1</span>, for a direct player jump.
                    </SearchHint>
                  ) : null}
                </SearchSection>

                <SearchSection
                  title="Champions"
                  countLabel={`${championResults.length}`}
                >
                  {championResults.length > 0 ? (
                    championResults.map((champion) => (
                      <Command.Item
                        key={`champion-${champion.championId}`}
                        value={`champion-${champion.name}`}
                        onSelect={() => navigate(`/lol/champions/${champion.championId}`)}
                        className={RESULT_ITEM_CLASS}
                      >
                        <span className="surface-subtle inline-flex h-9 w-9 items-center justify-center rounded-lg text-primary/88">
                          <SparkIcon className="h-4 w-4" />
                        </span>
                        <div className="min-w-0 flex-1">
                          <p className="truncate text-[0.97rem] font-medium text-fg">{champion.name}</p>
                          <p className="type-ui text-fg/65">Champion profile and matchup data</p>
                        </div>
                      </Command.Item>
                    ))
                  ) : (
                    <SearchHint>No champions match that query.</SearchHint>
                  )}
                </SearchSection>

                <SearchSection
                  title="Meta Pages"
                  countLabel={`${tierResults.length}`}
                >
                  {tierResults.length > 0 ? (
                    tierResults.map((item) => {
                      const parts = splitQuickLinkLabel(item.label);
                      return (
                        <Command.Item
                          key={item.href}
                          value={`tier-${item.label}`}
                          onSelect={() => navigate(item.href)}
                          className={RESULT_ITEM_CLASS}
                        >
                          <span className="surface-subtle inline-flex h-9 w-9 items-center justify-center rounded-lg text-primary/84">
                            <ArrowCornerIcon className="h-4 w-4" />
                          </span>
                          <div className="min-w-0 flex-1">
                            <p className="truncate text-[0.97rem] font-medium text-fg">{parts.title}</p>
                            <p className="type-ui truncate text-fg/65">
                              {parts.detail ?? "Quick route into the meta surface"}
                            </p>
                          </div>
                        </Command.Item>
                      );
                    })
                  ) : (
                    <SearchHint>No meta routes match that search.</SearchHint>
                  )}
                </SearchSection>
              </div>
            )}
          </Command.List>
        </Command>
      </div>
    </div>
  );
}
