"use client";

import { useState } from "react";

import { Button } from "@/components/ui/Button";

export function FavoriteButton({
  region,
  gameName,
  tagLine
}: {
  region: string;
  gameName: string;
  tagLine: string;
}) {
  const [busy, setBusy] = useState(false);
  const [msg, setMsg] = useState<string | null>(null);

  async function add() {
    setBusy(true);
    setMsg(null);
    try {
      const res = await fetch("/api/trn/user/users/me/favorites", {
        method: "POST",
        headers: { "content-type": "application/json" },
        body: JSON.stringify({ region, gameName, tagLine })
      });

      if (res.status === 401) {
        setMsg("Login required to save favorites.");
        return;
      }

      if (!res.ok) {
        const json = (await res.json().catch(() => null)) as
          | { message?: string; requestId?: string }
          | null;
        const msg =
          json?.message ?? `Failed to add favorite (${res.status}).`;
        const rid = json?.requestId ? ` (Request ID: ${json.requestId})` : "";
        setMsg(`${msg}${rid}`);
        return;
      }

      setMsg("Saved to favorites.");
    } catch (e) {
      setMsg(e instanceof Error ? e.message : "Failed to add favorite.");
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="flex items-center gap-3">
      <Button variant="outline" size="sm" onClick={add} disabled={busy}>
        {busy ? "Saving..." : "Add Favorite"}
      </Button>
      {msg ? <span className="text-xs text-muted">{msg}</span> : null}
    </div>
  );
}

