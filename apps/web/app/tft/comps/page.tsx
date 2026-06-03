import { BackendErrorCard } from "@/components/BackendErrorCard";
import { TftCompList } from "@/components/TftCompList";
import { Button } from "@/components/ui/Button";
import { Toolbar } from "@/components/ui/Toolbar";
import { fetchBackendJson } from "@/lib/backendCall";
import { getBackendBaseUrl, getErrorVerbosity } from "@/lib/env";
import { TFT_RANK_OPTIONS, TFT_REGION_OPTIONS, type TftCompListItem } from "@/lib/tft";

export default async function TftCompsPage({
  searchParams
}: {
  searchParams?: Promise<{ rankTier?: string; region?: string }>;
}) {
  const resolved = searchParams ? await searchParams : undefined;
  const rankTier = resolved?.rankTier?.trim().toUpperCase() || "EMERALD_PLUS";
  const region = resolved?.region?.trim().toUpperCase() || "ALL";
  const qs = new URLSearchParams();
  if (rankTier !== "EMERALD_PLUS") qs.set("rankTier", rankTier);
  if (region !== "ALL") qs.set("region", region);

  const result = await fetchBackendJson<TftCompListItem[]>(
    `${getBackendBaseUrl()}/api/tft/analytics/comps${qs.toString() ? `?${qs.toString()}` : ""}`,
    { next: { revalidate: 60 * 10 } }
  );

  const verbosity = getErrorVerbosity();

  return (
    <div className="grid gap-4">
      <Toolbar
        eyebrow="TFT Analytics"
        title="Meta Comps"
        meta={<span>Strongest boards for the live set, sorted your way</span>}
        filters={
          <form className="flex flex-wrap items-center gap-2" action="/tft/comps" method="get">
            <select name="rankTier" aria-label="Rank tier" defaultValue={rankTier} className="control-select max-w-[200px]">
              {TFT_RANK_OPTIONS.map((option) => (
                <option key={option} value={option}>
                  {option}
                </option>
              ))}
            </select>
            <select name="region" aria-label="Region" defaultValue={region} className="control-select max-w-[160px]">
              {TFT_REGION_OPTIONS.map((option) => (
                <option key={option} value={option}>
                  {option}
                </option>
              ))}
            </select>
            <Button type="submit" size="sm">Apply</Button>
          </form>
        }
      />

      {result.ok ? (
        <TftCompList comps={result.body ?? []} qs={qs.toString()} />
      ) : (
        <BackendErrorCard
          title="TFT Meta Comps"
          message={
            result.errorKind === "timeout"
              ? "Comps are taking too long to load."
              : result.errorKind === "unreachable"
                ? "We couldn't reach the TFT service right now."
                : "We couldn't load TFT comps."
          }
          hint="Try again in a moment."
          requestId={result.requestId}
          detail={
            verbosity === "verbose"
              ? JSON.stringify({ status: result.status, errorKind: result.errorKind }, null, 2)
              : null
          }
        />
      )}
    </div>
  );
}
