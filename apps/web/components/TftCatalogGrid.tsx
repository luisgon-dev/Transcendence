"use client";

import Image from "next/image";
import Link from "next/link";
import { useState } from "react";

import { Card } from "@/components/ui/Card";
import { Input } from "@/components/ui/Input";
import { tftIconUrl, type TftStaticEntity } from "@/lib/tft";

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

  const filtered = search.trim()
    ? items.filter((item) => item.name.toLowerCase().includes(search.trim().toLowerCase()))
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
          return (
            <Link key={item.apiName} href={`${basePath}/${item.apiName}`}>
              <Card className="flex items-center gap-3 p-4 transition hover:bg-white/8">
                {iconSrc ? (
                  <Image
                    src={iconSrc}
                    alt={item.name}
                    width={40}
                    height={40}
                    className="rounded-lg"
                    unoptimized
                  />
                ) : (
                  <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-lg border border-border/50 bg-primary/10 text-sm font-bold text-primary">
                    {item.name.charAt(0)}
                  </div>
                )}
                <div className="min-w-0">
                  <p className="truncate text-sm font-semibold text-fg">{item.name}</p>
                  {item.description && (
                    <p className="mt-0.5 line-clamp-1 text-xs text-fg/60">{item.description}</p>
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
