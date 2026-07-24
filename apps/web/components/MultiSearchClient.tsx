"use client";

import type { components } from "@transcendence/api-client";
import Image from "next/image";
import Link from "next/link";
import { FormEvent, useEffect, useMemo, useState } from "react";

import { Button } from "@/components/ui/Button";
import { Badge } from "@/components/ui/Badge";
import { Card } from "@/components/ui/Card";
import { Select } from "@/components/ui/Select";
import { formatPercent, kdaColorClass, winRateColorClass } from "@/lib/format";
import { championDisplayName } from "@/lib/gameDisplay";
import { LOL_REGION_OPTIONS } from "@/lib/lolRegions";
import { parseLobbyText } from "@/lib/multiSearch";
import { rankTierColorClass, rankTierDisplayLabel } from "@/lib/ranks";
import { encodeRiotIdPath } from "@/lib/riotid";
import { roleDisplayLabel } from "@/lib/roles";
import { championIconUrl } from "@/lib/staticData";

type MultiSearchResponse = components["schemas"]["MultiSearchResponse"];
type MultiSearchResult = components["schemas"]["MultiSearchSummonerResult"];
type MultiSearchRank = components["schemas"]["MultiSearchRankInfo"];

type ChampionStatic = {
  version: string;
  champions: Record<string, { id: string; name: string }>;
};

function problemMessage(body: unknown): string | null {
  if (!body || typeof body !== "object") return null;
  const record = body as Record<string, unknown>;
  for (const key of ["detail", "message", "title"]) {
    if (typeof record[key] === "string" && record[key].trim()) return record[key].trim();
  }
  return null;
}

function rankLabel(rank: MultiSearchRank | null | undefined): string {
  if (!rank) return "Unranked";
  const division = ["MASTER", "GRANDMASTER", "CHALLENGER"].includes(rank.tier.toUpperCase())
    ? ""
    : ` ${rank.division}`;
  return `${rankTierDisplayLabel(rank.tier)}${division}, ${rank.leaguePoints} LP`;
}

function profileIconUrl(version: string, profileIconId: number): string {
  return `https://ddragon.leagueoflegends.com/cdn/${version}/img/profileicon/${profileIconId}.png`;
}

export function MultiSearchClient() {
  const [region, setRegion] = useState("na");
  const [lobbyText, setLobbyText] = useState("");
  const [response, setResponse] = useState<MultiSearchResponse | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [staticData, setStaticData] = useState<ChampionStatic | null>(null);
  const parsed = useMemo(() => parseLobbyText(lobbyText), [lobbyText]);

  useEffect(() => {
    let active = true;
    void fetch("/api/static/champions", { cache: "force-cache" })
      .then(async (result) => {
        if (!result.ok) return;
        const data = (await result.json()) as ChampionStatic;
        if (active) setStaticData(data);
      })
      .catch(() => undefined);
    return () => {
      active = false;
    };
  }, []);

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (parsed.summoners.length === 0 || busy) return;

    setBusy(true);
    setError(null);
    try {
      const result = await fetch("/api/trn/app/lol/summoners/multi-search", {
        method: "POST",
        headers: { "content-type": "application/json" },
        body: JSON.stringify({ region, summoners: parsed.summoners })
      });
      const body = (await result.json().catch(() => null)) as MultiSearchResponse | null;
      if (!result.ok || !body) {
        throw new Error(problemMessage(body) ?? "Team search is unavailable right now.");
      }
      setResponse(body);
    } catch (caught) {
      setResponse(null);
      setError(caught instanceof Error ? caught.message : "Team search is unavailable right now.");
    } finally {
      setBusy(false);
    }
  }

  const foundCount = response?.results.filter((result) => result.found).length ?? 0;

  return (
    <div className="grid gap-6">
      <header className="page-hero grid gap-5 p-5 md:grid-cols-[minmax(0,1fr)_minmax(320px,0.8fr)] md:p-7">
        <div className="max-w-2xl">
          <p className="type-kicker text-primary">Multi-search</p>
          <h1 className="mt-3 type-page-title">Compare your lobby.</h1>
          <p className="mt-3 max-w-[62ch] text-fg/72">
            Paste up to five Riot IDs to compare rank, main role, recent form, and champion pool.
          </p>
        </div>
        <div className="surface-subtle self-end rounded-card px-4 py-3">
          <p className="type-kicker text-muted">Accepted input</p>
          <p className="mt-2 text-sm leading-6 text-fg/78">
            One <span className="font-semibold text-fg">GameName#TAG</span> per line, comma-separated,
            or copied lobby join messages.
          </p>
        </div>
      </header>

      <Card className="p-5 md:p-6">
        <form onSubmit={submit} className="grid gap-4">
          <div className="grid gap-4 md:grid-cols-[minmax(0,1fr)_140px] md:items-start">
            <div>
              <label htmlFor="multi-search-lobby" className="type-kicker text-fg/70">
                Lobby Riot IDs
              </label>
              <textarea
                id="multi-search-lobby"
                value={lobbyText}
                onChange={(event) => setLobbyText(event.target.value)}
                rows={6}
                autoCorrect="off"
                autoCapitalize="off"
                spellCheck={false}
                placeholder={"Kronic#NA1\nPlayer Two#NA1 joined the lobby"}
                className="mt-2 w-full resize-y rounded-control border border-border bg-surface px-4 py-3 text-sm leading-6 text-fg shadow-inset outline-none transition placeholder:text-muted/65 focus:border-primary/60 focus:ring-2 focus:ring-primary/25"
              />
            </div>
            <div>
              <label className="type-kicker text-fg/70">Region</label>
              <Select
                value={region}
                onValueChange={setRegion}
                options={[...LOL_REGION_OPTIONS]}
                ariaLabel="Lobby region"
                className="mt-2 h-12 w-full"
              />
            </div>
          </div>

          <div className="flex flex-wrap items-center justify-between gap-3 border-t border-border/40 pt-4">
            <div className="flex flex-wrap items-center gap-2" aria-live="polite">
              <Badge className={parsed.summoners.length > 0 ? "border-success/35 text-success" : ""}>
                {parsed.summoners.length}/5 ready
              </Badge>
              {parsed.rejected.length > 0 ? (
                <span className="text-sm text-warning">
                  {parsed.rejected.length} line{parsed.rejected.length === 1 ? "" : "s"} need a #tag
                </span>
              ) : null}
              {parsed.truncated ? <span className="text-sm text-warning">Only the first five IDs will be searched.</span> : null}
            </div>
            <Button type="submit" disabled={parsed.summoners.length === 0 || busy}>
              {busy ? "Scouting…" : "Scout team"}
            </Button>
          </div>
          {error ? <p role="alert" className="text-sm text-danger">{error}</p> : null}
        </form>
      </Card>

      {response ? (
        <div className="grid gap-5" aria-live="polite">
          <TeamInsights response={response} foundCount={foundCount} />
          <ResultsTable results={response.results} region={region} staticData={staticData} />
        </div>
      ) : (
        <div className="rounded-card border border-dashed border-border px-6 py-10 text-center">
          <p className="type-section">Lobby comparison appears here</p>
          <p className="mx-auto mt-2 max-w-xl text-sm leading-6 text-muted">
            Results use stored profiles. Missing players are marked.
          </p>
        </div>
      )}
    </div>
  );
}

function TeamInsights({ response, foundCount }: { response: MultiSearchResponse; foundCount: number }) {
  const insights = response.teamInsights;
  return (
    <section className="grid gap-3 lg:grid-cols-[0.75fr_1.25fr]" aria-labelledby="team-read-heading">
      <Card className="p-5">
        <p className="type-kicker text-muted">Team read</p>
        <h2 id="team-read-heading" className={`mt-2 type-section ${rankTierColorClass(insights.averageRankLabel)}`}>
          {rankTierDisplayLabel(insights.averageRankLabel)} average
        </h2>
        <p className="mt-2 text-sm text-fg/68">Based on {foundCount} of {response.results.length} stored profiles.</p>
        <div className="mt-4 flex flex-wrap gap-2">
          {insights.roleCoverage.map((role) => <Badge key={role}>{roleDisplayLabel(role)}</Badge>)}
          {insights.roleCoverage.length === 0 ? <Badge>No known main roles</Badge> : null}
        </div>
      </Card>
      <Card className="p-5">
        <div className="flex flex-wrap items-start justify-between gap-3">
          <div>
            <p className="type-kicker text-muted">Role pressure</p>
            <h2 className="mt-2 type-section">Coverage and autofill signals</h2>
          </div>
          {insights.missingRoles.length > 0 ? (
            <Badge className="border-warning/35 text-warning">
              Missing {insights.missingRoles.map(roleDisplayLabel).join(", ")}
            </Badge>
          ) : (
            <Badge className="border-success/35 text-success">All roles covered</Badge>
          )}
        </div>
        {insights.potentialAutofills.length > 0 ? (
          <ul className="mt-4 grid gap-2">
            {insights.potentialAutofills.map((risk) => (
              <li key={`${risk.gameName}-${risk.tagLine}-${risk.note}`} className="surface-subtle rounded-control px-3 py-2.5 text-sm text-fg/78">
                <span className="font-semibold text-fg">{risk.gameName}#{risk.tagLine}</span>: {risk.note}
              </li>
            ))}
          </ul>
        ) : (
          <p className="mt-4 text-sm text-fg/68">No duplicate-main or missing-profile autofill signals in this lobby.</p>
        )}
      </Card>
    </section>
  );
}

function ResultsTable({
  results,
  region,
  staticData
}: {
  results: MultiSearchResult[];
  region: string;
  staticData: ChampionStatic | null;
}) {
  return (
    <Card className="overflow-hidden p-0">
      <div className="border-b border-border/50 px-5 py-4">
        <p className="type-kicker text-muted">Player comparison</p>
        <h2 className="mt-1 type-section">Rank, role, form, and champion pool</h2>
      </div>
      <div className="overflow-x-auto">
        <table className="w-full min-w-[920px] text-left text-sm">
          <thead className="type-overline text-muted">
            <tr className="border-b border-border/40 bg-surface-2/45">
              <th className="px-5 py-3">Player</th>
              <th className="px-4 py-3">Solo rank</th>
              <th className="px-4 py-3">Main role</th>
              <th className="px-4 py-3">Tracked form</th>
              <th className="px-4 py-3">Top champions</th>
            </tr>
          </thead>
          <tbody>
            {results.map((result) => (
              <PlayerRow
                key={`${result.gameName}-${result.tagLine}`}
                result={result}
                region={region}
                staticData={staticData}
              />
            ))}
          </tbody>
        </table>
      </div>
    </Card>
  );
}

function PlayerRow({ result, region, staticData }: { result: MultiSearchResult; region: string; staticData: ChampionStatic | null }) {
  const href = `/lol/summoners/${region}/${encodeRiotIdPath({ gameName: result.gameName, tagLine: result.tagLine })}`;
  return (
    <tr className="border-b border-border/30 align-middle last:border-b-0">
      <td className="px-5 py-4">
        <Link href={href} className="group flex items-center gap-3 rounded-md focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/35">
          {staticData && result.profileIconId != null ? (
            <Image src={profileIconUrl(staticData.version, result.profileIconId)} alt="" width={40} height={40} className="h-10 w-10 rounded-control border border-border/55" />
          ) : (
            <span className="inline-flex h-10 w-10 items-center justify-center rounded-control border border-border/55 bg-surface-2 text-muted">?</span>
          )}
          <span>
            <span className="block font-semibold text-fg group-hover:text-primary">{result.gameName}#{result.tagLine}</span>
            <span className="type-caption text-muted">{result.found ? `Level ${result.summonerLevel ?? "?"}` : "Not in stored data"}</span>
          </span>
        </Link>
      </td>
      <td className={`px-4 py-4 font-medium ${rankTierColorClass(result.soloRank?.tier)}`}>
        {rankLabel(result.soloRank)}
      </td>
      <td className="px-4 py-4">{roleDisplayLabel(result.primaryRole)}</td>
      <td className="px-4 py-4">
        {result.overviewStats ? (
          <div className="grid gap-1">
            <span className={winRateColorClass(result.overviewStats.winRate)}>
              {formatPercent(result.overviewStats.winRate)} WR
            </span>
            <span className={`type-caption ${kdaColorClass(result.overviewStats.kdaRatio)}`}>
              {result.overviewStats.kdaRatio.toFixed(2)} KDA, {result.overviewStats.totalMatches} games
            </span>
          </div>
        ) : <span className="text-muted">No tracked form</span>}
      </td>
      <td className="px-4 py-4">
        {(result.topChampions?.length ?? 0) > 0 ? (
          <div className="flex items-center gap-2">
            {result.topChampions!.slice(0, 3).map((champion) => {
              const identity = staticData?.champions[String(champion.championId)];
              return (
                <span key={champion.championId} className="group/champion relative" title={`${championDisplayName(identity)}: ${champion.games} games, ${formatPercent(champion.winRate)} WR`}>
                  {identity && staticData ? (
                    <Image src={championIconUrl(staticData.version, identity.id)} alt={identity.name} width={34} height={34} className="h-[34px] w-[34px] rounded-md border border-border/55" />
                  ) : (
                    <span className="inline-flex h-[34px] w-[34px] items-center justify-center rounded-md border border-border/55 bg-surface-2 text-xs text-muted">?</span>
                  )}
                </span>
              );
            })}
          </div>
        ) : <span className="text-muted">No champion sample</span>}
      </td>
    </tr>
  );
}
