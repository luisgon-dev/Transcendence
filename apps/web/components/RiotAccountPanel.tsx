"use client";

import { useEffect, useState } from "react";
import Link from "next/link";

import { Button } from "@/components/ui/Button";
import { Card } from "@/components/ui/Card";
import { platformRegionToSlug } from "@/lib/lolRegions";
import { encodeRiotIdPath } from "@/lib/riotid";
import { RiotRsoButton } from "@/components/RiotRsoButton";

type RiotAccountLink = {
  puuid: string;
  gameName: string;
  tagLine: string;
  platformRegion: string;
  linkedAtUtc: string;
  verifiedAtUtc: string;
  canUnlink: boolean;
};

export function RiotAccountPanel() {
  const [link, setLink] = useState<RiotAccountLink | null | undefined>(undefined);
  const [message, setMessage] = useState<string | null>(null);

  async function load() {
    const response = await fetch("/api/trn/user/users/me/riot-account", { cache: "no-store" });
    if (response.status === 404) {
      setLink(null);
      return;
    }
    if (!response.ok) {
      setLink(null);
      setMessage("Riot account status is temporarily unavailable.");
      return;
    }
    setLink((await response.json()) as RiotAccountLink);
  }

  useEffect(() => {
    const params = new URLSearchParams(window.location.search);
    if (params.get("riot") === "linked") setMessage("Riot account verified and linked.");
    if (params.has("riotError")) setMessage("Riot account linking could not be completed.");
    void load();
  }, []);

  async function unlink() {
    const response = await fetch("/api/trn/user/users/me/riot-account", { method: "DELETE" });
    if (!response.ok) {
      setMessage("This Riot identity is your only sign-in method and cannot be unlinked yet.");
      return;
    }
    setMessage("Riot account unlinked.");
    setLink(null);
  }

  return (
    <Card className="p-5">
      <div className="flex flex-wrap items-start justify-between gap-4">
        <div className="max-w-2xl">
          <p className="type-kicker text-primary">Verified main</p>
          <h2 className="mt-2 type-section">Riot account</h2>
          {link ? (
            <div className="mt-3">
              <Link
                href={`/lol/summoners/${platformRegionToSlug(link.platformRegion)}/${encodeRiotIdPath(link)}`}
                className="type-ui font-semibold text-fg hover:underline"
              >
                {link.gameName}#{link.tagLine}
              </Link>
              <p className="mt-1 text-sm text-muted">
                Verified with Riot · {link.platformRegion} · refreshed {new Date(link.verifiedAtUtc).toLocaleString()}
              </p>
            </div>
          ) : link === null ? (
            <p className="mt-3 text-sm leading-6 text-fg/72">
              Link Riot to establish your verified main and sign in without sharing your Riot password with Transcendence.
            </p>
          ) : (
            <p className="mt-3 text-sm text-muted">Checking Riot link…</p>
          )}
        </div>

        {link ? (
          link.canUnlink ? (
            <Button variant="outline" size="sm" onClick={() => void unlink()}>
              Unlink
            </Button>
          ) : (
            <span className="rounded-control border border-border px-3 py-2 text-xs text-muted">
              Primary sign-in
            </span>
          )
        ) : (
          <div className="min-w-[280px]">
            <RiotRsoButton mode="link" returnTo="/account/favorites" label="Link Riot account" />
          </div>
        )}
      </div>
      {message ? <p className="mt-3 text-sm text-muted" aria-live="polite">{message}</p> : null}
    </Card>
  );
}
