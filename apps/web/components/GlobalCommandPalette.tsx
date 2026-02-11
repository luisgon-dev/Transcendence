"use client";

import { Command } from "cmdk";
import { useRouter } from "next/navigation";
import { useEffect, useMemo, useRef, useState } from "react";

import { GLOBAL_SEARCH_OPEN_EVENT } from "@/lib/globalSearch";
import { encodeRiotIdPath, parseRiotIdInput } from "@/lib/riotid";

type ChampionSearchItem = {
  championId: number;
  name: string;
};

type ChampionsResponse = {
  champions: Record<string, { id: string; name: string }>;
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
  { label: "Tier List · All Roles", href: "/tierlist" },
  { label: "Tier List · Top", href: "/tierlist?role=TOP" },
  { label: "Tier List · Jungle", href: "/tierlist?role=JUNGLE" },
  { label: "Tier List · Middle", href: "/tierlist?role=MIDDLE" },
  { label: "Tier List · Bottom", href: "/tierlist?role=BOTTOM" },
  { label: "Tier List · Support", href: "/tierlist?role=UTILITY" },
  { label: "Tier List · Challenger", href: "/tierlist?rankTier=CHALLENGER" }
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

export function GlobalCommandPalette() {
  const router = useRouter();
  const inputRef = useRef<HTMLInputElement | null>(null);
  const [open, setOpen] = useState(false);
  const [query, setQuery] = useState("");
  const [region, setRegion] = useState("na");
  const [champions, setChampions] = useState<ChampionSearchItem[]>([]);
  const [championsLoaded, setChampionsLoaded] = useState(false);

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

  const prefetchTargets = useMemo(() => {
    const targets = championResults.slice(0, 3).map((c) => `/champions/${c.championId}`);
    for (const tier of tierResults.slice(0, 2)) targets.push(tier.href);
    if (parsedRiotId) {
      targets.push(`/summoners/${region}/${encodeRiotIdPath(parsedRiotId)}`);
    }
    return targets;
  }, [championResults, tierResults, parsedRiotId, region]);

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

  if (!open) return null;

  const showEmpty =
    championResults.length === 0 && tierResults.length === 0 && !parsedRiotId;

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
              placeholder="Search champions, tier list, or summoner Riot ID (GameName#TAG)"
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

            <Command.Group heading="Summoner">
              {parsedRiotId ? (
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
              ) : (
                <p className="px-3 py-2 text-sm text-muted">
                  Enter a Riot ID as GameName#TAG.
                </p>
              )}
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
