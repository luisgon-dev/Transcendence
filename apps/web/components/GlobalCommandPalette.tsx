"use client";

import { Command } from "cmdk";
import Image from "next/image";
import { useRouter } from "next/navigation";
import { Dialog, VisuallyHidden } from "radix-ui";
import { useEffect, useLayoutEffect, useMemo, useRef, useState } from "react";

import { SearchIcon } from "@/components/ui/icons";
import { Select } from "@/components/ui/Select";
import { GLOBAL_SEARCH_OPEN_EVENT } from "@/lib/globalSearch";
import { buildLolPublicSummonerSearchPath } from "@/lib/lolPublicApi";
import { LOL_REGION_OPTIONS } from "@/lib/lolRegions";
import { encodeRiotIdPath, parseRiotIdInput } from "@/lib/riotid";
import { searchMatchScore } from "@/lib/searchNormalization";
import { championIconUrl } from "@/lib/staticData";

type ChampionSearchItem = {
  championId: number;
  name: string;
  slug: string;
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

type NavigationItem = {
  label: string;
  detail: string;
  href: string;
  keywords: string;
  requiresBuildLab?: boolean;
};

const NAVIGATION_ITEMS: readonly NavigationItem[] = [
  {
    label: "Tier List",
    detail: "Champion rankings",
    href: "/lol/tierlist",
    keywords: "tiers rankings"
  },
  {
    label: "Leaderboards",
    detail: "Regional and champion rankings",
    href: "/lol/leaderboards",
    keywords: "rank ladder"
  },
  {
    label: "Champions",
    detail: "Champion list",
    href: "/lol/champions",
    keywords: "champion analytics"
  },
  {
    label: "Build Lab",
    detail: "Adjusted item, rune, and spell decisions",
    href: "/lol/builds",
    keywords: "items runes spells builds wpa lab",
    requiresBuildLab: true
  },
  {
    label: "Build Atlas",
    detail: "Items and runes",
    href: "/lol/items",
    keywords: "items runes builds atlas library"
  },
  {
    label: "Runes",
    detail: "Rune usage and win rates",
    href: "/lol/runes",
    keywords: "runes keystone shards library"
  },
  {
    label: "Live Game",
    detail: "Current match lookup",
    href: "/lol/live",
    keywords: "spectator current match"
  },
  {
    label: "Pro Solo Q",
    detail: "Professional solo queue builds",
    href: "/lol/pro-builds",
    keywords: "pros pro builds"
  },
  {
    label: "Multi-Search",
    detail: "Search several players",
    href: "/lol/multi-search",
    keywords: "multi search scout team"
  }
];

const RESULT_ITEM_CLASS =
  "group flex min-h-12 cursor-pointer items-center gap-3 rounded-lg px-3 py-2 text-left text-fg/82 outline-none transition-colors data-[selected=true]:bg-primary/12 data-[selected=true]:text-fg";

function isEditableTarget(target: EventTarget | null) {
  if (!(target instanceof HTMLElement)) return false;
  if (target.isContentEditable) return true;
  const tag = target.tagName.toLowerCase();
  return tag === "input" || tag === "textarea" || tag === "select";
}

function useDebouncedValue<T>(value: T, delayMs: number) {
  const [debounced, setDebounced] = useState(value);

  useEffect(() => {
    const timeoutId = window.setTimeout(() => setDebounced(value), delayMs);
    return () => window.clearTimeout(timeoutId);
  }, [delayMs, value]);

  return debounced;
}

function profileIconSrc(version: string, profileIconId: number) {
  return `https://ddragon.leagueoflegends.com/cdn/${version}/img/profileicon/${profileIconId}.png`;
}

function resultValue(kind: string, id: string | number) {
  return `${kind}:${id}`;
}

function ResultGroup({
  heading,
  children
}: {
  heading: string;
  children: React.ReactNode;
}) {
  return (
    <Command.Group
      heading={heading}
      className="px-2 py-1.5 text-xs text-muted [&_[cmdk-group-heading]]:px-2 [&_[cmdk-group-heading]]:py-2 [&_[cmdk-group-heading]]:font-medium [&_[cmdk-group-heading]]:uppercase [&_[cmdk-group-heading]]:tracking-[0.12em]"
    >
      {children}
    </Command.Group>
  );
}

export function GlobalCommandPalette({ buildLabEnabled = false }: { buildLabEnabled?: boolean }) {
  const router = useRouter();
  const inputRef = useRef<HTMLInputElement | null>(null);
  const lastFocusedRef = useRef<HTMLElement | null>(null);
  const suggestionCacheRef = useRef<Map<string, SummonerSearchItem[]>>(new Map());
  const [open, setOpen] = useState(false);
  const [query, setQuery] = useState("");
  const [region, setRegion] = useState("na");
  const [selectedValue, setSelectedValue] = useState("");
  const [champions, setChampions] = useState<ChampionSearchItem[]>([]);
  const [ddragonVersion, setDdragonVersion] = useState<string | null>(null);
  const [championsLoaded, setChampionsLoaded] = useState(false);
  const [summonerResults, setSummonerResults] = useState<SummonerSearchItem[]>([]);
  const [summonerLoading, setSummonerLoading] = useState(false);

  const debouncedQuery = useDebouncedValue(query, 120);
  const trimmedQuery = debouncedQuery.trim();
  const parsedRiotId = parseRiotIdInput(query.trim());

  useEffect(() => {
    function openPalette() {
      lastFocusedRef.current = document.activeElement as HTMLElement | null;
      setOpen(true);
    }

    function onKeyDown(event: KeyboardEvent) {
      if (
        (event.metaKey || event.ctrlKey) &&
        event.key.toLowerCase() === "k" &&
        !event.altKey &&
        !event.shiftKey &&
        !isEditableTarget(event.target)
      ) {
        event.preventDefault();
        openPalette();
      }
    }

    window.addEventListener("keydown", onKeyDown);
    window.addEventListener(GLOBAL_SEARCH_OPEN_EVENT, openPalette);
    return () => {
      window.removeEventListener("keydown", onKeyDown);
      window.removeEventListener(GLOBAL_SEARCH_OPEN_EVENT, openPalette);
    };
  }, []);

  useEffect(() => {
    if (!open) return;
    const rafId = window.requestAnimationFrame(() => inputRef.current?.focus());
    return () => window.cancelAnimationFrame(rafId);
  }, [open]);

  useEffect(() => {
    if (!open || championsLoaded) return;

    let active = true;
    void fetch("/api/static/champions", { cache: "force-cache" })
      .then(async (response) => {
        if (!response.ok) throw new Error("Failed to load champions.");
        const payload = (await response.json()) as ChampionsResponse;
        if (!active) return;

        setDdragonVersion(payload.version);
        setChampions(
          Object.entries(payload.champions)
            .map(([championId, champion]) => ({
              championId: Number(championId),
              name: champion.name,
              slug: champion.id
            }))
            .filter((champion) => Number.isFinite(champion.championId))
            .sort((a, b) => a.name.localeCompare(b.name))
        );
      })
      .catch(() => undefined)
      .finally(() => {
        if (active) setChampionsLoaded(true);
      });

    return () => {
      active = false;
    };
  }, [championsLoaded, open]);

  useEffect(() => {
    if (!open || trimmedQuery.length < 2) {
      setSummonerResults([]);
      setSummonerLoading(false);
      return;
    }

    const cacheKey = `${region}|${trimmedQuery.toLocaleLowerCase()}`;
    const cached = suggestionCacheRef.current.get(cacheKey);
    if (cached) {
      setSummonerResults(cached);
      setSummonerLoading(false);
      return;
    }

    const abortController = new AbortController();
    setSummonerLoading(true);
    void fetch(buildLolPublicSummonerSearchPath(region, trimmedQuery, 6), {
      cache: "no-store",
      signal: abortController.signal
    })
      .then(async (response) => {
        if (!response.ok) throw new Error("Summoner search failed.");
        const payload = (await response.json()) as SummonerSearchResponse;
        if (abortController.signal.aborted) return;
        const items = Array.isArray(payload.items) ? payload.items : [];
        setSummonerResults(items);
        suggestionCacheRef.current.set(cacheKey, items);
        if (suggestionCacheRef.current.size > 60) {
          const oldestKey = suggestionCacheRef.current.keys().next().value as string | undefined;
          if (oldestKey) suggestionCacheRef.current.delete(oldestKey);
        }
      })
      .catch(() => {
        if (!abortController.signal.aborted) setSummonerResults([]);
      })
      .finally(() => {
        if (!abortController.signal.aborted) setSummonerLoading(false);
      });

    return () => abortController.abort();
  }, [open, region, trimmedQuery]);

  const championResults = useMemo(() => {
    if (!trimmedQuery) return champions.slice(0, 5);
    return champions
      .map((champion) => ({
        champion,
        score: Math.min(
          searchMatchScore(champion.name, trimmedQuery) ?? Number.POSITIVE_INFINITY,
          searchMatchScore(champion.slug, trimmedQuery) ?? Number.POSITIVE_INFINITY
        )
      }))
      .filter((result) => Number.isFinite(result.score))
      .sort(
        (a, b) =>
          a.score - b.score ||
          a.champion.name.length - b.champion.name.length ||
          a.champion.name.localeCompare(b.champion.name)
      )
      .slice(0, 8)
      .map((result) => result.champion);
  }, [champions, trimmedQuery]);

  const navigationResults = useMemo(() => {
    const navigationItems = buildLabEnabled
      ? NAVIGATION_ITEMS
      : NAVIGATION_ITEMS.filter((item) => !item.requiresBuildLab);
    if (!trimmedQuery) return navigationItems;
    return navigationItems.map((item) => ({
      item,
      score: searchMatchScore(`${item.label} ${item.keywords}`, trimmedQuery)
    }))
      .filter((result) => result.score != null)
      .sort((a, b) => a.score! - b.score! || a.item.label.localeCompare(b.item.label))
      .map((result) => result.item);
  }, [buildLabEnabled, trimmedQuery]);

  const directPlayerPath = parsedRiotId
    ? `/lol/summoners/${region}/${encodeRiotIdPath(parsedRiotId)}`
    : null;
  const directPlayerValue = parsedRiotId
    ? resultValue("direct-player", `${parsedRiotId.gameName}-${parsedRiotId.tagLine}-${region}`)
    : null;

  useLayoutEffect(() => {
    if (!open) return;
    if (directPlayerValue) {
      setSelectedValue(directPlayerValue);
      return;
    }
    if (championResults[0]) {
      setSelectedValue(resultValue("champion", championResults[0].championId));
      return;
    }
    if (navigationResults[0]) {
      setSelectedValue(resultValue("navigation", navigationResults[0].href));
      return;
    }
    if (summonerResults[0]) {
      setSelectedValue(
        resultValue(
          "player",
          `${summonerResults[0].platformRegion}-${summonerResults[0].gameName}-${summonerResults[0].tagLine}`
        )
      );
      return;
    }
    setSelectedValue("");
  }, [
    championResults,
    directPlayerValue,
    navigationResults,
    open,
    summonerResults
  ]);

  const prefetchTargets = useMemo(() => {
    const targets = championResults
      .slice(0, 3)
      .map((champion) => `/lol/champions/${champion.championId}`);
    if (directPlayerPath) targets.push(directPlayerPath);
    for (const item of navigationResults.slice(0, 2)) targets.push(item.href);
    return targets;
  }, [championResults, directPlayerPath, navigationResults]);

  useEffect(() => {
    if (!open) return;
    for (const target of prefetchTargets) router.prefetch(target);
  }, [open, prefetchTargets, router]);

  function navigate(path: string) {
    setOpen(false);
    setQuery("");
    setSelectedValue("");
    router.push(path);
  }

  function renderChampionGroup() {
    if (championResults.length === 0) return null;
    return (
      <ResultGroup heading="Champions">
        {championResults.map((champion) => (
          <Command.Item
            key={champion.championId}
            value={resultValue("champion", champion.championId)}
            onSelect={() => navigate(`/lol/champions/${champion.championId}`)}
            className={RESULT_ITEM_CLASS}
          >
            {ddragonVersion ? (
              <Image
                src={championIconUrl(ddragonVersion, champion.slug)}
                alt=""
                width={34}
                height={34}
                className="size-[34px] rounded-md"
              />
            ) : (
              <span className="size-[34px] rounded-md bg-surface-2" />
            )}
            <span className="min-w-0 flex-1 truncate font-medium">{champion.name}</span>
            <span className="text-xs text-muted group-data-[selected=true]:text-fg/65">
              Champion
            </span>
          </Command.Item>
        ))}
      </ResultGroup>
    );
  }

  function renderPlayerGroup() {
    if (!directPlayerPath && summonerResults.length === 0 && !summonerLoading) return null;
    return (
      <ResultGroup heading="Players">
        {directPlayerPath && parsedRiotId && directPlayerValue ? (
          <Command.Item
            value={directPlayerValue}
            onSelect={() => navigate(directPlayerPath)}
            className={RESULT_ITEM_CLASS}
          >
            <span className="flex size-[34px] items-center justify-center rounded-md bg-primary/12 font-semibold text-primary">
              #
            </span>
            <span className="min-w-0 flex-1 truncate font-medium">
              {parsedRiotId.gameName}
              <span className="text-fg/55">#{parsedRiotId.tagLine}</span>
            </span>
            <span className="text-xs uppercase text-muted">{region}</span>
          </Command.Item>
        ) : null}
        {summonerResults.map((summoner) => {
          const path = `/lol/summoners/${summoner.region}/${encodeRiotIdPath(summoner)}`;
          const value = resultValue(
            "player",
            `${summoner.platformRegion}-${summoner.gameName}-${summoner.tagLine}`
          );
          return (
            <Command.Item
              key={value}
              value={value}
              onSelect={() => navigate(path)}
              className={RESULT_ITEM_CLASS}
            >
              {ddragonVersion ? (
                <Image
                  src={profileIconSrc(ddragonVersion, summoner.profileIconId)}
                  alt=""
                  width={34}
                  height={34}
                  className="size-[34px] rounded-md"
                />
              ) : (
                <span className="size-[34px] rounded-md bg-surface-2" />
              )}
              <span className="min-w-0 flex-1 truncate font-medium">
                {summoner.gameName}
                <span className="text-fg/55">#{summoner.tagLine}</span>
              </span>
              <span className="text-xs text-muted">{summoner.platformRegion}</span>
            </Command.Item>
          );
        })}
        {summonerLoading ? (
          <p className="px-3 py-2 text-sm text-muted">Searching players…</p>
        ) : null}
      </ResultGroup>
    );
  }

  function renderNavigationGroup() {
    if (navigationResults.length === 0) return null;
    return (
      <ResultGroup heading="Navigation">
        {navigationResults.map((item) => (
          <Command.Item
            key={item.href}
            value={resultValue("navigation", item.href)}
            onSelect={() => navigate(item.href)}
            className={RESULT_ITEM_CLASS}
          >
            <span className="flex size-[34px] items-center justify-center rounded-md bg-surface-2 text-muted">
              ↗
            </span>
            <span className="min-w-0 flex-1">
              <span className="block truncate font-medium">{item.label}</span>
              <span className="block truncate text-xs text-muted">{item.detail}</span>
            </span>
          </Command.Item>
        ))}
      </ResultGroup>
    );
  }

  const hasResults =
    championResults.length > 0 ||
    navigationResults.length > 0 ||
    summonerResults.length > 0 ||
    Boolean(directPlayerPath) ||
    summonerLoading;
  const championFirst = !parsedRiotId && championResults.length > 0;

  return (
    <Dialog.Root
      open={open}
      onOpenChange={(nextOpen) => {
        setOpen(nextOpen);
        if (!nextOpen) {
          setQuery("");
          setSelectedValue("");
        }
      }}
    >
      <Dialog.Portal>
        <Dialog.Overlay className="fixed inset-0 z-50 bg-bg/76" />
        <Dialog.Content
          aria-label="Global search"
          aria-modal="true"
          aria-describedby={undefined}
          onOpenAutoFocus={(event) => {
            event.preventDefault();
            inputRef.current?.focus();
          }}
          onCloseAutoFocus={(event) => {
            event.preventDefault();
            lastFocusedRef.current?.focus?.();
          }}
          className="fixed left-1/2 top-[max(1rem,8vh)] z-50 w-[min(720px,calc(100vw-24px))] -translate-x-1/2 overflow-hidden rounded-2xl border border-border/75 bg-surface shadow-overlay focus:outline-none"
        >
          <VisuallyHidden.Root asChild>
            <Dialog.Title>Global search</Dialog.Title>
          </VisuallyHidden.Root>
          <Command
            label="Global search input"
            shouldFilter={false}
            value={selectedValue}
            onValueChange={setSelectedValue}
          >
            <div className="flex items-center gap-2 border-b border-border/55 p-3">
              <SearchIcon className="ml-1 size-5 shrink-0 text-muted" />
              <Command.Input
                ref={inputRef}
                value={query}
                onValueChange={setQuery}
                placeholder="Search champions, players, or pages"
                aria-label="Global search input"
                className="h-11 min-w-0 flex-1 bg-transparent px-1 text-base text-fg outline-none placeholder:text-muted"
              />
              <Select
                value={region}
                onValueChange={setRegion}
                options={[...LOL_REGION_OPTIONS]}
                ariaLabel="Summoner region"
                className="h-10 w-[106px] shrink-0"
              />
            </div>

            <Command.List className="max-h-[min(66vh,560px)] overflow-y-auto py-1">
              {!hasResults ? (
                <Command.Empty className="px-5 py-10 text-center text-sm text-muted">
                  No results. Try a champion, page, or Riot ID.
                </Command.Empty>
              ) : parsedRiotId ? (
                <>
                  {renderPlayerGroup()}
                  {renderChampionGroup()}
                  {renderNavigationGroup()}
                </>
              ) : championFirst ? (
                <>
                  {renderChampionGroup()}
                  {renderPlayerGroup()}
                  {renderNavigationGroup()}
                </>
              ) : (
                <>
                  {renderNavigationGroup()}
                  {renderPlayerGroup()}
                  {renderChampionGroup()}
                </>
              )}
            </Command.List>

            <div className="flex items-center justify-between border-t border-border/50 px-4 py-2 text-xs text-muted">
              <span>↑↓ Select</span>
              <span>Enter Open · Esc Close</span>
            </div>
          </Command>
        </Dialog.Content>
      </Dialog.Portal>
    </Dialog.Root>
  );
}
