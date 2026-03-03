import { Badge } from "@/components/ui/Badge";
import { Card } from "@/components/ui/Card";
import {
  type AnalyticsSampleLike,
  normalizeAnalyticsSample
} from "@/lib/analyticsSample";

type AnalyticsSampleBannerProps = {
  sample: AnalyticsSampleLike;
};

export function AnalyticsSampleBanner({ sample }: AnalyticsSampleBannerProps) {
  const normalized = normalizeAnalyticsSample(sample);
  if (!normalized || normalized.status === "sufficient") return null;

  if (normalized.status === "no_data") {
    return (
      <Card className="border-primary/45 bg-primary/10 p-4">
        <p className="text-sm font-medium text-primary">
          No current-patch analytics samples yet.
        </p>
        <p className="mt-1 text-xs text-fg/85">
          Data collection is running in early-patch high-ingestion mode. Check back soon.
        </p>
      </Card>
    );
  }

  return (
    <Card className="border-amber-400/45 bg-amber-500/10 p-4">
      <div className="flex flex-wrap items-center gap-2">
        <Badge className="border-amber-400/45 bg-amber-500/20 text-amber-100">
          Early Patch
        </Badge>
        <p className="text-sm font-medium text-amber-100">
          Low sample size: {normalized.sampleSize} / {normalized.minimumRecommendedSampleSize}
        </p>
      </div>
      <p className="mt-1 text-xs text-fg/85">
        Insights are based on limited current-patch games and will stabilize as more matches are ingested.
      </p>
    </Card>
  );
}
