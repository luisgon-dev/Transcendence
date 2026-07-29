"use client";

import Link from "next/link";
import { useEffect, useMemo, useState } from "react";

import { Badge } from "@/components/ui/Badge";
import { Button } from "@/components/ui/Button";
import { Card } from "@/components/ui/Card";
import { EmptyState } from "@/components/ui/EmptyState";
import { Input } from "@/components/ui/Input";
import { SegmentedControl } from "@/components/ui/SegmentedControl";
import {
  buildLabPermalink,
  type BuildLabMode,
  type BuildLabRole,
  type SavedBuild,
  type SavedBuildCompatibilityStatus,
  type SavedBuildRepairChoice,
  type SavedBuildUnavailableItem
} from "@/lib/buildLab";
import { searchMatchScore } from "@/lib/searchNormalization";
import type { ItemMap } from "@/lib/staticData";

type CompatibilityBadge = { label: string; className?: string };

// Each status says something different, so each gets its own words. NO_SOURCE_GENERATION is
// informational — the build predates published analytics — so it wears neither the action red nor
// the muted data red that encodes a bad outcome.
const COMPATIBILITY_BADGES: Partial<Record<SavedBuildCompatibilityStatus, CompatibilityBadge>> = {
  ITEMS_RETIRED: {
    label: "Items unavailable",
    className: "border-danger/35 bg-danger/10 text-danger"
  },
  PATCH_CHANGED: {
    label: "Patch changed",
    className: "border-warning/40 bg-warning/10 text-warning"
  },
  NO_SOURCE_GENERATION: { label: "Saved before analytics were published" }
};

const UNAVAILABLE_REASONS: Record<SavedBuildUnavailableItem["reason"], string> = {
  RETIRED: "Retired from the game",
  REMOVED_FROM_STORE: "No longer purchasable"
};

function compatibilityBadge(status: SavedBuildCompatibilityStatus): CompatibilityBadge | null {
  if (status === "CURRENT") return null;
  return COMPATIBILITY_BADGES[status] ?? { label: "Needs review" };
}

function reasonLabel(reason: SavedBuildUnavailableItem["reason"]) {
  return UNAVAILABLE_REASONS[reason] ?? "Unavailable on this patch";
}

export function SavedBuildList({
  initialBuilds,
  authenticated
}: {
  initialBuilds: SavedBuild[];
  authenticated: boolean;
}) {
  const [builds, setBuilds] = useState(initialBuilds);
  const [status, setStatus] = useState<string | null>(null);
  const [repairingId, setRepairingId] = useState<string | null>(null);
  const [confirmingDeleteId, setConfirmingDeleteId] = useState<string | null>(null);

  function buildHref(build: SavedBuild) {
    return buildLabPermalink(build.championId, {
      role: build.role as BuildLabRole,
      opponentChampionId: build.opponentChampionId ?? undefined,
      patch: build.patch || undefined,
      region: build.region || undefined,
      mode: build.rankingMode.toLowerCase() as BuildLabMode,
      section: "items",
      itemPath: build.itemPath,
      runeSelections: build.runeSelections,
      spellPair: [build.spell1Id, build.spell2Id].filter(
        (value): value is number => typeof value === "number"
      )
    });
  }

  async function share(build: SavedBuild) {
    setStatus("Creating share link…");
    const result = await fetch(`/api/trn/user/users/me/lol/saved-builds/${build.id}/share`, {
      method: "POST"
    });
    if (!result.ok) {
      setStatus("The share link could not be created.");
      return;
    }
    const body = (await result.json()) as { shareId: string };
    const url = `${window.location.origin}/lol/builds/shared/${body.shareId}`;
    await navigator.clipboard?.writeText(url);
    setBuilds((current) =>
      current.map((candidate) =>
        candidate.id === build.id ? { ...candidate, shareId: body.shareId } : candidate
      )
    );
    setStatus("Read-only share link copied.");
  }

  async function revoke(build: SavedBuild) {
    const result = await fetch(`/api/trn/user/users/me/lol/saved-builds/${build.id}/share`, {
      method: "DELETE"
    });
    if (!result.ok) {
      setStatus("The share link could not be revoked.");
      return;
    }
    setBuilds((current) =>
      current.map((candidate) =>
        candidate.id === build.id ? { ...candidate, shareId: null } : candidate
      )
    );
    setStatus("Share link revoked immediately.");
  }

  async function remove(build: SavedBuild) {
    const result = await fetch(`/api/trn/user/users/me/lol/saved-builds/${build.id}`, {
      method: "DELETE"
    });
    if (!result.ok) {
      setStatus("The saved build could not be deleted.");
      return;
    }
    setConfirmingDeleteId(null);
    setBuilds((current) => current.filter((candidate) => candidate.id !== build.id));
    setStatus(`“${build.name}” was deleted.`);
  }

  if (!authenticated) {
    return (
      <EmptyState
        title="Sign in to save builds"
        description="Anonymous visitors can open and clone shared configurations. An account is required to keep and share your own."
        action={<Link href="/account/login" className="font-semibold text-primary">Sign in</Link>}
      />
    );
  }

  if (builds.length === 0) {
    return (
      <EmptyState
        title="No saved builds yet"
        description="Configure a champion in Build Lab, then save the complete item, rune, spell, matchup, and filter state."
        action={<Link href="/lol/builds" className="font-semibold text-primary">Open Build Lab</Link>}
      />
    );
  }

  return (
    <section className="grid gap-3" aria-label="Saved builds">
      {status ? <p className="text-sm text-muted" role="status">{status}</p> : null}
      {builds.map((build) => {
        const badge = compatibilityBadge(build.compatibilityStatus);
        const unavailable = build.unavailableItems ?? [];
        const repairing = repairingId === build.id;
        const confirmingDelete = confirmingDeleteId === build.id;
        return (
          <Card key={build.id} className="p-4">
            <div className="flex flex-col gap-4 lg:flex-row lg:items-start lg:justify-between">
              <div className="min-w-0">
                <div className="flex flex-wrap items-center gap-2">
                  <h2 className="type-section">{build.name}</h2>
                  <span className="rounded-full border border-border/60 px-2 py-0.5 text-xs text-muted">
                    {build.role} · {build.region || "GLOBAL"} · {build.patch}
                  </span>
                  {build.analyticsChanged ? (
                    <Badge className="border-warning/40 bg-warning/10 text-warning">
                      Analytics updated
                    </Badge>
                  ) : null}
                  {badge ? <Badge className={badge.className}>{badge.label}</Badge> : null}
                </div>
                <p className="mt-2 text-xs text-muted">
                  {build.itemPath.length} item selections · {build.runeSelections.length} rune selections
                  {build.spell1Id && build.spell2Id ? " · spell pair saved" : ""}
                  {build.opponentChampionId ? ` · opponent ${build.opponentChampionId}` : ""}
                </p>
                {unavailable.length > 0 ? (
                  <p className="mt-2 text-xs text-fg/70">
                    {unavailable.length} selection{unavailable.length === 1 ? "" : "s"} cannot be built
                    on the active patch. Nothing was substituted automatically — choose what happens to
                    each one.
                  </p>
                ) : null}
              </div>
              <div className="flex flex-wrap gap-2 lg:justify-end">
                <Link
                  href={buildHref(build)}
                  className="inline-flex min-h-9 items-center rounded-control bg-primary px-3 text-sm font-semibold text-primary-fg"
                >
                  Open current analysis
                </Link>
                {unavailable.length > 0 ? (
                  <Button
                    size="sm"
                    variant="outline"
                    aria-expanded={repairing}
                    aria-controls={repairing ? `repair-${build.id}` : undefined}
                    onClick={() => setRepairingId(repairing ? null : build.id)}
                  >
                    {repairing
                      ? "Close repair"
                      : `Repair ${unavailable.length} selection${unavailable.length === 1 ? "" : "s"}`}
                  </Button>
                ) : null}
                <Button size="sm" variant="outline" onClick={() => void share(build)}>
                  {build.shareId ? "Copy share link" : "Share"}
                </Button>
                {build.shareId ? (
                  <Button size="sm" variant="ghost" onClick={() => void revoke(build)}>Revoke</Button>
                ) : null}
                {confirmingDelete ? (
                  <span className="inline-flex flex-wrap items-center gap-2">
                    <span className="text-xs text-muted">Delete permanently?</span>
                    <Button
                      size="sm"
                      variant="outline"
                      autoFocus
                      aria-label={`Confirm delete of ${build.name}`}
                      className="border-danger/45 text-danger hover:border-danger hover:bg-danger/10 hover:text-danger"
                      onClick={() => void remove(build)}
                    >
                      Confirm delete
                    </Button>
                    <Button size="sm" variant="ghost" onClick={() => setConfirmingDeleteId(null)}>
                      Keep
                    </Button>
                  </span>
                ) : (
                  <Button
                    size="sm"
                    variant="ghost"
                    aria-label={`Delete ${build.name}`}
                    onClick={() => setConfirmingDeleteId(build.id)}
                  >
                    Delete
                  </Button>
                )}
              </div>
            </div>
            {repairing ? (
              <BuildRepairPanel
                id={`repair-${build.id}`}
                build={build}
                onCancel={() => setRepairingId(null)}
                onRepaired={(updated) => {
                  setBuilds((current) =>
                    current.map((candidate) => (candidate.id === updated.id ? updated : candidate))
                  );
                  setRepairingId(null);
                  setStatus(`“${updated.name}” was repaired with your explicit choices.`);
                }}
              />
            ) : null}
          </Card>
        );
      })}
    </section>
  );
}

type RepairDraft = { action: "DROP" | "REPLACE"; replacementItemId?: number };

const CHOICE_OPTIONS = [
  { value: "DROP", label: "Drop" },
  { value: "REPLACE", label: "Replace" }
];

// Item names are only needed once a repair panel is open, so the map is fetched on demand and kept
// for the rest of the session.
let itemNameCache: Record<string, string> | null = null;

function BuildRepairPanel({
  id,
  build,
  onCancel,
  onRepaired
}: {
  id: string;
  build: SavedBuild;
  onCancel: () => void;
  onRepaired: (build: SavedBuild) => void;
}) {
  const unavailable = build.unavailableItems ?? [];
  const [itemNames, setItemNames] = useState<Record<string, string> | null>(itemNameCache);
  const [drafts, setDrafts] = useState<Record<number, RepairDraft>>({});
  const [queries, setQueries] = useState<Record<number, string>>({});
  const [pending, setPending] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (itemNames) return;
    let cancelled = false;
    void fetch("/api/static/items", { cache: "force-cache" })
      .then(async (response) => {
        if (!response.ok) throw new Error("Item names are unavailable.");
        const payload = (await response.json()) as ItemMap;
        const names = Object.fromEntries(
          Object.entries(payload.items).map(([itemId, item]) => [itemId, item.name])
        );
        itemNameCache = names;
        if (!cancelled) setItemNames(names);
      })
      .catch(() => undefined);
    return () => {
      cancelled = true;
    };
  }, [itemNames]);

  function itemLabel(itemId: number) {
    return itemNames?.[String(itemId)] ?? `Item ${itemId}`;
  }

  const choices = useMemo<SavedBuildRepairChoice[]>(
    () =>
      // Drafts are keyed by an item the server itself reported as unavailable, so every choice
      // targets a real unavailable selection. An incomplete REPLACE is omitted, never downgraded
      // to a silent drop.
      Object.entries(drafts).flatMap<SavedBuildRepairChoice>(([itemId, draft]) => {
        if (draft.action === "DROP") return [{ itemId: Number(itemId), action: "DROP" }];
        return draft.replacementItemId
          ? [
              {
                itemId: Number(itemId),
                action: "REPLACE",
                replacementItemId: draft.replacementItemId
              }
            ]
          : [];
      }),
    [drafts]
  );

  const awaitingReplacement = Object.values(drafts).some(
    (draft) => draft.action === "REPLACE" && !draft.replacementItemId
  );

  async function apply() {
    setPending(true);
    setError(null);
    let repaired: SavedBuild | null = null;
    try {
      const response = await fetch(
        `/api/trn/user/users/me/lol/saved-builds/${build.id}/repair`,
        {
          method: "POST",
          headers: { "content-type": "application/json" },
          body: JSON.stringify({ choices })
        }
      );
      if (response.ok) {
        repaired = (await response.json()) as SavedBuild;
      } else {
        // The server validates every choice (unknown item, unbuyable replacement); surface its
        // reason rather than a generic failure.
        const problem = (await response.json().catch(() => null)) as { detail?: string } | null;
        setError(problem?.detail?.trim() || "The repair could not be applied.");
      }
    } catch {
      setError("The repair could not be applied.");
    } finally {
      setPending(false);
    }
    if (repaired) onRepaired(repaired);
  }

  return (
    <section
      id={id}
      aria-label={`Repair unavailable selections in ${build.name}`}
      className="mt-4 grid gap-4 border-t border-border/45 pt-4"
    >
      <div className="grid gap-1">
        <h3 className="type-ui font-semibold text-fg">Unavailable selections</h3>
        <p className="type-caption text-muted">
          Pick an outcome for each one. A retired item is never replaced for you.
        </p>
      </div>

      {unavailable.map((item) => {
        const draft = drafts[item.itemId];
        const query = queries[item.itemId] ?? "";
        const matches =
          draft?.action === "REPLACE" && itemNames && query.trim().length > 1
            ? Object.entries(itemNames)
                .map(([itemId, name]) => ({
                  itemId: Number(itemId),
                  name,
                  score: searchMatchScore(name, query) ?? Number.POSITIVE_INFINITY
                }))
                .filter((match) => Number.isFinite(match.score) && match.itemId !== item.itemId)
                .sort((a, b) => a.score - b.score || a.name.localeCompare(b.name))
                .slice(0, 6)
            : [];

        return (
          <div key={item.itemId} className="grid gap-3 rounded-card border border-border/55 bg-surface-2/25 p-3">
            <div className="flex flex-wrap items-center justify-between gap-2">
              <div className="min-w-0">
                <p className="type-ui font-medium text-fg">{itemLabel(item.itemId)}</p>
                <p className="type-caption text-muted">
                  {reasonLabel(item.reason)} · <span className="type-tabular">id {item.itemId}</span>
                </p>
              </div>
              <SegmentedControl
                options={CHOICE_OPTIONS}
                value={draft?.action ?? ""}
                ariaLabel={`Repair choice for ${itemLabel(item.itemId)}`}
                onValueChange={(value) =>
                  setDrafts((current) => ({
                    ...current,
                    [item.itemId]:
                      value === "REPLACE"
                        ? { action: "REPLACE", replacementItemId: current[item.itemId]?.replacementItemId }
                        : { action: "DROP" }
                  }))
                }
              />
            </div>

            {draft?.action === "REPLACE" ? (
              <div className="grid gap-2 border-t border-border/40 pt-3">
                <label className="grid gap-1.5">
                  <span className="field-label">Replacement item</span>
                  <Input
                    value={query}
                    onChange={(event) =>
                      setQueries((current) => ({ ...current, [item.itemId]: event.target.value }))
                    }
                    placeholder="Search items on the active patch"
                    className="h-11"
                  />
                </label>
                {draft.replacementItemId ? (
                  <p className="type-caption text-fg/75">
                    Replacing with <span className="font-semibold">{itemLabel(draft.replacementItemId)}</span>
                  </p>
                ) : (
                  <p className="type-caption text-muted">
                    {itemNames
                      ? "Type at least two characters, then choose a replacement."
                      : "Loading item names…"}
                  </p>
                )}
                {matches.length > 0 ? (
                  <ul className="grid gap-1">
                    {matches.map((match) => (
                      <li key={match.itemId}>
                        <button
                          type="button"
                          aria-pressed={draft.replacementItemId === match.itemId}
                          onClick={() =>
                            setDrafts((current) => ({
                              ...current,
                              [item.itemId]: { action: "REPLACE", replacementItemId: match.itemId }
                            }))
                          }
                          className="flex w-full items-center justify-between gap-2 rounded-control border border-border/60 bg-surface px-3 py-2 text-left text-sm text-fg/85 transition-colors duration-150 hover:border-border-strong hover:bg-surface-2/60 hover:text-fg focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40 focus-visible:ring-offset-2 focus-visible:ring-offset-bg aria-pressed:border-primary/45 aria-pressed:text-fg"
                        >
                          <span className="min-w-0 truncate">{match.name}</span>
                          <span className="type-tabular shrink-0 text-xs text-muted">{match.itemId}</span>
                        </button>
                      </li>
                    ))}
                  </ul>
                ) : null}
              </div>
            ) : null}
          </div>
        );
      })}

      {error ? (
        <p role="alert" className="rounded-card border border-danger/35 bg-danger/10 p-3 text-sm text-danger">
          {error}
        </p>
      ) : null}

      <div className="flex flex-wrap items-center gap-2">
        <Button
          size="sm"
          onClick={() => void apply()}
          disabled={pending || choices.length === 0 || awaitingReplacement}
        >
          {pending
            ? "Applying…"
            : choices.length === 0
              ? "Apply repair"
              : `Apply ${choices.length} repair${choices.length === 1 ? "" : "s"}`}
        </Button>
        <Button size="sm" variant="ghost" onClick={onCancel} disabled={pending}>
          Cancel
        </Button>
        {awaitingReplacement ? (
          <span className="type-caption text-muted">Choose a replacement item to continue.</span>
        ) : null}
      </div>
    </section>
  );
}
