// Next's image optimizer, addressed directly.
//
// `next/image` is a client component. It earns that cost when it is managing
// responsive `sizes`, a blur placeholder, priority hints or fill layout — and
// it does not when it renders a fixed 30px champion square 170 times in a
// table. On /lol/tierlist those instances measured −68ms TBT and −99ms of
// script evaluation once replaced by a plain <img>. The bytes still matter
// (Data Dragon serves 120px PNGs; unoptimized, the tier list would pull
// megabytes of icons), so keep the optimizer and drop only the component.
//
// The optimizer validates `w` against `images.deviceSizes` + `images.imageSizes`
// and `q` against `images.qualities`, both from next.config.mjs — an
// unconfigured value is a 400, so the widths below are pinned by a test.

/** Default `images.imageSizes` — the widths the optimizer allows for small assets. */
const IMAGE_SIZES = [16, 32, 48, 64, 96, 128, 256, 384] as const;

/** Must be listed in `images.qualities` in next.config.mjs. */
const DEFAULT_QUALITY = 75;

/** Smallest configured width that still covers `target`, or the largest available. */
export function nextImageWidth(target: number): number {
  return IMAGE_SIZES.find((width) => width >= target) ?? IMAGE_SIZES[IMAGE_SIZES.length - 1];
}

export function nextImageUrl(src: string, width: number, quality: number = DEFAULT_QUALITY): string {
  return `/_next/image?url=${encodeURIComponent(src)}&w=${width}&q=${quality}`;
}

/**
 * `src` + `srcSet` for a fixed-size image, matching what `next/image` emits for
 * the same dimensions: a 1x and a 2x candidate, with 2x as the `src` fallback.
 */
export function fixedImageProps(
  src: string,
  renderedSize: number,
  quality: number = DEFAULT_QUALITY
): { src: string; srcSet: string } {
  const oneX = nextImageWidth(renderedSize);
  const twoX = nextImageWidth(renderedSize * 2);

  return {
    src: nextImageUrl(src, twoX, quality),
    srcSet: `${nextImageUrl(src, oneX, quality)} 1x, ${nextImageUrl(src, twoX, quality)} 2x`
  };
}
