import { fetchAndConsumeWithTimeout } from "@/lib/fetchWithTimeout";
import { nextImageUrl } from "@/lib/nextImage";

export const ASSET_KINDS = ["champion", "item", "rune", "splash"] as const;
type AssetKind = (typeof ASSET_KINDS)[number];
export type AssetReadiness = {
  ready: boolean;
  checkedAt: number;
  durationMs: number;
  checks: Record<AssetKind, boolean>;
};

/** One bounded, single-flight synthetic probe. Public callers cannot select targets or bypass TTL. */
export class AssetReadinessProbe {
  private cached?: AssetReadiness;
  private pending?: Promise<AssetReadiness>;

  constructor(private readonly baseUrl = `http://127.0.0.1:${process.env.PORT || 3000}`) {}

  snapshot(): AssetReadiness | undefined { return this.cached; }

  get(): Promise<AssetReadiness> {
    if (this.pending) return this.pending;
    if (this.cached && Date.now() - this.cached.checkedAt < (this.cached.ready ? 30_000 : 5_000)) {
      return Promise.resolve(this.cached);
    }
    this.pending = this.run().then(result => { this.cached = result; return result; })
      .finally(() => { this.pending = undefined; });
    return this.pending;
  }

  private async run(): Promise<AssetReadiness> {
    const started = Date.now();
    const checks: Record<AssetKind, boolean> = { champion: false, item: false, rune: false, splash: false };
    const remaining = () => Math.max(1, 8_000 - (Date.now() - started));
    try {
      const version = await fetchAndConsumeWithTimeout(
        new URL("/api/static/champions", this.baseUrl), { cache: "no-store" },
        async response => {
          if (!response.ok) throw new Error("Static metadata unavailable");
          const catalog = await response.json() as { version?: unknown; champions?: Record<string, { id?: string }> };
          const latest = catalog.version;
          if (typeof latest !== "string" || !/^\d+\.\d+\.\d+$/.test(latest)) throw new Error("Invalid static version");
          if (catalog.champions?.["103"]?.id !== "Ahri") throw new Error("Champion metadata unavailable");
          return latest;
        }, { timeoutMs: remaining() }
      );
      const assets: Array<[AssetKind, string, number]> = [
        ["champion", `https://ddragon.leagueoflegends.com/cdn/${version}/img/champion/Ahri.png`, 64],
        ["item", `https://ddragon.leagueoflegends.com/cdn/${version}/img/item/1001.png`, 32],
        ["rune", "https://ddragon.leagueoflegends.com/cdn/img/perk-images/Styles/Precision/Conqueror/Conqueror.png", 32],
        ["splash", "https://ddragon.leagueoflegends.com/cdn/img/champion/splash/Ahri_0.jpg", 384]
      ];
      await Promise.all(assets.map(async ([kind, url, width]) => {
        try {
          checks[kind] = await fetchAndConsumeWithTimeout(
            new URL(nextImageUrl(url, width), this.baseUrl), { cache: "no-store", headers: { accept: "image/webp" } },
            async response => {
              const body = await response.arrayBuffer();
              return response.ok && (response.headers.get("content-type") ?? "").startsWith("image/") && body.byteLength > 0;
            },
            { timeoutMs: remaining() }
          );
        } catch { checks[kind] = false; }
      }));
    } catch { /* Bounded labels and no upstream URLs or credentials in the public response. */ }
    return { ready: ASSET_KINDS.every(kind => checks[kind]), checks,
      checkedAt: Date.now(), durationMs: Date.now() - started };
  }

  renderPrometheus(): string {
    const lines = ["# TYPE transcendence_image_probe_ready gauge"];
    for (const kind of ASSET_KINDS) lines.push(
      `transcendence_image_probe_ready{kind="${kind}"} ${this.cached?.checks[kind] ? 1 : 0}`
    );
    lines.push("# TYPE transcendence_image_probe_last_check_unixtime_seconds gauge",
      `transcendence_image_probe_last_check_unixtime_seconds ${(this.cached?.checkedAt ?? 0) / 1000}`,
      "# TYPE transcendence_image_probe_duration_seconds gauge",
      `transcendence_image_probe_duration_seconds ${(this.cached?.durationMs ?? 0) / 1000}`);
    return lines.join("\n") + "\n";
  }
}

const globalState = globalThis as typeof globalThis & { __trnAssetReadiness?: AssetReadinessProbe };
export function getAssetReadinessProbe(): AssetReadinessProbe {
  return globalState.__trnAssetReadiness ??= new AssetReadinessProbe();
}
