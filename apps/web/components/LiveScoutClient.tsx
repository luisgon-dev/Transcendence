"use client";

import { FormEvent, useState } from "react";

import { LiveGameCard } from "@/components/LiveGameCard";
import { Button } from "@/components/ui/Button";
import { Card } from "@/components/ui/Card";
import { Select } from "@/components/ui/Select";
import { LOL_REGION_OPTIONS } from "@/lib/lolRegions";
import { parseRiotIdInput, type RiotId } from "@/lib/riotid";

type ScoutTarget = RiotId & { region: string };

export function LiveScoutClient() {
  const [region, setRegion] = useState("na");
  const [riotId, setRiotId] = useState("");
  const [target, setTarget] = useState<ScoutTarget | null>(null);
  const [error, setError] = useState<string | null>(null);

  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const parsed = parseRiotIdInput(riotId);
    if (!parsed) {
      setError("Enter a Riot ID in GameName#TAG format.");
      return;
    }

    setError(null);
    setTarget({ region, ...parsed });
  }

  return (
    <div className="grid gap-6">
      <header className="page-hero grid gap-5 p-5 md:grid-cols-[minmax(0,1fr)_minmax(300px,0.72fr)] md:p-7">
        <div className="max-w-2xl">
          <p className="type-kicker text-primary">Live game scout</p>
          <h1 className="mt-3 type-page-title">Read both teams while the match is live.</h1>
          <p className="mt-3 max-w-[64ch] text-fg/72">
            Check a Riot ID for an active game, then compare ladder rank, recent form, streaks,
            champion pools, summoner spells, and selected runes. Active games refresh every minute.
          </p>
        </div>
        <div className="surface-subtle self-end rounded-card px-4 py-3">
          <p className="type-kicker text-muted">Freshness contract</p>
          <p className="mt-2 text-sm leading-6 text-fg/78">
            Results come from the latest worker observation. The checked time and data age stay
            visible so a stale snapshot never looks live.
          </p>
        </div>
      </header>

      <Card className="p-5 md:p-6">
        <form onSubmit={submit} className="grid gap-4 md:grid-cols-[minmax(0,1fr)_150px_auto] md:items-end">
          <div>
            <label htmlFor="live-scout-riot-id" className="type-kicker text-fg/70">
              Riot ID
            </label>
            <input
              id="live-scout-riot-id"
              value={riotId}
              onChange={(event) => setRiotId(event.target.value)}
              placeholder="Kronic#NA1"
              autoComplete="off"
              autoCapitalize="off"
              spellCheck={false}
              className="mt-2 h-12 w-full rounded-control border border-border bg-surface px-4 text-sm text-fg shadow-inset outline-none transition placeholder:text-muted/65 focus:border-primary/60 focus:ring-2 focus:ring-primary/25"
            />
          </div>
          <div>
            <label className="type-kicker text-fg/70">Region</label>
            <Select
              value={region}
              onValueChange={setRegion}
              options={[...LOL_REGION_OPTIONS]}
              ariaLabel="Live game region"
              className="mt-2 h-12 w-full"
            />
          </div>
          <Button type="submit" className="h-12 px-6">
            Scout game
          </Button>
          {error ? (
            <p role="alert" className="text-sm text-danger md:col-span-3">
              {error}
            </p>
          ) : null}
        </form>
      </Card>

      {target ? (
        <section aria-label={`Live game for ${target.gameName}#${target.tagLine}`}>
          <LiveGameCard
            key={`${target.region}:${target.gameName}:${target.tagLine}`}
            region={target.region}
            gameName={target.gameName}
            tagLine={target.tagLine}
            detailed
          />
        </section>
      ) : (
        <div className="rounded-card border border-dashed border-border px-6 py-10 text-center">
          <p className="type-section">The live matchup appears here</p>
          <p className="mx-auto mt-2 max-w-xl text-sm leading-6 text-muted">
            Search any tracked player. If they are in game, both teams are grouped by side with
            clearly labeled stored-data signals—not hidden certainty scores.
          </p>
        </div>
      )}
    </div>
  );
}
