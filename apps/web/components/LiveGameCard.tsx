"use client";

import { useState } from "react";

import { Button } from "@/components/ui/Button";
import { Card } from "@/components/ui/Card";

type LiveGameResponseDto = {
  state?: string;
  message?: string;
  [k: string]: unknown;
};

export function LiveGameCard({
  region,
  gameName,
  tagLine
}: {
  region: string;
  gameName: string;
  tagLine: string;
}) {
  const [busy, setBusy] = useState(false);
  const [data, setData] = useState<LiveGameResponseDto | null>(null);
  const [error, setError] = useState<string | null>(null);

  async function check() {
    setBusy(true);
    setError(null);
    try {
      const res = await fetch(
        `/api/trn/app/summoners/${encodeURIComponent(region)}/${encodeURIComponent(
          gameName
        )}/${encodeURIComponent(tagLine)}/live-game`,
        { cache: "no-store" }
      );

      const json = (await res.json().catch(() => null)) as LiveGameResponseDto | null;
      if (!res.ok) {
        setError(
          (json?.message as string | undefined) ??
            `Live game request failed (${res.status}).`
        );
        return;
      }

      setData(json);
    } catch (e) {
      setError(e instanceof Error ? e.message : "Live game error.");
    } finally {
      setBusy(false);
    }
  }

  return (
    <Card className="p-5">
      <div className="flex items-center justify-between">
        <div>
          <h3 className="font-[var(--font-sora)] text-lg font-semibold">
            Live Game
          </h3>
          <p className="mt-1 text-sm text-fg/75">
            Check whether this player is currently in a game.
          </p>
        </div>
        <Button variant="outline" onClick={check} disabled={busy}>
          {busy ? "Checking..." : "Check"}
        </Button>
      </div>

      {error ? <p className="mt-3 text-sm text-red-300">{error}</p> : null}

      {data ? (
        <pre className="mt-4 max-h-[360px] overflow-auto rounded-lg border border-border/60 bg-black/30 p-3 text-xs text-fg/85">
          {JSON.stringify(data, null, 2)}
        </pre>
      ) : (
        <p className="mt-4 text-sm text-muted">
          No live game data loaded yet.
        </p>
      )}
    </Card>
  );
}

