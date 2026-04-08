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
          card: "border-success/35 bg-success/10",
          badge: "border-success/35 bg-success/20 text-fg",
          kicker: "Stable sample",
          summary: "Enough recent games to trust the current trend."
        }
      : normalized.status === "low_sample"
        ? {
            card: "border-warning/40 bg-warning/10",
            badge: "border-warning/40 bg-warning/20 text-fg",
            kicker: "Light sample",
            summary:
              shortfall > 0
                ? `${shortfall} more recent games would make this read steadier.`
                : "Useful direction, but the sample is still filling in."
          }
        : {
            card: "border-danger/40 bg-danger/10",
            badge: "border-danger/40 bg-danger/20 text-fg",
            kicker: "Early sample",
            summary: "Treat this as early direction until more current-patch games land."
          };
  const phaseLabel =
    normalized.patchPhase === "bootstrap"
      ? "Patch launch"
      : normalized.patchPhase === "provisional"
        ? "Patch settling"
        : normalized.patchPhase === "maturing"
          ? "Patch maturing"
          : "Patch stable";

  return (
    <Card className={`overflow-hidden px-4 py-3 md:px-4 md:py-3.5 ${tone.card}`}>
      <div className="flex flex-col gap-3 lg:flex-row lg:items-center lg:justify-between">
        <div className="min-w-0">
          <div className="flex flex-wrap items-center gap-2">
            <Badge className={tone.badge}>{tone.kicker}</Badge>
            <Badge>{phaseLabel}</Badge>
            <span className="type-ui text-fg/80">{tone.summary}</span>
          </div>
        </div>

        <div className="flex flex-wrap items-center gap-2">
          <span className="surface-chip rounded-full px-3 py-1.5 text-xs text-fg/72">
            {normalized.sampleSize} games
          </span>
          <span className="surface-chip rounded-full px-3 py-1.5 text-xs text-fg/72">
            {patchAgeLabel}
          </span>
          <span className="surface-chip rounded-full px-3 py-1.5 text-xs text-fg/72">
            {coverage}% of target
          </span>
        </div>
      </div>
    </Card>
  );
}
