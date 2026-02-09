"use client";

import { useMemo, useState } from "react";
import { useRouter } from "next/navigation";

import { Button } from "@/components/ui/Button";
import { Input } from "@/components/ui/Input";
import { cn } from "@/lib/cn";
import { encodeRiotIdPath, parseRiotIdInput } from "@/lib/riotid";

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
];

export function SearchBar({ className }: { className?: string }) {
  const router = useRouter();
  const [region, setRegion] = useState("na");
  const [query, setQuery] = useState("");
  const [error, setError] = useState<string | null>(null);

  const hint = useMemo(() => "Riot ID (e.g., Faker#KR1)", []);

  function onSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);

    const riotId = parseRiotIdInput(query);
    if (!riotId) {
      setError("Enter a Riot ID like GameName#TAG.");
      return;
    }

    const riotIdPath = encodeRiotIdPath(riotId);
    router.push(`/summoners/${region}/${riotIdPath}`);
  }

  return (
    <form
      onSubmit={onSubmit}
      className={cn(
        "flex w-full flex-col gap-2 sm:flex-row sm:items-center",
        className
      )}
    >
      <div className="flex gap-2">
        <select
          className="h-11 min-w-[92px] rounded-md border border-border/70 bg-surface/35 px-3 text-sm text-fg shadow-glass outline-none focus:border-primary/70 focus:ring-2 focus:ring-primary/25"
          value={region}
          onChange={(e) => setRegion(e.target.value)}
          aria-label="Region"
        >
          {REGIONS.map((r) => (
            <option key={r.value} value={r.value}>
              {r.label}
            </option>
          ))}
        </select>

        <Input
          value={query}
          onChange={(e) => setQuery(e.target.value)}
          placeholder={hint}
          className="w-full sm:w-[360px]"
          autoCorrect="off"
          autoCapitalize="off"
          spellCheck={false}
        />
      </div>

      <div className="flex items-center gap-3">
        <Button type="submit">Search</Button>
        {error ? <p className="text-sm text-red-300">{error}</p> : null}
      </div>
    </form>
  );
}

