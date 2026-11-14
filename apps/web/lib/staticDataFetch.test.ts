import { afterEach, expect, it, vi } from "vitest";
import { fetchStaticJson } from "./staticDataFetch";
import { fetchChampionMap, fetchRunesReforged } from "./staticData";

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
