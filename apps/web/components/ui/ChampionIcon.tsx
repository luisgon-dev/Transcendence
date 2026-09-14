import { fixedImageProps } from "@/lib/nextImage";
import { championIconUrl } from "@/lib/staticData";

/**
 * Champion square at a fixed pixel size.
 *
 * A plain <img> on purpose: see lib/nextImage.ts. The optimizer still resizes
 * and re-encodes the Data Dragon PNG, so this costs the same bytes as
 * `next/image` and none of the per-instance hydration — which is what a
 * 170-row table notices.
 */
export function ChampionIcon({
  version,
  championSlug,
  alt,
  size,
  className
}: {
  version: string;
  championSlug: string;
  alt: string;
  size: number;
  className?: string;
}) {
  const { src, srcSet } = fixedImageProps(championIconUrl(version, championSlug), size);

  return (
    // eslint-disable-next-line @next/next/no-img-element -- deliberate: see lib/nextImage.ts
    <img
      src={src}
      srcSet={srcSet}
      alt={alt}
      width={size}
      height={size}
      loading="lazy"
      decoding="async"
      className={className}
      // next/image sets this too: keeps the alt text from flashing inside the
      // 30px box before the square arrives.
      style={{ color: "transparent" }}
    />
  );
}
