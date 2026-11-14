import { beforeEach, expect, it, vi } from "vitest";
import { fetchChampionMap, fetchItemMap, fetchSummonerSpellMap, fetchRunesReforged } from "@/lib/staticData";
import { GET as champions } from "./champions/route";
import { GET as items } from "./items/route";
import { GET as spells } from "./spells/route";
import { GET as runes } from "./runes/route";

vi.mock("next/headers", () => ({ headers: vi.fn(async () => new Headers()) }));
vi.mock("@/lib/staticData", () => ({ fetchChampionMap: vi.fn(), fetchItemMap: vi.fn(),
  fetchSummonerSpellMap: vi.fn(), fetchRunesReforged: vi.fn() }));
const loaders = [fetchChampionMap, fetchItemMap, fetchSummonerSpellMap, fetchRunesReforged];
const handlers = [champions, items, spells, runes];
beforeEach(() => vi.clearAllMocks());

it("never caches a temporary metadata failure", async () => {
  for (const loader of loaders) vi.mocked(loader).mockRejectedValue(new Error("Temporarily unavailable"));
  for (const handler of handlers) {
    const response = await handler();
    expect(response.status).toBe(502);
    expect(response.headers.get("cache-control")).toBe("no-store");
  }
});

it("retains shared caching for successful catalog responses", async () => {
  vi.mocked(fetchChampionMap).mockResolvedValue({ version: "16.22.1", champions: {} });
  vi.mocked(fetchItemMap).mockResolvedValue({ version: "16.22.1", items: {} });
  vi.mocked(fetchSummonerSpellMap).mockResolvedValue({ version: "16.22.1", spells: {} });
  vi.mocked(fetchRunesReforged).mockResolvedValue({ version: "16.22.1", runeById: {}, styleById: {}, runeSortById: {}, trees: [] });
  for (const handler of handlers) {
    const response = await handler();
    expect(response.status).toBe(200);
    expect(response.headers.get("cache-control")).toContain("s-maxage=86400");
  }
});
