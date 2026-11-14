import { afterEach, expect, it, vi } from "vitest";
import { fetchStaticJson } from "./staticDataFetch";
import { fetchChampionMap, fetchRunesReforged, runeIconUrl } from "./staticData";

afterEach(() => { vi.useRealTimers(); vi.unstubAllGlobals(); });

it("bounds a stalled JSON body and allows a subsequent healthy request", async () => {
  vi.useFakeTimers();
  const fetch = vi.fn(async () => new Response(new ReadableStream({ start() {} })));
  vi.stubGlobal("fetch", fetch);
  const result = fetchStaticJson("https://static.invalid/data.json", "Unavailable").catch(error => error);
  await vi.advanceTimersByTimeAsync(4001);
  expect(await result).toMatchObject({ name: "AbortError" });
  fetch.mockResolvedValue(Response.json({ ready: true }));
  expect(await fetchStaticJson("https://static.invalid/data.json", "Unavailable")).toEqual({ ready: true });
});

it("retains Data Dragon runes and stat shards when optional enrichment stalls", async () => {
  vi.useFakeTimers();
  vi.stubGlobal("fetch", vi.fn(async (input: RequestInfo | URL) => {
    if (String(input).includes("versions.json")) return Response.json(["16.22.1"]);
    if (String(input).includes("runesReforged.json")) return Response.json([
      { id: 8000, name: "Precision", icon: "precision.png", slots: [{ runes: [{ id: 8005, name: "Press the Attack", icon: "pta.png" }] }] }
    ]);
    return new Response(new ReadableStream({ start() {} }));
  }));
  const result = fetchRunesReforged();
  await vi.advanceTimersByTimeAsync(2001);
  const runes = await result;
  expect(runes.runeById["8005"].name).toBe("Press the Attack");
  expect(runes.runeById["5008"].name).toBeTruthy();
  expect(runes.trees).toHaveLength(1);
});

it("rejects an invalid version before constructing asset URLs", async () => {
  const fetch = vi.fn(async () => Response.json(["https://unexpected.invalid/"]));
  vi.stubGlobal("fetch", fetch);
  await expect(fetchChampionMap()).rejects.toThrow("Invalid Data Dragon version");
  expect(fetch).toHaveBeenCalledTimes(1);
});

it("keeps standard runes and shards on Data Dragon while enriching only missing perks", async () => {
  vi.stubGlobal("fetch", vi.fn(async (input: RequestInfo | URL) => {
    if (String(input).includes("versions.json")) return Response.json(["16.22.1"]);
    if (String(input).includes("runesReforged.json")) return Response.json([
      { id: 8000, name: "Precision", icon: "precision.png", slots: [{ runes: [
        { id: 8010, name: "Conqueror", icon: "perk-images/Styles/Precision/Conqueror/Conqueror.png" }
      ] }] }
    ]);
    return Response.json([
      { id: 8010, name: "Conqueror", iconPath: "/lol-game-data/assets/v1/perk-images/styles/precision/conqueror/conqueror.png" },
      { id: 5008, name: "Adaptive Force", iconPath: "/lol-game-data/assets/v1/perk-images/statmods/statmodsadaptiveforceicon.png" },
      { id: 99999, name: "Extra Perk", iconPath: "/lol-game-data/assets/v1/perk-images/extra.png" }
    ]);
  }));
  const result = await fetchRunesReforged();
  expect(runeIconUrl(result.runeById["8010"].icon)).toBe(
    "https://ddragon.leagueoflegends.com/cdn/img/perk-images/Styles/Precision/Conqueror/Conqueror.png");
  expect(runeIconUrl(result.runeById["5008"].icon)).toBe(
    "https://ddragon.leagueoflegends.com/cdn/img/perk-images/StatMods/StatModsAdaptiveForceIcon.png");
  expect(runeIconUrl(result.runeById["5011"].icon)).toContain("StatModsHealthPlusIcon.png");
  expect(result.runeSortById["5008"]).toBeGreaterThanOrEqual(1000);
  expect(result.runeById["99999"].icon).toContain("raw.communitydragon.org/latest/");
});
