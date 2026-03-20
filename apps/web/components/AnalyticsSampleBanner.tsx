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
          kicker: "Strong Sample",
          title: "There are plenty of recent games behind this view.",
          detail:
            "This page has enough current-patch data to trust the overall trend.",
          footnote:
            "Sample strength depends on patch age, selected region, and how many recent matches fit these filters."
        }
      : normalized.status === "low_sample"
        ? {
            card: "border-amber-400/40 bg-amber-500/10",
            badge: "border-amber-400/40 bg-amber-500/20 text-amber-50",
            meter: "bg-amber-300",
            kicker: "Limited Sample",
            title: "The trend is useful, but the sample size is still light.",
            detail:
              shortfall > 0
                ? `${shortfall} more recent games would make this read more dependable.`
                : "This view is close to a stronger sample size.",
            footnote:
              "Early patch trends can swing quickly while more matches are still being added."
          }
        : {
            card: "border-rose-400/40 bg-rose-500/10",
            badge: "border-rose-400/40 bg-rose-500/20 text-rose-50",
            meter: "bg-rose-300",
            kicker: "Very Early Data",
            title: "New-patch data is still too thin to call this reliably.",
            detail:
              "There are not enough recent games for this filter yet, so treat the numbers as early direction only.",
            footnote:
              "Check back after more matches have been played for this region and filter set."
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
          <h2 className="mt-3 type-section text-fg">
            {tone.title}
          </h2>
          <p className="type-ui mt-2 text-fg/80">{tone.detail}</p>
        </div>

        <div className="min-w-[144px] rounded-2xl border border-white/10 bg-black/15 px-4 py-3 text-right">
          <p className="type-kicker text-fg/55">Sample Strength</p>
          <p className="mt-1 text-3xl font-semibold text-fg">{coverage}%</p>
          <p className="type-ui mt-1 text-fg/65">of the recommended sample size</p>
        </div>
      </div>

      <div className="mt-4 grid gap-3 md:grid-cols-2">
        <div className="rounded-2xl border border-white/10 bg-black/15 px-4 py-3">
          <p className="type-kicker text-fg/55">Sample Size</p>
          <p className="mt-1 text-2xl font-semibold text-fg">{normalized.sampleSize}</p>
          <p className="type-ui mt-1 text-fg/65">recent matches in this view</p>
        </div>
        <div className="rounded-2xl border border-white/10 bg-black/15 px-4 py-3">
          <p className="type-kicker text-fg/55">Patch Age</p>
          <p className="mt-1 text-2xl font-semibold text-fg">{patchAgeLabel}</p>
          <p className="type-ui mt-1 text-fg/65">
            {normalized.isEarlyPatchWindow
              ? "early-patch volatility still applies"
              : "beyond the early-patch window"}
          </p>
        </div>
      </div>

      <div className="mt-4">
        <div className="type-kicker flex items-center justify-between text-fg/55">
          <span>Sample Progress</span>
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
        <p className="type-ui mt-3 text-fg/70">{tone.footnote}</p>
      </div>
    </Card>
  );
}
