import { Badge } from "@/components/ui/Badge";
import { Card } from "@/components/ui/Card";
import {
  analyticsSampleCoveragePercent,
  analyticsSampleShortfall,
  type AnalyticsSampleLike,
  normalizeAnalyticsSample
} from "@/lib/analyticsSample";

type AnalyticsSampleBannerProps = {
  sample: AnalyticsSampleLike;
};

export function AnalyticsSampleBanner({ sample }: AnalyticsSampleBannerProps) {
  const normalized = normalizeAnalyticsSample(sample);
  if (!normalized) return null;

  const coverage = analyticsSampleCoveragePercent(normalized);
  const shortfall = analyticsSampleShortfall(normalized);
  const patchAgeLabel =
    normalized.patchAgeHours >= 48
      ? `${Math.round(normalized.patchAgeHours / 24)}d old`
      : `${Math.round(normalized.patchAgeHours)}h old`;

  const tone =
    normalized.status === "sufficient"
      ? {
          card: "border-emerald-400/35 bg-emerald-500/10",
          badge: "border-emerald-400/35 bg-emerald-500/20 text-emerald-50",
          meter: "bg-emerald-300",
          kicker: "Stable Sample",
          title: "Confidence is driven by a healthy current-patch sample.",
          detail:
            "Current filters have enough exposure for the patch to treat this read as broadly stable.",
          footnote:
            "Primary drivers: current-patch game count, selected region, and how old the patch is."
        }
      : normalized.status === "low_sample"
        ? {
            card: "border-amber-400/40 bg-amber-500/10",
            badge: "border-amber-400/40 bg-amber-500/20 text-amber-50",
            meter: "bg-amber-300",
            kicker: "Low Exposure",
            title: "The shape of the data is visible, but the sample is still filling in.",
            detail:
              shortfall > 0
                ? `${shortfall} more current-patch games would bring this view to the recommended confidence floor.`
                : "Exposure is close to the recommended confidence floor.",
            footnote:
              "Treat edge cases cautiously while ingestion catches up during the patch window."
          }
        : {
            card: "border-rose-400/40 bg-rose-500/10",
            badge: "border-rose-400/40 bg-rose-500/20 text-rose-50",
            meter: "bg-rose-300",
            kicker: "No Current Data",
            title: "This view is waiting on current-patch match ingestion.",
            detail:
              "There are not enough current-patch games yet for this slice, so the page is mostly showing structure instead of a reliable read.",
            footnote:
              "Check again after more matches are collected for this region and filter set."
          };

  return (
    <Card className={`overflow-hidden p-4 md:p-5 ${tone.card}`}>
      <div className="flex flex-col gap-4 lg:flex-row lg:items-start lg:justify-between">
        <div className="max-w-2xl">
          <div className="flex flex-wrap items-center gap-2">
            <Badge className={tone.badge}>{tone.kicker}</Badge>
            <Badge>{normalized.isEarlyPatchWindow ? "Early Patch Window" : "Current Patch"}</Badge>
            <Badge>{patchAgeLabel}</Badge>
          </div>
          <h2 className="mt-3 font-[var(--font-sora)] text-lg font-semibold tracking-tight text-fg">
            {tone.title}
          </h2>
          <p className="mt-1 text-sm text-fg/80">{tone.detail}</p>
        </div>

        <div className="min-w-[144px] rounded-2xl border border-white/10 bg-black/15 px-4 py-3 text-right">
          <p className="text-[11px] uppercase tracking-[0.24em] text-fg/55">Confidence</p>
          <p className="mt-1 text-3xl font-semibold text-fg">{coverage}%</p>
          <p className="text-xs text-fg/65">of target sample reached</p>
        </div>
      </div>

      <div className="mt-4 grid gap-3 md:grid-cols-3">
        <div className="rounded-2xl border border-white/10 bg-black/15 px-4 py-3">
          <p className="text-[11px] uppercase tracking-[0.24em] text-fg/55">Current Sample</p>
          <p className="mt-1 text-2xl font-semibold text-fg">{normalized.sampleSize}</p>
          <p className="text-xs text-fg/65">current-patch matches in this slice</p>
        </div>
        <div className="rounded-2xl border border-white/10 bg-black/15 px-4 py-3">
          <p className="text-[11px] uppercase tracking-[0.24em] text-fg/55">Confidence Floor</p>
          <p className="mt-1 text-2xl font-semibold text-fg">
            {normalized.minimumRecommendedSampleSize}
          </p>
          <p className="text-xs text-fg/65">
            {shortfall > 0 ? `${shortfall} matches short of target` : "recommended sample met"}
          </p>
        </div>
        <div className="rounded-2xl border border-white/10 bg-black/15 px-4 py-3">
          <p className="text-[11px] uppercase tracking-[0.24em] text-fg/55">Patch Age</p>
          <p className="mt-1 text-2xl font-semibold text-fg">{patchAgeLabel}</p>
          <p className="text-xs text-fg/65">
            {normalized.isEarlyPatchWindow
              ? "early-patch volatility still applies"
              : "beyond the early-patch window"}
          </p>
        </div>
      </div>

      <div className="mt-4">
        <div className="flex items-center justify-between text-[11px] uppercase tracking-[0.24em] text-fg/55">
          <span>Exposure</span>
          <span>
            {normalized.sampleSize} / {normalized.minimumRecommendedSampleSize}
          </span>
        </div>
        <div className="mt-2 h-2.5 overflow-hidden rounded-full bg-black/20">
          <div
            className={`h-full rounded-full transition-all ${tone.meter}`}
            style={{ width: `${coverage}%` }}
          />
        </div>
        <p className="mt-3 text-xs text-fg/70">{tone.footnote}</p>
      </div>
    </Card>
  );
}
