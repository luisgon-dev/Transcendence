"use client";

import { useCallback, useEffect, useState } from "react";

import { Card } from "@/components/ui/Card";
import { Button } from "@/components/ui/Button";
import { formatTftTime, type TftAcceptedResponse, type TftSummonerProfile } from "@/lib/tft";

type TftSummonerPayload =
  | { kind: "profile"; profile: TftSummonerProfile }
  | { kind: "accepted"; accepted: TftAcceptedResponse };

export function TftSummonerProfileClient({
  region,
  gameName,
  tagLine,
  initialPayload
}: {
  region: string;
  gameName: string;
  tagLine: string;
  initialPayload: TftSummonerPayload;
}) {
  const [payload, setPayload] = useState<TftSummonerPayload>(initialPayload);
  const [refreshing, setRefreshing] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const loadProfile = useCallback(async () => {
    const res = await fetch(
      `/api/trn/public/tft/summoners/${encodeURIComponent(region)}/${encodeURIComponent(gameName)}/${encodeURIComponent(tagLine)}`,
      { cache: "no-store" }
    );

    const json = (await res.json().catch(() => null)) as TftSummonerProfile | TftAcceptedResponse | null;
    if (res.status === 202 && json) {
      setPayload({ kind: "accepted", accepted: json as TftAcceptedResponse });
      return;
    }

    if (!res.ok || !json) {
      setError(`Failed to load TFT summoner (${res.status}).`);
      return;
    }

    setPayload({ kind: "profile", profile: json as TftSummonerProfile });
  }, [region, gameName, tagLine]);

  useEffect(() => {
    if (payload.kind !== "accepted") return;
    const retryAfterSeconds = Math.max(3, payload.accepted.retryAfterSeconds ?? 5);
    const timeoutId = window.setTimeout(() => {
      void loadProfile();
    }, retryAfterSeconds * 1000);

    return () => window.clearTimeout(timeoutId);
  }, [payload, region, gameName, tagLine, loadProfile]);

  async function queueRefresh() {
    setRefreshing(true);
    setError(null);

    try {
      const res = await fetch(
        `/api/trn/public/tft/summoners/${encodeURIComponent(region)}/${encodeURIComponent(gameName)}/${encodeURIComponent(tagLine)}/refresh`,
        { method: "POST" }
      );
      const json = (await res.json().catch(() => null)) as TftAcceptedResponse | null;
      if (!res.ok || !json) {
        setError(`Failed to queue refresh (${res.status}).`);
        return;
      }

      setPayload({ kind: "accepted", accepted: json });
    } finally {
      setRefreshing(false);
    }
  }

  if (payload.kind === "accepted") {
    return (
      <Card className="grid gap-4 p-6">
        <div>
          <h1 className="font-[var(--font-sora)] text-3xl font-semibold tracking-tight">
            {gameName}#{tagLine}
          </h1>
          <p className="mt-2 text-sm text-fg/75">
            {payload.accepted.message ?? "TFT profile is not ready yet."}
          </p>
        </div>
        <div className="flex flex-wrap items-center gap-3">
          <Button type="button" onClick={queueRefresh} disabled={refreshing}>
            {refreshing ? "Queueing..." : "Queue Refresh"}
          </Button>
          <Button type="button" variant="outline" onClick={() => void loadProfile()}>
            Check Again
          </Button>
        </div>
        {error ? <p className="text-sm text-danger">{error}</p> : null}
      </Card>
    );
  }

  const profile = payload.profile;

  return (
    <div className="grid gap-4">
      <Card className="grid gap-3 p-6">
        <div className="flex flex-wrap items-start justify-between gap-3">
          <div>
            <h1 className="font-[var(--font-sora)] text-3xl font-semibold tracking-tight">
              {profile.gameName}#{profile.tagLine}
            </h1>
            <p className="mt-1 text-sm text-fg/75">
              {profile.platformRegion} · Level {profile.summonerLevel.toLocaleString()} · Updated{" "}
              {new Date(profile.updatedAtUtc).toLocaleString()}
            </p>
          </div>
          <Button type="button" variant="outline" onClick={queueRefresh} disabled={refreshing}>
            {refreshing ? "Queueing..." : "Refresh"}
          </Button>
        </div>

        <div className="grid gap-3 md:grid-cols-3">
          {profile.ranks.length === 0 ? (
            <p className="text-sm text-muted">No TFT ranked entries stored yet.</p>
          ) : (
            profile.ranks.map((rank) => (
              <Card key={`${rank.queueType}-${rank.tier}`} className="p-4">
                <p className="text-xs uppercase tracking-[0.2em] text-primary">{rank.queueType}</p>
                <p className="mt-2 text-lg font-semibold text-fg">
                  {rank.tier} {rank.rankNumber}
                </p>
                <p className="mt-1 text-sm text-fg/80">{rank.leaguePoints} LP</p>
                <p className="mt-1 text-xs text-muted">
                  {rank.wins}W {rank.losses}L
                </p>
              </Card>
            ))
          )}
        </div>
      </Card>

      <Card className="p-6">
        <div className="flex items-center justify-between gap-3">
          <div>
            <h2 className="font-[var(--font-sora)] text-xl font-semibold">Recent Matches</h2>
            <p className="text-sm text-fg/75">Stored TFT match history for this profile.</p>
          </div>
        </div>

        <div className="mt-4 grid gap-3">
          {profile.recentMatches.length === 0 ? (
            <p className="text-sm text-muted">No stored TFT matches yet.</p>
          ) : (
            profile.recentMatches.map((match) => (
              <Card key={match.matchId} className="grid gap-3 p-4">
                <div className="flex flex-wrap items-center justify-between gap-2">
                  <div>
                    <p className="text-sm font-semibold text-fg">Placement #{match.placement}</p>
                    <p className="text-xs text-muted">
                      {match.setCoreName ?? `Set ${match.setNumber ?? "?"}`} · {match.patch ?? "Unknown patch"} · {formatTftTime(match.matchDate)}
                    </p>
                  </div>
                  <div className="text-right text-xs text-muted">
                    <p>Level {match.level}</p>
                    <p>{match.playersEliminated} eliminations</p>
                  </div>
                </div>

                <div className="flex flex-wrap gap-2">
                  {match.traits.map((trait) => (
                    <span key={`${match.matchId}-${trait.name}`} className="rounded-full border border-border/60 px-2.5 py-1 text-xs text-fg/80">
                      {trait.name} {trait.numUnits}
                    </span>
                  ))}
                </div>

                <div className="flex flex-wrap gap-2">
                  {match.units.map((unit) => (
                    <span key={`${match.matchId}-${unit.characterId}`} className="rounded-full border border-primary/30 bg-primary/10 px-2.5 py-1 text-xs text-primary">
                      {unit.name ?? unit.characterId} {unit.tier > 1 ? `★${unit.tier}` : ""}
                    </span>
                  ))}
                </div>

                {match.augments.length > 0 ? (
                  <p className="text-xs text-fg/75">
                    Augments: {match.augments.join(", ")}
                  </p>
                ) : null}
              </Card>
            ))
          )}
        </div>
      </Card>

      {error ? <p className="text-sm text-danger">{error}</p> : null}
    </div>
  );
}
