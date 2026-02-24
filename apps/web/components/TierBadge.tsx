import { cn } from "@/lib/cn";
import {
  tierBgClass,
  tierBorderClass,
  tierColorClass,
  type UITierGrade
} from "@/lib/tierlist";

export function TierBadge({
  tier,
  size = "sm",
  className
}: {
  tier: UITierGrade;
  size?: "sm" | "md";
  className?: string;
}) {
  return (
    <span
      className={cn(
        "inline-flex items-center justify-center rounded-md border font-semibold",
        tierColorClass(tier),
        tierBgClass(tier),
        tierBorderClass(tier),
        size === "md" ? "min-w-[2rem] px-2 py-1 text-sm" : "min-w-[1.5rem] px-1.5 py-0.5 text-xs",
        className
      )}
    >
      {tier}
    </span>
  );
}
