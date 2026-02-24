import Image from "next/image";

import { cn } from "@/lib/cn";
import { championIconUrl } from "@/lib/staticData";

export function ChampionPortrait({
  championSlug,
  championName,
  version,
  size = 32,
  showName = false,
  className
}: {
  championSlug: string;
  championName: string;
  version: string;
  size?: number;
  showName?: boolean;
  className?: string;
}) {
  return (
    <div className={cn("flex items-center gap-2", className)}>
      <Image
        src={championIconUrl(version, championSlug)}
        alt={championName}
        width={size}
        height={size}
        className="rounded-md"
      />
      {showName ? (
        <span className="truncate text-sm font-medium">{championName}</span>
      ) : null}
    </div>
  );
}
