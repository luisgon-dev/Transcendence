"use client";

import { Command } from "cmdk";
import { AnimatePresence, motion, useReducedMotion } from "framer-motion";
import Image from "next/image";
import { useRouter } from "next/navigation";
import { Dialog, VisuallyHidden } from "radix-ui";
import { useEffect, useMemo, useRef, useState } from "react";

import { ArrowCornerIcon, SearchIcon, SparkIcon } from "@/components/ui/icons";
import { Select } from "@/components/ui/Select";
import {
  getGlobalSearchOpenDetail,
  GLOBAL_SEARCH_OPEN_EVENT,
  type GlobalSearchOpenOrigin
} from "@/lib/globalSearch";
import { buildLolPublicSummonerSearchPath } from "@/lib/lolPublicApi";
import { DEFAULT_TIERLIST_RANK_TIER, rankTierDisplayLabel } from "@/lib/ranks";
import { encodeRiotIdPath, parseRiotIdInput } from "@/lib/riotid";
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
  { label: "Champions", href: "/lol/champions" },
  { label: "Pro Builds", href: "/lol/pro-builds" }
] as const;

const RESULT_ITEM_CLASS =
  "group flex min-h-[52px] cursor-pointer items-center gap-3 rounded-card border border-transparent px-3 py-2.5 text-left text-fg/90 transition duration-150 data-[selected=true]:translate-x-1 data-[selected=true]:border-primary/25 data-[selected=true]:bg-primary/10 data-[selected=true]:shadow-card";

const PANEL_ENTRY_EASE = [0.16, 1, 0.3, 1] as const;

const resultsContainerVariants = {
  hidden: { opacity: 0 },
  visible: {
    opacity: 1,
    transition: {
      staggerChildren: 0.045,
      delayChildren: 0.08
    }
  },
  exit: {
    opacity: 0,
    transition: {
      duration: 0.12
    }
  }
};

const resultsSectionVariants = {
  hidden: { opacity: 0, y: 14 },
  visible: {
    opacity: 1,
    y: 0,
    transition: {
      duration: 0.26,
      ease: PANEL_ENTRY_EASE
    }
  }
};

function getPanelTopOffset() {
  if (typeof window === "undefined") return 96;
  return Math.max(72, Math.round(window.innerHeight * 0.09));
}

function getPanelWidth() {
  if (typeof window === "undefined") return 880;
  return Math.max(320, Math.min(880, window.innerWidth - 24));
}

function getOverlayBackground(origin: GlobalSearchOpenOrigin | null) {
  if (typeof window === "undefined") return undefined;

  const spotlightX = origin?.centerX ?? window.innerWidth / 2;
  const spotlightY = origin?.centerY ?? Math.max(96, window.innerHeight * 0.18);

  return {
    background: `
      radial-gradient(480px 280px at ${spotlightX}px ${spotlightY}px, color-mix(in oklch, var(--t-primary), transparent 86%), transparent 60%),
      radial-gradient(760px 340px at 50% 0%, color-mix(in oklch, var(--t-fg), transparent 96%), transparent 68%),
      oklch(0.12 0.02 264 / 0.72)
    `
  };
}

function getPanelEnterState(
  origin: GlobalSearchOpenOrigin | null,
  prefersReducedMotion: boolean
) {
  if (prefersReducedMotion || typeof window === "undefined") {
    return {
      opacity: 0,
      x: 0,
      y: -10,
      scaleX: 1,
      scaleY: 1,
      borderRadius: 24
    };
  }

  if (!origin) {
    return {
      opacity: 0,
      x: 0,
      y: -18,
      scaleX: 0.985,
      scaleY: 0.97,
      borderRadius: 24
    };
  }

  const panelWidth = getPanelWidth();
  const panelTop = getPanelTopOffset();

  return {
    opacity: 0.76,
    x: origin.centerX - window.innerWidth / 2,
    y: origin.centerY - panelTop,
    scaleX: Math.min(1, Math.max(0.18, origin.width / panelWidth)),
    scaleY: Math.min(1, Math.max(0.15, origin.height / 280)),
    borderRadius: Math.max(18, Math.round(origin.height / 2))
  };
}

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
  const prefersReducedMotion = useReducedMotion() ?? false;
  const inputRef = useRef<HTMLInputElement | null>(null);
  // The element focused when the palette opened, so focus returns there on close. Captured manually
  // because forceMount (kept for the exit animation) disrupts Radix's own focus-restore capture.
  const lastFocusedRef = useRef<HTMLElement | null>(null);
  const suggestionCacheRef = useRef<Map<string, SummonerSearchItem[]>>(new Map());
  const [open, setOpen] = useState(false);
  const [openOrigin, setOpenOrigin] = useState<GlobalSearchOpenOrigin | null>(null);
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
        lastFocusedRef.current = document.activeElement as HTMLElement | null;
        setOpenOrigin(null);
        setOpen(true);
      }
      // Escape (and outside-click / focus-out) close is owned by the Radix Dialog below, which also
      // dismisses a nested Region dropdown first — so no manual Escape handling here.
    }

    function onOpenEvent(event: Event) {
      lastFocusedRef.current = document.activeElement as HTMLElement | null;
      setOpenOrigin(getGlobalSearchOpenDetail(event).origin);
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
            name: data.name,
            slug: data.id
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

  const regionLabel = REGIONS.find((item) => item.value === region)?.label ?? region.toUpperCase();
  const directOpenPath = parsedRiotId
    ? `/lol/summoners/${region}/${encodeRiotIdPath(parsedRiotId)}`
    : null;
  const showEmpty =
    championResults.length === 0 &&
    tierResults.length === 0 &&
    summonerResults.length === 0 &&
    !parsedRiotId;
  const panelEnterState = getPanelEnterState(openOrigin, prefersReducedMotion);
  const overlayStyle = getOverlayBackground(openOrigin);
  const sectionVariants = prefersReducedMotion
    ? {
        hidden: { opacity: 0 },
        visible: {
          opacity: 1,
          transition: { duration: 0.12 }
        }
      }
    : resultsSectionVariants;
  const panelTopOffset = getPanelTopOffset();

  return (
    // Radix Dialog supplies the modal a11y the hand-rolled overlay lacked: role="dialog" + aria-modal,
    // a focus trap, focus return to the launcher on close, Escape-to-close, and page scroll lock — while
    // forceMount keeps the nodes present so framer-motion still owns the enter/exit animation.
    <Dialog.Root open={open} onOpenChange={setOpen}>
      <Dialog.Portal forceMount>
        <AnimatePresence initial={false} onExitComplete={() => setOpenOrigin(null)}>
          {open ? (
            <div className="command-palette-overlay fixed inset-0 z-50">
              <Dialog.Overlay asChild forceMount>
                <motion.div
                  className="absolute inset-0 backdrop-blur-md"
                  style={overlayStyle}
                  initial={{ opacity: 0 }}
                  animate={{ opacity: 1 }}
                  exit={{ opacity: 0 }}
                  transition={{ duration: prefersReducedMotion ? 0.12 : 0.2, ease: PANEL_ENTRY_EASE }}
                />
              </Dialog.Overlay>

              <div
            className="command-palette-shell absolute inset-x-0 top-0 flex justify-center px-3"
            style={{ paddingTop: `${panelTopOffset}px` }}
          >
            <Dialog.Content
              asChild
              forceMount
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
            >
            <motion.div
              className="command-palette-panel pointer-events-auto w-[min(880px,calc(100vw-24px))] overflow-hidden border border-border/70 bg-surface shadow-overlay"
              initial={panelEnterState}
              animate={{
                opacity: 1,
                x: 0,
                y: 0,
                scaleX: 1,
                scaleY: 1,
                borderRadius: 24
              }}
              exit={
                prefersReducedMotion
                  ? { opacity: 0, y: -6 }
                  : {
                      opacity: 0,
                      y: -16,
                      scaleX: 0.985,
                      scaleY: 0.98
                    }
              }
              transition={
                prefersReducedMotion
                  ? { duration: 0.12 }
                  : {
                      type: "spring",
                      stiffness: 300,
                      damping: 32,
                      mass: 0.82,
                      opacity: { duration: 0.16, ease: PANEL_ENTRY_EASE }
                    }
              }
              style={{ transformOrigin: "top center" }}
            >
              <VisuallyHidden.Root asChild>
                <Dialog.Title>Global search</Dialog.Title>
              </VisuallyHidden.Root>
              <Command shouldFilter={false} className="relative w-full">
                <div className="pointer-events-none absolute inset-x-8 top-0 h-px bg-gradient-to-r from-transparent via-primary/75 to-transparent" />

                <motion.div
                  variants={resultsContainerVariants}
                  initial="hidden"
                  animate="visible"
                  exit="exit"
                  className="border-b border-border/50 px-4 pb-4 pt-4 sm:px-5 sm:pb-5 sm:pt-5"
                >
                  <motion.div
                    variants={sectionVariants}
                    className="grid gap-3 lg:grid-cols-[minmax(0,1fr)_auto] lg:items-start"
                  >
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
                  </motion.div>

                  <motion.div variants={sectionVariants} className="mt-4 flex flex-wrap gap-2">
                    <span className="type-kicker surface-chip-accent rounded-full px-2.5 py-1 text-primary/92">
                      {query.trim() ? "Filtering live" : "Ready for instant route"}
                    </span>
                    <span className="type-kicker surface-chip rounded-full px-2.5 py-1 text-fg/68">
                      {championsLoaded ? `${championResults.length} champion routes` : "Loading champion index"}
                    </span>
                    <span className="type-kicker surface-chip rounded-full px-2.5 py-1 text-fg/68">
                      {summonerLoading ? `Checking ${regionLabel}` : `Region ${regionLabel}`}
                    </span>
                  </motion.div>

                  <motion.div
                    variants={sectionVariants}
                    className="mt-4 flex flex-col gap-3 sm:flex-row sm:items-end"
                  >
                    <div className="relative flex-1">
                      <SearchIcon className="pointer-events-none absolute left-4 top-1/2 h-4 w-4 -translate-y-1/2 text-primary/75" />
                      <Command.Input
                        ref={inputRef}
                        value={query}
                        onValueChange={setQuery}
                        onKeyDown={handleQueryKeyDown}
                        placeholder="Search champions, tier list, or summoner"
                        className="type-ui h-12 w-full rounded-control border border-border/65 bg-surface-2/55 pl-11 pr-4 text-fg shadow-inset outline-none transition placeholder:text-muted/70 focus:border-primary/65 focus:bg-surface-2/70 focus:ring-2 focus:ring-primary/18"
                        aria-label="Global search input"
                      />
                    </div>

                    <div className="sm:w-[124px]">
                      <label className="type-kicker mb-2 block text-fg/65">Region</label>
                      <Select
                        value={region}
                        onValueChange={setRegion}
                        options={[...REGIONS]}
                        ariaLabel="Summoner region"
                        className="h-12 w-full rounded-control border-border/65 bg-surface-2/55 text-fg shadow-inset"
                      />
                    </div>
                  </motion.div>

                  {parsedRiotId && directOpenPath ? (
                    <motion.button
                      variants={sectionVariants}
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
                    </motion.button>
                  ) : null}
                </motion.div>

                <Command.List className="max-h-[min(68vh,640px)] overflow-y-auto px-3 pb-4 pt-4 sm:px-4 sm:pb-5">
                  {showEmpty ? (
                    <motion.div
                      initial={{ opacity: 0, y: prefersReducedMotion ? 0 : 10 }}
                      animate={{ opacity: 1, y: 0 }}
                      exit={{ opacity: 0 }}
                      transition={{ duration: 0.18, ease: PANEL_ENTRY_EASE }}
                    >
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
                    </motion.div>
                  ) : (
                    <motion.div
                      variants={resultsContainerVariants}
                      initial="hidden"
                      animate="visible"
                      exit="exit"
                      className="grid gap-3 md:grid-cols-[minmax(0,1.15fr)_minmax(280px,0.85fr)]"
                    >
                      <motion.div variants={sectionVariants}>
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
                                  <span className="type-caption inline-flex h-9 w-9 items-center justify-center rounded-lg border border-border/55 bg-surface/60 text-muted">
                                    ?
                                  </span>
                                )}
                                <div className="min-w-0 flex-1">
                                  <p className="type-ui truncate font-medium text-fg">
                                    {item.gameName}
                                    <span className="text-fg/58">#{item.tagLine}</span>
                                  </p>
                                  <p className="type-caption truncate text-fg/65">
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
                                <p className="type-ui truncate font-medium text-fg">
                                  Open {parsedRiotId.gameName}#{parsedRiotId.tagLine}
                                </p>
                                <p className="type-caption text-fg/65">{regionLabel} direct profile route</p>
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
                      </motion.div>

                      <motion.div variants={sectionVariants}>
                        <SearchSection
                          title="Champions"
                          countLabel={`${championResults.length}`}
                        >
                          {championResults.length > 0 ? (
                            championResults.map((champion) => {
                              const championHref = `/lol/champions/${champion.championId}`;
                              return (
                                <Command.Item
                                  key={`champion-${champion.championId}`}
                                  value={`champion-${champion.name}`}
                                  onSelect={() => navigate(championHref)}
                                  onMouseEnter={() => router.prefetch(championHref)}
                                  onFocus={() => router.prefetch(championHref)}
                                  className={RESULT_ITEM_CLASS}
                                >
                                  {ddragonVersion && champion.slug ? (
                                    <Image
                                      src={championIconUrl(ddragonVersion, champion.slug)}
                                      alt=""
                                      width={36}
                                      height={36}
                                      className="h-9 w-9 shrink-0 rounded-lg border border-border/50"
                                    />
                                  ) : (
                                    <span className="surface-subtle inline-flex h-9 w-9 items-center justify-center rounded-lg text-primary/88">
                                      <SparkIcon className="h-4 w-4" />
                                    </span>
                                  )}
                                  <div className="min-w-0 flex-1">
                                    <p className="type-ui truncate font-medium text-fg">{champion.name}</p>
                                    <p className="type-caption text-fg/65">Champion profile and matchup data</p>
                                  </div>
                                </Command.Item>
                              );
                            })
                          ) : (
                            <SearchHint>No champions match that query.</SearchHint>
                          )}
                        </SearchSection>
                      </motion.div>

                      <motion.div variants={sectionVariants}>
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
                                    <p className="type-ui truncate font-medium text-fg">{parts.title}</p>
                                    <p className="type-caption truncate text-fg/65">
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
                      </motion.div>
                    </motion.div>
                  )}
                </Command.List>
              </Command>
            </motion.div>
            </Dialog.Content>
          </div>
        </div>
      ) : null}
        </AnimatePresence>
      </Dialog.Portal>
    </Dialog.Root>
  );
}
