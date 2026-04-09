"use client";

import Image from "next/image";
import Link from "next/link";
import { useState } from "react";

import { Card } from "@/components/ui/Card";
import { Input } from "@/components/ui/Input";
import {
  buildTftEntityMap,
  formatTftDescription,
  formatTftEntityName,
  getTftCompositionEntities,
  isTftCraftableItem,
  tftIconObjectClass,
  tftIconUrl,
  type TftStaticEntity
} from "@/lib/tft";

function TftItemRecipeRow({
  components,
  compact = false
}: {
  components: TftStaticEntity[];
  compact?: boolean;
}) {
  return (
    <div className={`flex items-center gap-2 ${compact ? "" : "flex-wrap"}`}>
      {components.map((component, index) => {
        const iconSrc = tftIconUrl(component.icon);
        const label = formatTftEntityName(component);
        return (
          <div
            key={`${component.apiName}-${index}`}
            className="inline-flex items-center gap-2 rounded-full border border-border/50 bg-surface/78 px-2.5 py-1.5 text-xs text-fg/78 shadow-inset"
          >
            {iconSrc ? (
              <Image
                src={iconSrc}
                alt={label}
                width={20}
                height={20}
                className={`h-5 w-5 ${tftIconObjectClass(component.icon)}`}
                sizes="20px"
              />
            ) : (
              <span className="flex h-5 w-5 items-center justify-center rounded-full bg-primary/12 text-[10px] font-bold text-primary">
                {label.charAt(0)}
              </span>
            )}
            <span className="truncate">{label}</span>
          </div>
        );
      })}
    </div>
  );
}

export function TftItemsGrid({ items }: { items: TftStaticEntity[] }) {
  const [search, setSearch] = useState("");
  const entityMap = buildTftEntityMap(items);
  const craftableItems = items
    .filter(isTftCraftableItem)
    .sort((left, right) => formatTftEntityName(left).localeCompare(formatTftEntityName(right)));

  const normalizedSearch = search.trim().toLowerCase();
  const filtered = normalizedSearch
    ? craftableItems.filter((item) => {
        const recipeComponents = getTftCompositionEntities(item, entityMap);
        const searchableText = [
          formatTftEntityName(item),
          formatTftDescription(item.description) ?? "",
          ...recipeComponents.map((component) => formatTftEntityName(component))
        ]
          .join(" ")
          .toLowerCase();
        return searchableText.includes(normalizedSearch);
      })
    : craftableItems;

  return (
    <div className="grid gap-4">
      <Input
        type="search"
        placeholder="Search craftable items..."
        value={search}
        onChange={(event) => setSearch(event.target.value)}
        className="max-w-sm"
      />
      <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-3">
        {filtered.map((item) => {
          const iconSrc = tftIconUrl(item.icon);
          const displayName = formatTftEntityName(item);
          const displayDescription = formatTftDescription(item.description);
          const recipeComponents = getTftCompositionEntities(item, entityMap);

          return (
            <Link key={item.apiName} href={`/tft/items/${item.apiName}`} className="group relative block">
              <Card className="flex h-full min-h-[188px] flex-col gap-3 p-4 transition hover:bg-white/8">
                <div className="flex items-start gap-3">
                  {iconSrc ? (
                    <div className="flex h-12 w-12 shrink-0 items-center justify-center overflow-hidden rounded-xl border border-border/50 bg-surface/60">
                      <Image
                        src={iconSrc}
                        alt={displayName}
                        width={48}
                        height={48}
                        className={`h-12 w-12 ${tftIconObjectClass(item.icon)}`}
                        sizes="48px"
                      />
                    </div>
                  ) : (
                    <div className="flex h-12 w-12 shrink-0 items-center justify-center rounded-xl border border-border/50 bg-primary/10 text-sm font-bold text-primary">
                      {displayName.charAt(0)}
                    </div>
                  )}
                  <div className="min-w-0">
                    <p className="truncate text-sm font-semibold text-fg group-hover:underline">{displayName}</p>
                    {displayDescription && (
                      <p className="mt-1 line-clamp-3 text-xs text-fg/60">{displayDescription}</p>
                    )}
                  </div>
                </div>

                <div className="mt-auto grid gap-2 md:hidden">
                  <p className="type-kicker text-[10px] text-fg/45">Recipe</p>
                  <TftItemRecipeRow components={recipeComponents} compact />
                </div>
              </Card>

              <div className="pointer-events-none absolute inset-x-4 bottom-4 z-10 hidden translate-y-2 opacity-0 transition duration-200 ease-[cubic-bezier(0.25,1,0.5,1)] md:block group-hover:translate-y-0 group-hover:opacity-100 group-focus-visible:translate-y-0 group-focus-visible:opacity-100">
                <div className="rounded-2xl border border-primary/24 bg-bg/96 p-3 shadow-card backdrop-blur">
                  <p className="type-kicker text-[10px] text-fg/45">Build With</p>
                  <div className="mt-2">
                    <TftItemRecipeRow components={recipeComponents} />
                  </div>
                </div>
              </div>
            </Link>
          );
        })}
        {filtered.length === 0 && (
          <p className="col-span-full text-sm text-fg/60">No craftable items matched &quot;{search}&quot;</p>
        )}
      </div>
    </div>
  );
}
