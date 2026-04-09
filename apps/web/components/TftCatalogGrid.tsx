"use client";

import Image from "next/image";
import Link from "next/link";
import { useState } from "react";

import { Card } from "@/components/ui/Card";
import { Input } from "@/components/ui/Input";
import {
  formatTftDescription,
  formatTftEntityName,
  tftIconObjectClass,
  tftIconUrl,
  type TftStaticEntity
} from "@/lib/tft";

export function TftCatalogGrid({
  items,
  basePath,
  columns = "md:grid-cols-2 xl:grid-cols-3"
}: {
  items: TftStaticEntity[];
  basePath: string;
  columns?: string;
}) {
  const [search, setSearch] = useState("");
  const normalizedSearch = search.trim().toLowerCase();

  const filtered = normalizedSearch
    ? items.filter((item) => formatTftEntityName(item).toLowerCase().includes(normalizedSearch))
    : items;

  return (
    <div className="grid gap-4">
      <Input
        type="search"
        placeholder="Search by name..."
        value={search}
        onChange={(e) => setSearch(e.target.value)}
        className="max-w-sm"
      />
      <div className={`grid gap-3 ${columns}`}>
        {filtered.map((item) => {
          const iconSrc = tftIconUrl(item.icon);
          const displayName = formatTftEntityName(item);
          const displayDescription = formatTftDescription(item.description);
          return (
            <Link key={item.apiName} href={`${basePath}/${item.apiName}`}>
              <Card className="flex items-center gap-3 p-4 transition hover:bg-white/8">
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
                  <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-lg border border-border/50 bg-primary/10 text-sm font-bold text-primary">
                    {displayName.charAt(0)}
                  </div>
                )}
                <div className="min-w-0">
                  <p className="truncate text-sm font-semibold text-fg">{displayName}</p>
                  {displayDescription && (
                    <p className="mt-0.5 line-clamp-2 text-xs text-fg/60">{displayDescription}</p>
                  )}
                </div>
              </Card>
            </Link>
          );
        })}
        {filtered.length === 0 && (
          <p className="col-span-full text-sm text-fg/60">No results for &quot;{search}&quot;</p>
        )}
      </div>
    </div>
  );
}
