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
  const [gameName, setGameName] = useState("");
  const [tag, setTag] = useState("");
  const [error, setError] = useState<string | null>(null);

  const hints = useMemo(
    () => ({
      gameName: "Game name (e.g., Faker)",
      tag: "Tag (e.g., KR1)"
    }),
    []
  );

  function onSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);

    // Convenience: allow paste of "GameName#TAG" into the game name field.
    const hasHash = gameName.includes("#");
    const pasted = hasHash ? parseRiotIdInput(gameName) : null;
    if (hasHash && !pasted) {
      setError("Enter a game name and tag (or paste GameName#TAG).");
      return;
    }
    const riotId = pasted ?? { gameName: gameName.trim(), tagLine: tag.trim() };

    if (!riotId.gameName || !riotId.tagLine) {
      setError("Enter a game name and tag (or paste GameName#TAG).");
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
          value={gameName}
          onChange={(e) => setGameName(e.target.value)}
          placeholder={hints.gameName}
          aria-label="Game name"
          className="w-full sm:w-[300px]"
          autoCorrect="off"
          autoCapitalize="off"
          spellCheck={false}
        />

        <Input
          value={tag}
          onChange={(e) => setTag(e.target.value)}
          placeholder={hints.tag}
          aria-label="Tag"
          className="w-[120px] sm:w-[120px]"
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

