import { encodeRiotIdPath } from "@/lib/riotid";
import { normalizeLolRegionSlug } from "@/lib/lolRegions";

/** Carry only public scouting context; never forward arbitrary query parameters or auth state. */
export function scoutingLinks(target: { region: string; gameName: string; tagLine: string; queue?: string }) {
  const region = normalizeLolRegionSlug(target.region);
  const riotId = `${target.gameName}#${target.tagLine}`;
  const context = new URLSearchParams({ region, riotId });
  const profileContext = new URLSearchParams();
  if (target.queue) { context.set("queue", target.queue); profileContext.set("queue", target.queue); }
  const query = profileContext.toString();
  return {
    profile: `/lol/summoners/${region}/${encodeRiotIdPath(target)}${query ? `?${query}` : ""}`,
    live: `/lol/live?${context}`,
    multi: `/lol/multi-search?${context}`
  };
}
