import Image from "next/image";
import Link from "next/link";
import type { Metadata } from "next";
import { headers } from "next/headers";
import { notFound } from "next/navigation";

import { BackendErrorCard } from "@/components/BackendErrorCard";
import { Card } from "@/components/ui/Card";
import { buttonClassName } from "@/components/ui/buttonStyles";
import { analyticsFeatureFlags } from "@/lib/analyticsFeatureFlags";
import { fetchBackendJson } from "@/lib/backendCall";
import { resolveClientIp } from "@/lib/clientIp";
import {
  buildLabPermalink,
  buildLabRegionLabel,
  humanizeToken,
  type BuildLabMode,
  type BuildLabRole,
  type PublicSavedBuild
} from "@/lib/buildLab";
import { getBackendBaseUrl } from "@/lib/env";
import {
  fetchItemMap,
  fetchRunesReforged,
  fetchSummonerSpellMap,
  itemIconUrl,
  runeIconUrl,
  summonerSpellIconUrl
} from "@/lib/staticData";

// A share link is a capability token: revoking it must actually un-publish the page, so it is
// never indexed and never cached. `app/robots.ts` disallows the route as well.
export const metadata: Metadata = {
  title: "Shared build",
  robots: { index: false, follow: false }
};

export default async function SharedBuildPage({
  params
}: {
  params: Promise<{ shareId: string }>;
}) {
  if (!(await analyticsFeatureFlags()).buildLab) notFound();
  const { shareId } = await params;
  // The share token is the only credential this page carries, so the anonymous lookup must stay
  // metered. The backend partitions its read limiter on the client address and exempts internal
  // sources, so a server-side fetch that forwards nothing arrives unmetered and every guess made
  // through this page is free. Forward the identity our edge vouches for — the same value the BFF
  // proxy sends — so the per-IP budget applies on the path the product actually uses.
  const clientIp = resolveClientIp(await headers());
  const [result, itemStatic, runeStatic, spellStatic] = await Promise.all([
    fetchBackendJson<PublicSavedBuild>(
      `${getBackendBaseUrl()}/api/lol/saved-builds/${encodeURIComponent(shareId)}`,
      {
        cache: "no-store",
        ...(clientIp ? { headers: { "x-forwarded-for": clientIp } } : null)
      }
    ),
    fetchItemMap(),
    fetchRunesReforged(),
    fetchSummonerSpellMap()
  ]);
  if (!result.ok || !result.body) {
    return (
      <BackendErrorCard
        title="Shared build"
        message="This link is invalid, expired, or has been revoked."
      />
    );
  }

  const build = result.body;
  const spellIds = [build.spell1Id, build.spell2Id].filter(
    (value): value is number => typeof value === "number"
  );
  const href = buildLabPermalink(build.championId, {
    role: build.role as BuildLabRole,
    opponentChampionId: build.opponentChampionId ?? undefined,
    patch: build.patch || undefined,
    region: build.region || undefined,
    mode: build.rankingMode.toLowerCase() as BuildLabMode,
    section: "items",
    itemPath: build.itemPath,
    runeSelections: build.runeSelections,
    spellPair: spellIds
  });
  const unavailableItems = build.unavailableItems ?? [];
  const unavailable = new Set(unavailableItems.map((entry) => entry.itemId));

  return (
    <div className="mx-auto grid max-w-3xl gap-5">
      <header className="border-b border-border/60 pb-5">
        <p className="type-kicker text-primary">Shared Build Lab configuration</p>
        <h1 className="type-page-title mt-2">{build.name}</h1>
        <p className="mt-2 text-sm text-muted">
          {build.role} · {buildLabRegionLabel(build.region)} · saved on patch {build.patch}
        </p>
      </header>
      <Card className="p-5">
        <dl className="grid gap-4 sm:grid-cols-3">
          <div className="min-w-0">
            <dt className="type-kicker text-muted">Item path</dt>
            <dd className="mt-1.5 flex flex-wrap items-center gap-1.5">
              {build.itemPath.length === 0 ? (
                <span className="text-sm text-muted">Not selected</span>
              ) : (
                build.itemPath.map((itemId, index) => (
                  <span
                    key={`${itemId}-${index}`}
                    className="inline-flex items-center gap-1"
                    title={itemStatic.items[String(itemId)]?.name ?? `Item ${itemId}`}
                  >
                    <Image
                      src={itemIconUrl(itemStatic.version, itemId)}
                      alt={itemStatic.items[String(itemId)]?.name ?? `Item ${itemId}`}
                      width={28}
                      height={28}
                      className={
                        unavailable.has(itemId)
                          ? "size-7 rounded-control border border-danger/50 opacity-60"
                          : "size-7 rounded-control border border-border/55"
                      }
                    />
                  </span>
                ))
              )}
            </dd>
          </div>
          <div className="min-w-0">
            <dt className="type-kicker text-muted">Runes</dt>
            <dd className="mt-1.5 flex flex-wrap items-center gap-1.5">
              {build.runeSelections.length === 0 ? (
                <span className="text-sm text-muted">Not selected</span>
              ) : (
                build.runeSelections.map((runeId, index) => (
                  <Image
                    key={`${runeId}-${index}`}
                    src={runeIconUrl(runeStatic.runeById[String(runeId)]?.icon ?? "")}
                    alt={runeStatic.runeById[String(runeId)]?.name ?? `Rune ${runeId}`}
                    title={runeStatic.runeById[String(runeId)]?.name ?? `Rune ${runeId}`}
                    width={26}
                    height={26}
                    className="size-[26px] rounded-full border border-border/55 bg-surface-2 p-0.5"
                  />
                ))
              )}
            </dd>
          </div>
          <div className="min-w-0">
            <dt className="type-kicker text-muted">Spells</dt>
            <dd className="mt-1.5 flex flex-wrap items-center gap-1.5">
              {spellIds.length === 0 ? (
                <span className="text-sm text-muted">Not selected</span>
              ) : (
                spellIds.map((spellId) => (
                  <Image
                    key={spellId}
                    src={summonerSpellIconUrl(
                      spellStatic.version,
                      spellStatic.spells[String(spellId)]?.id ?? ""
                    )}
                    alt={spellStatic.spells[String(spellId)]?.name ?? `Spell ${spellId}`}
                    title={spellStatic.spells[String(spellId)]?.name ?? `Spell ${spellId}`}
                    width={28}
                    height={28}
                    className="size-7 rounded-control border border-border/55"
                  />
                ))
              )}
            </dd>
          </div>
        </dl>
        {build.analyticsChanged ? (
          <p className="mt-5 rounded-control border border-warning/35 bg-warning/10 px-3 py-2 text-sm text-warning">
            The promoted analytics generation has changed since this build was saved. Opening it
            re-estimates every value against the current generation.
          </p>
        ) : null}
        {unavailableItems.length > 0 ? (
          <div className="mt-3 rounded-control border border-danger/35 bg-danger/10 px-3 py-2 text-sm text-danger">
            {/* Availability is resolved against the live active patch, never the patch the build
                was saved on — naming the saved patch here would claim an item is missing from the
                one patch it is known to have existed on. */}
            <p>This setup contains items that cannot be built on the current active patch:</p>
            <ul className="mt-1 grid gap-0.5">
              {unavailableItems.map((entry) => (
                <li key={entry.itemId}>
                  {itemStatic.items[String(entry.itemId)]?.name ?? `Item ${entry.itemId}`} ·{" "}
                  {humanizeToken(entry.reason)}
                </li>
              ))}
            </ul>
            <p className="mt-1">Build Lab marks them incompatible and replaces nothing on its own.</p>
          </div>
        ) : build.compatibilityStatus !== "CURRENT" ? (
          <p className="mt-3 rounded-control border border-border/60 bg-surface-2 px-3 py-2 text-sm text-fg/78">
            {build.compatibilityStatus === "PATCH_CHANGED"
              ? `This build was saved on patch ${build.patch}; the promoted generation models a newer one.`
              : "This build has no source generation, so its saved estimates cannot be compared with the current ones."}
          </p>
        ) : null}
        <div className="mt-5 flex flex-wrap items-center gap-3">
          <Link href={href} className={buttonClassName({ size: "sm" })}>
            Open and clone in Build Lab
          </Link>
          <span className="text-xs text-muted">Sign in only when you choose to save your clone.</span>
        </div>
      </Card>
    </div>
  );
}
